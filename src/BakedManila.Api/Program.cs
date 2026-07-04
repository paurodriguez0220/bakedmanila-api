using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BakedManila.Api.Auth;
using BakedManila.Api.Data;
using BakedManila.Api.Middleware;
using BakedManila.Core.Data;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();

// TODO(Plan 5 deploy): App Service fronts this app with a proxy — configure UseForwardedHeaders
// before UseRateLimiter, or every customer shares the proxy IP's rate-limit partition.
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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]
                    ?? throw new InvalidOperationException("Missing Jwt:SigningKey configuration."))),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddScoped<IProductRepository, EfProductRepository>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IPaymentMethod, ManualPayment>();
builder.Services.AddScoped<INotificationSender, LoggingNotificationSender>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await DevSeeder.MigrateAndSeedAsync(app.Services, app.Configuration, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("BakedManila API"));
}

app.MapControllers();

app.Run();

public partial class Program;
