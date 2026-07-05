using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Communication.Email;
using Azure.Storage.Blobs;
using BakedManila.Api.Auth;
using BakedManila.Api.Data;
using BakedManila.Api.Middleware;
using BakedManila.Api.Services;
using BakedManila.Core.Data;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Get a token from POST /api/auth/login, then paste it here.",
        };
        return Task.CompletedTask;
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "600";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("orders", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
            }));
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
            }));
    options.AddPolicy("lookup", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
            }));
});

builder.Services.AddDbContext<BakedManilaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BakedManila")));

builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<BakedManilaDbContext>();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrEmpty(jwtSigningKey))
{
    throw new InvalidOperationException("Missing Jwt:SigningKey configuration.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("ViteDev", policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

builder.Services.AddScoped<IProductRepository, EfProductRepository>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IPaymentMethod, ManualPayment>();

var emailConnectionString = builder.Configuration["Email:ConnectionString"];
if (!string.IsNullOrEmpty(emailConnectionString))
{
    builder.Services.AddSingleton(new EmailClient(emailConnectionString));
    builder.Services.AddScoped<INotificationSender, AcsEmailNotificationSender>();
}
else
{
    builder.Services.AddScoped<INotificationSender, LoggingNotificationSender>();
}

builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton(TimeProvider.System);

var imagesProvider = builder.Configuration["Images:Provider"];

// Outside Development, the image provider must be set deliberately — an unconfigured value must
// not silently fall back to the local FileSystem store in production. Non-dev environments that
// explicitly configure a provider (e.g. integration tests running as "Testing" with "FileSystem")
// remain allowed; only an unset/empty value is rejected.
if (!builder.Environment.IsDevelopment() && string.IsNullOrEmpty(imagesProvider))
{
    throw new InvalidOperationException("Images:Provider must be 'AzureBlob' outside Development.");
}

var imagesRoot = builder.Configuration["Images:FileSystemRoot"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "images");

if (imagesProvider == "AzureBlob")
{
    builder.Services.AddSingleton<IImageStore>(_ => new AzureBlobImageStore(
        new BlobContainerClient(
            builder.Configuration.GetConnectionString("BlobStorage")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:BlobStorage."),
            "product-images")));
}
else
{
    builder.Services.AddSingleton<IImageStore>(_ => new FileSystemImageStore(imagesRoot));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await DevSeeder.MigrateAndSeedAsync(app.Services, app.Configuration, CancellationToken.None);
}
else if (app.Configuration.GetValue<bool>("Migrations:ApplyAtStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<BakedManilaDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    // App Service terminates TLS and fronts this app with its own proxy, which sets these
    // headers itself — clearing the known network/proxy lists (loopback-only by default)
    // trusts that single hop. Clear() is required: the properties are get-only, so an empty
    // collection initializer would silently keep the defaults. This is only safe because
    // App Service is the sole path to this app; never do this behind a proxy an attacker
    // could bypass or spoof.
    forwardedOptions.KnownIPNetworks.Clear();
    forwardedOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedOptions);

    // Must come after ForwardedHeaders: HSTS only emits on HTTPS requests, and behind
    // App Service that is only visible via the rewritten X-Forwarded-Proto scheme.
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseCors("ViteDev");
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("BakedManila API"));

    Directory.CreateDirectory(imagesRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(imagesRoot),
        RequestPath = "/images",
    });
}

var spaIndex = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
if (File.Exists(spaIndex))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallback("{*path}", async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/openapi")
            || context.Request.Path.StartsWithSegments("/scalar")
            || context.Request.Path.StartsWithSegments("/images"))
        {
            // Unmatched API-shaped route: 404 here, upgraded to problem+json by UseStatusCodePages.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(spaIndex);
    });
}

app.MapControllers();

app.Run();

public partial class Program;
