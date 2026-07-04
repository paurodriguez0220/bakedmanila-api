# BakedManila API Admin Implementation Plan (Plan 2 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The baker can log in, manage products and photos, and work orders (status + payment), with a real email notification per order.

**Architecture:** Extends the Plan 1 monolith. JWT bearer auth over ASP.NET Core Identity (schema already migrated). Admin controllers under `/api/admin/*` guarded by `[Authorize(Roles = "Admin")]`. New seam `IImageStore` (Core) with `FileSystemImageStore` for dev/tests and `AzureBlobImageStore` for production (wired fully in Plan 5). `INotificationSender` gains the ACS Email implementation, selected by config. Spec: `docs/superpowers/specs/2026-07-04-bakedmanila-design.md`.

**Tech Stack:** .NET 10, `Microsoft.AspNetCore.Authentication.JwtBearer`, ASP.NET Core Identity (`UserManager`), `Azure.Communication.Email`, `Azure.Storage.Blobs`, xUnit v2 + LocalDB integration tests via existing `ApiFactory`.

## Global Constraints

Everything from Plan 1's Global Constraints still binds (nullable+warnings-as-errors, decimal(18,2), explicit lengths, async+CancellationToken, record DTOs, ProblemDetails-only errors, Conventional Commits, commit footer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`). Plus, learned in Plan 1 — these are repo law:

- Validation attributes on record DTOs go on **constructor parameters** (never `[property:]` — net10 MVC throws at runtime). `[Required]` on value types only works if the parameter is **nullable**.
- Raw SQL with `NEXT VALUE FOR` must materialize via `ToListAsync()` before `.Single()`.
- Test project is **xunit v2** — no `TestContext.Current`; use `CancellationToken.None`.
- Commits via multiple `-m` flags, never PowerShell here-strings. Shell is PowerShell 5.1: no `&&`, use `;`.
- Admin endpoints MAY expose int IDs (authenticated surface). Public endpoints still must not.
- `Product.CreatedAt`/`UpdatedAt` must be set via injected `TimeProvider` in this plan's CRUD (final-review carry-forward).
- Config keys introduced here: `Jwt:SigningKey`, `Jwt:Issuer`, `Jwt:Audience`, `Admin:Email`, `Admin:Password`, `Images:Provider` (`FileSystem`|`AzureBlob`), `Images:FileSystemRoot`, `ConnectionStrings:BlobStorage`, `Email:ConnectionString`, `Email:From`, `Email:To`. Dev defaults live in `appsettings.Development.json`; production values come from Key Vault in Plan 5. **No secrets in appsettings.json (non-Development).**

---

### Task 1: Identity services, JWT issuance, login endpoint, admin seeding

**Files:**
- Create: `src/BakedManila.Api/Auth/JwtTokenService.cs`, `src/BakedManila.Api/Controllers/AuthController.cs`, `src/BakedManila.Api/Dtos/LoginRequest.cs`, `src/BakedManila.Api/Dtos/LoginResponse.cs`
- Modify: `src/BakedManila.Api/Program.cs`, `src/BakedManila.Api/Data/DevSeeder.cs`, `src/BakedManila.Api/appsettings.Development.json`, `src/BakedManila.Api/appsettings.json`
- Test: `tests/BakedManila.Core.Tests/Api/AuthEndpointTests.cs`, plus test helper `tests/BakedManila.Core.Tests/Api/AdminAuth.cs`

**Interfaces:**
- Consumes: `BakedManilaDbContext` (Identity tables exist from Plan 1), `ApiFactory`.
- Produces:
  - `sealed class JwtTokenService(IConfiguration config, TimeProvider time)` with `(string Token, DateTime ExpiresAtUtc) CreateToken(IdentityUser user, IList<string> roles)` — HS256, claims `sub`=email, `ClaimTypes.Role` per role, lifetime 8 h, issuer/audience/key from `Jwt:*`.
  - `POST /api/auth/login` body `{ email, password }` → `200 { token, expiresAtUtc }` or plain `401` (no detail — same response for unknown email and wrong password).
  - `DevSeeder.SeedAdminAsync(IServiceProvider, IConfiguration, CancellationToken)` — ensures role `Admin` and the configured admin user exist; called from `MigrateAndSeedAsync` (Development only).
  - Test helper `static class AdminAuth { static Task<string> GetTokenAsync(ApiFactory factory)   // seeds admin via UserManager if missing, logs in over HTTP, returns bearer token (cached per factory) }` — Tasks 2–5 use this.
  - Rate-limit policy `"auth"`: fixed window, 10 per 5 minutes per IP, on the login endpoint.

- [ ] **Step 1: Add package**

```powershell
dotnet add src/BakedManila.Api package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Api/AdminAuth.cs
using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BakedManila.Core.Tests.Api;

public static class AdminAuth
{
    public const string Email = "admin@test.local";
    public const string Password = "Test!Passw0rd";

    public static async Task<string> GetTokenAsync(ApiFactory factory)
    {
        await EnsureAdminAsync(factory);
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Email, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    public static async Task EnsureAdminAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin"))
        {
            _ = await roles.CreateAsync(new IdentityRole("Admin"));
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(Email) is null)
        {
            var user = new IdentityUser { UserName = Email, Email = Email };
            var created = await users.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            _ = await users.AddToRoleAsync(user, "Admin");
        }
    }
}
```

```csharp
// tests/BakedManila.Core.Tests/Api/AuthEndpointTests.cs
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AuthEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync(); // ensures schema
        await AdminAuth.EnsureAdminAsync(_factory);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Login_ReturnsToken_WithAdminRoleClaim()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminAuth.Email, password = AdminAuth.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow.AddHours(7));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        Assert.Contains(jwt.Claims, c => c.Type is "role" or System.Security.Claims.ClaimTypes.Role
            && c.Value == "Admin");
    }

    [Theory]
    [InlineData("admin@test.local", "WrongPassword1!")]
    [InlineData("nobody@test.local", "Test!Passw0rd")]
    public async Task Login_Returns401_ForBadCredentials(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Returns400_ForMissingFields()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = AdminAuth.Email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter AuthEndpointTests`
Expected: compile error — `LoginResponse` not defined.

- [ ] **Step 4: Implement**

```csharp
// src/BakedManila.Api/Dtos/LoginRequest.cs
using System.ComponentModel.DataAnnotations;

namespace BakedManila.Api.Dtos;

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(128)] string Password);
```

```csharp
// src/BakedManila.Api/Dtos/LoginResponse.cs
namespace BakedManila.Api.Dtos;

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc);
```

```csharp
// src/BakedManila.Api/Auth/JwtTokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace BakedManila.Api.Auth;

public sealed class JwtTokenService(IConfiguration config, TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public (string Token, DateTime ExpiresAtUtc) CreateToken(IdentityUser user, IList<string> roles)
    {
        var signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Missing Jwt:SigningKey configuration.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.Add(Lifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
```

```csharp
// src/BakedManila.Api/Controllers/AuthController.cs
using BakedManila.Api.Auth;
using BakedManila.Api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<IdentityUser> users,
    JwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(); // identical response for unknown email and wrong password
        }

        var roles = await users.GetRolesAsync(user);
        var (token, expiresAtUtc) = tokens.CreateToken(user, roles);
        return Ok(new LoginResponse(token, expiresAtUtc));
    }
}
```

`Program.cs` additions — after the existing `AddRateLimiter` options, add the `"auth"` policy inside the same `AddRateLimiter` call:

```csharp
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
            }));
```

After the DbContext registration, add Identity + JWT auth (new usings: `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Identity`, `Microsoft.IdentityModel.Tokens`, `System.Text`, `BakedManila.Api.Auth`):

```csharp
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
```

In the pipeline, add between `app.UseRateLimiter();` and the OpenAPI block:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

`DevSeeder.cs` — add admin seeding. New method + call it at the end of `MigrateAndSeedAsync` (signature change: add `IConfiguration config` parameter; update the Program.cs call site to `await DevSeeder.MigrateAndSeedAsync(app.Services, app.Configuration, CancellationToken.None);`):

```csharp
private static async Task SeedAdminAsync(IServiceProvider scopedServices, IConfiguration config, CancellationToken ct)
{
    var roles = scopedServices.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roles.RoleExistsAsync("Admin"))
    {
        _ = await roles.CreateAsync(new IdentityRole("Admin"));
    }

    var email = config["Admin:Email"];
    var password = config["Admin:Password"];
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
    {
        return; // no admin configured — skip (prod seeds via config in Plan 5)
    }

    var users = scopedServices.GetRequiredService<UserManager<IdentityUser>>();
    if (await users.FindByEmailAsync(email) is not null)
    {
        return;
    }
    var admin = new IdentityUser { UserName = email, Email = email };
    var created = await users.CreateAsync(admin, password);
    if (created.Succeeded)
    {
        _ = await users.AddToRoleAsync(admin, "Admin");
    }
}
```

(`SeedAdminAsync` is called inside the existing scope in `MigrateAndSeedAsync`, after product seeding; ct unused by Identity APIs — keep the parameter for signature symmetry and discard with `_ = ct;` if the compiler warns.)

`appsettings.Development.json` — add:

```json
"Jwt": {
  "SigningKey": "dev-only-signing-key-bakedmanila-0123456789abcdef0123456789abcdef",
  "Issuer": "BakedManila",
  "Audience": "BakedManila"
},
"Admin": {
  "Email": "admin@bakedmanila.local",
  "Password": "DevAdmin!2026"
}
```

`appsettings.json` — add empty placeholders (documenting the keys, no secrets):

```json
"Jwt": { "SigningKey": "", "Issuer": "BakedManila", "Audience": "BakedManila" }
```

`ApiFactory` — add JWT + admin settings so the Testing environment has them (in `ConfigureWebHost`, after existing `UseSetting` calls):

```csharp
builder.UseSetting("Jwt:SigningKey", "test-signing-key-bakedmanila-0123456789abcdef0123456789abcdef");
builder.UseSetting("Jwt:Issuer", "BakedManila");
builder.UseSetting("Jwt:Audience", "BakedManila");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter AuthEndpointTests`
Expected: 4 tests PASS (1 + 2 theory cases + 1).

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test` → 44/44 green, 0 warnings.

```powershell
git add -A
git commit -m "feat(auth): add JWT login, Identity wiring, admin seeding" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Admin orders — filtered list

**Files:**
- Create: `src/BakedManila.Api/Controllers/AdminOrdersController.cs`, `src/BakedManila.Api/Dtos/AdminOrderDto.cs`
- Modify: `src/BakedManila.Core/Repositories/IOrderRepository.cs`, `src/BakedManila.Core/Repositories/EfOrderRepository.cs`
- Test: `tests/BakedManila.Core.Tests/Api/AdminOrdersEndpointTests.cs`

**Interfaces:**
- Consumes: `AdminAuth.GetTokenAsync(factory)` (Task 1), existing order entities.
- Produces:
  - `IOrderRepository` gains: `Task<List<Order>> GetFilteredAsync(OrderStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct)` (filters on `PreferredDate` range and status; `Include(Items)`; newest `CreatedAt` first) and `Task<Order?> GetByIdAsync(int id, CancellationToken ct)` (`Include(Items)`).
  - `record AdminOrderDto(int Id, string OrderNumber, string Status, string CustomerName, string Phone, string? Email, string? MessengerHandle, DateOnly PreferredDate, bool IsRush, string? Notes, string FulfillmentType, string PaymentMethod, string PaymentStatus, int SubtotalCentavos, DateTime CreatedAt, IReadOnlyList<OrderItemDto> Items)` with `static AdminOrderDto FromEntity(Order o)` (reuses `OrderItemDto` from Plan 1 and `.ToCentavos()`).
  - `GET /api/admin/orders?status=&from=&to=` → `200 [AdminOrderDto]`, `[Authorize(Roles = "Admin")]`; query params bind `OrderStatus?`, `DateOnly?`, `DateOnly?` (enum as string, e.g. `?status=Pending`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Api/AdminOrdersEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminOrdersEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using (var db = await _factory.CreateDbAsync())
        {
            db.Orders.AddRange(
                MakeOrder("BM-2026-0101", OrderStatus.Pending, new DateOnly(2026, 7, 10), new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc)),
                MakeOrder("BM-2026-0102", OrderStatus.Confirmed, new DateOnly(2026, 7, 12), new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc)),
                MakeOrder("BM-2026-0103", OrderStatus.Pending, new DateOnly(2026, 7, 20), new DateTime(2026, 7, 3, 8, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }
        _client = _factory.CreateClient();
        var token = await AdminAuth.GetTokenAsync(_factory);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static Order MakeOrder(string number, OrderStatus status, DateOnly preferred, DateTime created) => new()
    {
        OrderNumber = number,
        Status = status,
        CustomerName = "Maria",
        Phone = "09171234567",
        PreferredDate = preferred,
        FulfillmentType = FulfillmentType.Pickup,
        PaymentMethod = PaymentMethodType.ManualGcash,
        Subtotal = 280m,
        CreatedAt = created,
        Items = [new OrderItem { ProductName = "Snap", UnitPrice = 280m, Quantity = 1 }],
    };

    [Fact]
    public async Task List_Returns401_WithoutToken()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/api/admin/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsAllOrders_NewestFirst()
    {
        var orders = await _client.GetFromJsonAsync<List<AdminOrderDto>>("/api/admin/orders");
        Assert.Equal(["BM-2026-0103", "BM-2026-0102", "BM-2026-0101"],
            orders!.Select(o => o.OrderNumber).ToArray());
        Assert.All(orders, o => Assert.NotEmpty(o.Items));
    }

    [Fact]
    public async Task List_FiltersByStatusAndDateRange()
    {
        var pending = await _client.GetFromJsonAsync<List<AdminOrderDto>>("/api/admin/orders?status=Pending");
        Assert.Equal(2, pending!.Count);

        var inWindow = await _client.GetFromJsonAsync<List<AdminOrderDto>>(
            "/api/admin/orders?from=2026-07-11&to=2026-07-15");
        var only = Assert.Single(inWindow!);
        Assert.Equal("BM-2026-0102", only.OrderNumber);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AdminOrdersEndpointTests`
Expected: compile error — `AdminOrderDto` not defined.

- [ ] **Step 3: Implement**

Repository additions:

```csharp
// append to IOrderRepository.cs interface
Task<List<Order>> GetFilteredAsync(OrderStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct);
Task<Order?> GetByIdAsync(int id, CancellationToken ct);
```

```csharp
// append to EfOrderRepository.cs
public Task<List<Order>> GetFilteredAsync(OrderStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct)
{
    var query = db.Orders.AsQueryable();
    if (status is not null)
    {
        query = query.Where(o => o.Status == status);
    }
    if (from is not null)
    {
        query = query.Where(o => o.PreferredDate >= from);
    }
    if (to is not null)
    {
        query = query.Where(o => o.PreferredDate <= to);
    }
    return query
        .Include(o => o.Items)
        .OrderByDescending(o => o.CreatedAt)
        .ToListAsync(ct);
}

public Task<Order?> GetByIdAsync(int id, CancellationToken ct) =>
    db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct);
```

```csharp
// src/BakedManila.Api/Dtos/AdminOrderDto.cs
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record AdminOrderDto(
    int Id,
    string OrderNumber,
    string Status,
    string CustomerName,
    string Phone,
    string? Email,
    string? MessengerHandle,
    DateOnly PreferredDate,
    bool IsRush,
    string? Notes,
    string FulfillmentType,
    string PaymentMethod,
    string PaymentStatus,
    int SubtotalCentavos,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items)
{
    public static AdminOrderDto FromEntity(Order o) => new(
        o.Id,
        o.OrderNumber,
        o.Status.ToString(),
        o.CustomerName,
        o.Phone,
        o.Email,
        o.MessengerHandle,
        o.PreferredDate,
        o.IsRush,
        o.Notes,
        o.FulfillmentType.ToString(),
        o.PaymentMethod.ToString(),
        o.PaymentStatus.ToString(),
        o.Subtotal.ToCentavos(),
        o.CreatedAt,
        o.Items.Select(i => new OrderItemDto(i.ProductName, i.UnitPrice.ToCentavos(), i.Quantity)).ToList());
}
```

```csharp
// src/BakedManila.Api/Controllers/AdminOrdersController.cs
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public sealed class AdminOrdersController(IOrderRepository orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminOrderDto>>> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var list = await orders.GetFilteredAsync(status, from, to, ct);
        return Ok(list.Select(AdminOrderDto.FromEntity).ToList());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AdminOrdersEndpointTests`
Expected: 3 tests PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test` → 47/47 green.

```powershell
git add -A
git commit -m "feat(admin): add filtered admin orders list behind Admin role" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Admin orders — status transition and payment PATCH

**Files:**
- Create: `src/BakedManila.Core/Domain/Exceptions/OrderNotFoundException.cs`, `src/BakedManila.Api/Dtos/UpdateOrderStatusRequest.cs`, `src/BakedManila.Api/Dtos/UpdatePaymentStatusRequest.cs`
- Modify: `src/BakedManila.Api/Controllers/AdminOrdersController.cs`, `src/BakedManila.Api/Middleware/DomainExceptionHandler.cs`
- Test: append to `tests/BakedManila.Core.Tests/Api/AdminOrdersEndpointTests.cs`

**Interfaces:**
- Consumes: `Order.TransitionTo` / `Order.MarkPayment` (Plan 1), `IOrderRepository.GetByIdAsync` + `SaveChangesAsync` (Task 2).
- Produces:
  - `sealed class OrderNotFoundException(int id) : Exception($"Order {id} was not found.")` → mapped to 404 in `DomainExceptionHandler`.
  - `record UpdateOrderStatusRequest([Required] OrderStatus? Status);` `record UpdatePaymentStatusRequest([Required] PaymentStatus? PaymentStatus);` (nullable enums — the `[Required]` lesson).
  - `PATCH /api/admin/orders/{id}/status` → `200 AdminOrderDto` | `404` | `409` (invalid transition); `PATCH /api/admin/orders/{id}/payment` → `200 AdminOrderDto` | `404`.

- [ ] **Step 1: Write the failing tests** — append inside `AdminOrdersEndpointTests`:

```csharp
[Fact]
public async Task PatchStatus_AdvancesOrder_AndRejectsInvalidTransition()
{
    var orders = await _client.GetFromJsonAsync<List<AdminOrderDto>>("/api/admin/orders?status=Pending");
    var target = orders![0];

    var ok = await _client.PatchAsJsonAsync($"/api/admin/orders/{target.Id}/status", new { status = "Confirmed" });
    Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    var updated = await ok.Content.ReadFromJsonAsync<AdminOrderDto>();
    Assert.Equal("Confirmed", updated!.Status);

    var invalid = await _client.PatchAsJsonAsync($"/api/admin/orders/{target.Id}/status", new { status = "Pending" });
    Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
}

[Fact]
public async Task PatchStatus_Returns404_ForUnknownOrder()
{
    var response = await _client.PatchAsJsonAsync("/api/admin/orders/999999/status", new { status = "Confirmed" });
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task PatchPayment_TogglesPaidState()
{
    var orders = await _client.GetFromJsonAsync<List<AdminOrderDto>>("/api/admin/orders");
    var target = orders![0];

    var response = await _client.PatchAsJsonAsync($"/api/admin/orders/{target.Id}/payment", new { paymentStatus = "Paid" });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var updated = await response.Content.ReadFromJsonAsync<AdminOrderDto>();
    Assert.Equal("Paid", updated!.PaymentStatus);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AdminOrdersEndpointTests`
Expected: 3 new tests FAIL with 404/405 (routes don't exist).

- [ ] **Step 3: Implement**

```csharp
// src/BakedManila.Core/Domain/Exceptions/OrderNotFoundException.cs
namespace BakedManila.Core.Domain.Exceptions;

public sealed class OrderNotFoundException(int id) : Exception($"Order {id} was not found.")
{
    public int Id { get; } = id;
}
```

`DomainExceptionHandler` — add to the switch:

```csharp
OrderNotFoundException => (StatusCodes.Status404NotFound, "Order not found"),
```

```csharp
// src/BakedManila.Api/Dtos/UpdateOrderStatusRequest.cs
using System.ComponentModel.DataAnnotations;
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record UpdateOrderStatusRequest([Required] OrderStatus? Status);
```

```csharp
// src/BakedManila.Api/Dtos/UpdatePaymentStatusRequest.cs
using System.ComponentModel.DataAnnotations;
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record UpdatePaymentStatusRequest([Required] PaymentStatus? PaymentStatus);
```

Append to `AdminOrdersController`:

```csharp
[HttpPatch("{id:int}/status")]
public async Task<ActionResult<AdminOrderDto>> UpdateStatus(
    int id, UpdateOrderStatusRequest request, CancellationToken ct)
{
    var order = await orders.GetByIdAsync(id, ct)
        ?? throw new OrderNotFoundException(id);
    order.TransitionTo(request.Status!.Value); // validated: throws InvalidStatusTransitionException → 409
    await orders.SaveChangesAsync(ct);
    return Ok(AdminOrderDto.FromEntity(order));
}

[HttpPatch("{id:int}/payment")]
public async Task<ActionResult<AdminOrderDto>> UpdatePayment(
    int id, UpdatePaymentStatusRequest request, CancellationToken ct)
{
    var order = await orders.GetByIdAsync(id, ct)
        ?? throw new OrderNotFoundException(id);
    order.MarkPayment(request.PaymentStatus!.Value);
    await orders.SaveChangesAsync(ct);
    return Ok(AdminOrderDto.FromEntity(order));
}
```

(add `using BakedManila.Core.Domain.Exceptions;` to the controller.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AdminOrdersEndpointTests` → 6 tests PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test` → 50/50 green.

```powershell
git add -A
git commit -m "feat(admin): add order status transition and payment PATCH endpoints" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Admin products CRUD

**Files:**
- Create: `src/BakedManila.Api/Controllers/AdminProductsController.cs`, `src/BakedManila.Api/Dtos/AdminProductDto.cs`, `src/BakedManila.Api/Dtos/SaveProductRequest.cs`, `src/BakedManila.Core/Domain/Exceptions/DuplicateSlugException.cs`
- Modify: `src/BakedManila.Core/Repositories/IProductRepository.cs`, `src/BakedManila.Core/Repositories/EfProductRepository.cs`, `src/BakedManila.Api/Middleware/DomainExceptionHandler.cs`
- Test: `tests/BakedManila.Core.Tests/Api/AdminProductsEndpointTests.cs`

**Interfaces:**
- Consumes: `AdminAuth`, `Product` entity, `TimeProvider` (DI singleton).
- Produces:
  - `IProductRepository` gains: `Task<List<Product>> GetAllForAdminAsync(CancellationToken ct)` (includes unavailable, excludes soft-deleted via the global filter, `Include(Images)`, ordered by SortOrder), `Task<Product?> GetByIdAsync(int id, CancellationToken ct)` (`Include(Images)`), `Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct)`, `void Add(Product product)`, `Task SaveChangesAsync(CancellationToken ct)`.
  - `sealed class DuplicateSlugException(string slug) : Exception($"A product with slug '{slug}' already exists.")` → 409.
  - `record SaveProductRequest([Required, MaxLength(100)] string? Name, [Required, MaxLength(120), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] string? Slug, [MaxLength(2000)] string? Description, [Required, Range(1, 100_000_000)] int? PriceCentavos, [Required] bool? IsAvailable, [Required] int? SortOrder);`
  - `record AdminProductDto(int Id, string Slug, string Name, string Description, int PriceCentavos, bool IsAvailable, int SortOrder, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<ProductImageDto> Images)` with `FromEntity(Product, string imageBaseUrl)`.
  - Endpoints (all `[Authorize(Roles = "Admin")]`, route `api/admin/products`): `GET` list; `POST` → 201 (sets `CreatedAt`/`UpdatedAt` from `TimeProvider`, price = `PriceCentavos / 100m`); `PUT {id:int}` → 200 (updates fields + `UpdatedAt`) | 404; `DELETE {id:int}` → 204 soft delete (`IsDeleted = true`) | 404. Reuses `ProductNotFoundException`? No — that takes a slug; use `Problem(404)` directly for id-not-found here, matching ProductsController's style.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Api/AdminProductsEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminProductsEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using (var db = await _factory.CreateDbAsync()) { } // ensure schema
        _client = _factory.CreateClient();
        var token = await AdminAuth.GetTokenAsync(_factory);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object ValidProduct(string slug = "ube-crinkles") => new
    {
        name = "Ube Crinkles",
        slug,
        description = "Chewy, deep purple.",
        priceCentavos = 30000,
        isAvailable = true,
        sortOrder = 5,
    };

    [Fact]
    public async Task Crud_Roundtrip_CreateUpdateSoftDelete()
    {
        // Create
        var created = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<AdminProductDto>();
        Assert.Equal(30000, product!.PriceCentavos);
        Assert.NotEqual(default, product.CreatedAt);

        // Visible on public storefront
        var publicList = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products");
        Assert.Contains(publicList!, p => p.Slug == "ube-crinkles");

        // Update: mark sold out, raise price
        var updated = await _client.PutAsJsonAsync($"/api/admin/products/{product.Id}", new
        {
            name = "Ube Crinkles",
            slug = "ube-crinkles",
            description = "Chewy, deep purple.",
            priceCentavos = 32000,
            isAvailable = false,
            sortOrder = 5,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedDto = await updated.Content.ReadFromJsonAsync<AdminProductDto>();
        Assert.Equal(32000, updatedDto!.PriceCentavos);
        Assert.False(updatedDto.IsAvailable);
        Assert.True(updatedDto.UpdatedAt >= updatedDto.CreatedAt);

        // Sold-out product hidden from public list, still in admin list
        publicList = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products");
        Assert.DoesNotContain(publicList!, p => p.Slug == "ube-crinkles");
        var adminList = await _client.GetFromJsonAsync<List<AdminProductDto>>("/api/admin/products");
        Assert.Contains(adminList!, p => p.Slug == "ube-crinkles");

        // Soft delete: gone from admin list too
        var deleted = await _client.DeleteAsync($"/api/admin/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        adminList = await _client.GetFromJsonAsync<List<AdminProductDto>>("/api/admin/products");
        Assert.DoesNotContain(adminList!, p => p.Slug == "ube-crinkles");
    }

    [Fact]
    public async Task Create_Returns409_ForDuplicateSlug()
    {
        _ = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct("dup-slug"));
        var second = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct("dup-slug"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("Bad Slug!")]
    [InlineData("UPPER")]
    [InlineData("-leading")]
    public async Task Create_Returns400_ForInvalidSlug(string slug)
    {
        var response = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct(slug));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_Return401_WithoutToken()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/products", ValidProduct())).StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AdminProductsEndpointTests`
Expected: compile error — `AdminProductDto` not defined.

- [ ] **Step 3: Implement** (repository additions, exception, DTOs, controller)

```csharp
// append to IProductRepository.cs
Task<List<Product>> GetAllForAdminAsync(CancellationToken ct);
Task<Product?> GetByIdAsync(int id, CancellationToken ct);
Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct);
void Add(Product product);
Task SaveChangesAsync(CancellationToken ct);
```

```csharp
// append to EfProductRepository.cs
public Task<List<Product>> GetAllForAdminAsync(CancellationToken ct) =>
    db.Products.Include(p => p.Images).OrderBy(p => p.SortOrder).ToListAsync(ct);

public Task<Product?> GetByIdAsync(int id, CancellationToken ct) =>
    db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == id, ct);

public Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct) =>
    db.Products.AnyAsync(p => p.Slug == slug && (exceptProductId == null || p.Id != exceptProductId), ct);

public void Add(Product product) => db.Products.Add(product);

public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
```

```csharp
// src/BakedManila.Core/Domain/Exceptions/DuplicateSlugException.cs
namespace BakedManila.Core.Domain.Exceptions;

public sealed class DuplicateSlugException(string slug)
    : Exception($"A product with slug '{slug}' already exists.")
{
    public string Slug { get; } = slug;
}
```

`DomainExceptionHandler` — add: `DuplicateSlugException => (StatusCodes.Status409Conflict, "Duplicate slug"),`

```csharp
// src/BakedManila.Api/Dtos/SaveProductRequest.cs
using System.ComponentModel.DataAnnotations;

namespace BakedManila.Api.Dtos;

public sealed record SaveProductRequest(
    [Required, MaxLength(100)] string? Name,
    [Required, MaxLength(120), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug must be lowercase letters, digits, and single hyphens.")] string? Slug,
    [MaxLength(2000)] string? Description,
    [Required, Range(1, 100_000_000)] int? PriceCentavos,
    [Required] bool? IsAvailable,
    [Required] int? SortOrder);
```

```csharp
// src/BakedManila.Api/Dtos/AdminProductDto.cs
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record AdminProductDto(
    int Id,
    string Slug,
    string Name,
    string Description,
    int PriceCentavos,
    bool IsAvailable,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ProductImageDto> Images)
{
    public static AdminProductDto FromEntity(Product p, string imageBaseUrl) => new(
        p.Id,
        p.Slug,
        p.Name,
        p.Description,
        p.Price.ToCentavos(),
        p.IsAvailable,
        p.SortOrder,
        p.CreatedAt,
        p.UpdatedAt,
        p.Images.OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto($"{imageBaseUrl}/{i.BlobName}"))
            .ToList());
}
```

```csharp
// src/BakedManila.Api/Controllers/AdminProductsController.cs
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = "Admin")]
public sealed class AdminProductsController(
    IProductRepository products,
    TimeProvider time,
    IConfiguration config) : ControllerBase
{
    private string ImageBaseUrl => config["Storage:PublicBaseUrl"] ?? string.Empty;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminProductDto>>> List(CancellationToken ct)
    {
        var list = await products.GetAllForAdminAsync(ct);
        return Ok(list.Select(p => AdminProductDto.FromEntity(p, ImageBaseUrl)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AdminProductDto>> Create(SaveProductRequest request, CancellationToken ct)
    {
        if (await products.SlugExistsAsync(request.Slug!, exceptProductId: null, ct))
        {
            throw new DuplicateSlugException(request.Slug!);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var product = new Product
        {
            Name = request.Name!,
            Slug = request.Slug!,
            Description = request.Description ?? string.Empty,
            Price = request.PriceCentavos!.Value / 100m,
            IsAvailable = request.IsAvailable!.Value,
            SortOrder = request.SortOrder!.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };
        products.Add(product);
        await products.SaveChangesAsync(ct);

        var dto = AdminProductDto.FromEntity(product, ImageBaseUrl);
        return CreatedAtAction(nameof(List), null, dto);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminProductDto>> Update(int id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        if (product is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
        }
        if (await products.SlugExistsAsync(request.Slug!, exceptProductId: id, ct))
        {
            throw new DuplicateSlugException(request.Slug!);
        }

        product.Name = request.Name!;
        product.Slug = request.Slug!;
        product.Description = request.Description ?? string.Empty;
        product.Price = request.PriceCentavos!.Value / 100m;
        product.IsAvailable = request.IsAvailable!.Value;
        product.SortOrder = request.SortOrder!.Value;
        product.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await products.SaveChangesAsync(ct);

        return Ok(AdminProductDto.FromEntity(product, ImageBaseUrl));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        if (product is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
        }
        product.IsDeleted = true;
        product.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await products.SaveChangesAsync(ct);
        return NoContent();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AdminProductsEndpointTests` → 6 tests PASS (1+1+3 theory+1).

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test` → 56/56 green.

```powershell
git add -A
git commit -m "feat(admin): add products CRUD with soft delete and slug uniqueness" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Product image upload via IImageStore

**Files:**
- Create: `src/BakedManila.Core/Services/IImageStore.cs`, `src/BakedManila.Core/Services/AzureBlobImageStore.cs`, `src/BakedManila.Api/Services/FileSystemImageStore.cs`, `src/BakedManila.Api/Dtos/ProductImageAdminDto.cs`
- Modify: `src/BakedManila.Api/Controllers/AdminProductsController.cs`, `src/BakedManila.Api/Program.cs`, `src/BakedManila.Api/appsettings.Development.json`
- Test: `tests/BakedManila.Core.Tests/Api/AdminProductImagesEndpointTests.cs`

**Interfaces:**
- Consumes: `ProductImage` entity, `IProductRepository.GetByIdAsync` + `SaveChangesAsync`.
- Produces:

```csharp
public interface IImageStore
{
    /// Returns the stored blob name, e.g. "products/12/3f9c….jpg".
    Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct);
    Task DeleteAsync(string blobName, CancellationToken ct);
}
```

  - `AzureBlobImageStore(BlobContainerClient container)` in Core (package `Azure.Storage.Blobs` on Core) — container `product-images`; upload with content-type header. **Not integration-tested in this plan** (needs Azurite/real storage — Plan 5 wires and verifies it); keep it thin and reviewed.
  - `FileSystemImageStore(string root)` in Api — writes `{root}/products/{productId}/{guid}{ext}`, delete removes the file. Used in Development and tests.
  - Content-type → extension map (both stores share it via `ImageContentTypes.TryGetExtension(contentType, out ext)` static helper in Core next to `IImageStore`): `image/jpeg`→`.jpg`, `image/png`→`.png`, `image/webp`→`.webp`. Anything else is rejected at the controller with 422.
  - Endpoints on `AdminProductsController`: `POST {id:int}/images` (multipart form file field `file`, max 5 MB → 422 over-limit or bad type, 404 unknown product; store first, then `ProductImage` row with `SortOrder = existing max + 1`) → `201 ProductImageAdminDto(int Id, string Url, int SortOrder)`; `DELETE {id:int}/images/{imageId:int}` → 204 (DB row removed first, then best-effort store delete — a failure is logged, not surfaced) | 404.
  - DI in Program.cs by config `Images:Provider`: `"AzureBlob"` → `AzureBlobImageStore` with `ConnectionStrings:BlobStorage`; anything else/absent → `FileSystemImageStore` rooted at `Images:FileSystemRoot` (default `{ContentRootPath}/App_Data/images`). Development also maps static files: requests to `/images/*` serve from that root, and dev `Storage:PublicBaseUrl` becomes `http://localhost:5127/images`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Api/AdminProductImagesEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminProductImagesEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _imagesRoot = null!;
    private int _productId;

    public async Task InitializeAsync()
    {
        _imagesRoot = Path.Combine(Path.GetTempPath(), $"bm-images-{Guid.NewGuid():N}");
        _factory = new ApiFactory(configureHost: builder =>
        {
            builder.UseSetting("Images:Provider", "FileSystem");
            builder.UseSetting("Images:FileSystemRoot", _imagesRoot);
        });
        await using (var db = await _factory.CreateDbAsync())
        {
            db.Products.Add(new BakedManila.Core.Domain.Product
            {
                Name = "Classic Chip", Slug = "classic-chip", Price = 280m,
            });
            await db.SaveChangesAsync();
            _productId = db.Products.Local.First().Id;
        }
        _client = _factory.CreateClient();
        var token = await AdminAuth.GetTokenAsync(_factory);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_imagesRoot))
        {
            Directory.Delete(_imagesRoot, recursive: true);
        }
    }

    private static MultipartFormDataContent JpegUpload(int bytes = 1024)
    {
        var payload = new ByteArrayContent(new byte[bytes]);
        payload.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { payload, "file", "cookie.jpg" } };
    }

    [Fact]
    public async Task Upload_StoresFile_CreatesRow_AndServesUrl()
    {
        var response = await _client.PostAsync($"/api/admin/products/{_productId}/images", JpegUpload());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = await response.Content.ReadFromJsonAsync<ProductImageAdminDto>();
        Assert.EndsWith(".jpg", image!.Url);
        Assert.Equal(1, image.SortOrder);

        // file physically exists under the temp root
        var files = Directory.GetFiles(Path.Combine(_imagesRoot, "products", _productId.ToString()));
        Assert.Single(files);

        // image URL appears on the admin product
        var admin = await _client.GetFromJsonAsync<List<AdminProductDto>>("/api/admin/products");
        Assert.Single(admin!.Single(p => p.Id == _productId).Images);
    }

    [Fact]
    public async Task Upload_Returns422_ForWrongTypeOrTooLarge()
    {
        var gif = new ByteArrayContent(new byte[10]);
        gif.Headers.ContentType = new MediaTypeHeaderValue("image/gif");
        var badType = await _client.PostAsync($"/api/admin/products/{_productId}/images",
            new MultipartFormDataContent { { gif, "file", "cookie.gif" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badType.StatusCode);

        var tooLarge = await _client.PostAsync($"/api/admin/products/{_productId}/images",
            JpegUpload(bytes: 6 * 1024 * 1024));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLarge.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns404_ForUnknownProduct()
    {
        var response = await _client.PostAsync("/api/admin/products/999999/images", JpegUpload());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesRowAndFile()
    {
        var created = await (await _client.PostAsync($"/api/admin/products/{_productId}/images", JpegUpload()))
            .Content.ReadFromJsonAsync<ProductImageAdminDto>();

        var deleted = await _client.DeleteAsync($"/api/admin/products/{_productId}/images/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var dir = Path.Combine(_imagesRoot, "products", _productId.ToString());
        Assert.Empty(Directory.Exists(dir) ? Directory.GetFiles(dir) : []);
    }
}
```

**`ApiFactory` change this task needs:** add an optional constructor parameter `Action<IWebHostBuilder>? configureHost = null`, invoked at the end of `ConfigureWebHost`. Existing tests keep working (parameterless).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AdminProductImagesEndpointTests`
Expected: compile error — `ProductImageAdminDto` not defined.

- [ ] **Step 3: Implement**

```csharp
// src/BakedManila.Core/Services/IImageStore.cs
namespace BakedManila.Core.Services;

public interface IImageStore
{
    Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct);
    Task DeleteAsync(string blobName, CancellationToken ct);
}

public static class ImageContentTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public static bool TryGetExtension(string contentType, out string extension) =>
        Map.TryGetValue(contentType, out extension!);
}
```

```csharp
// src/BakedManila.Core/Services/AzureBlobImageStore.cs
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BakedManila.Core.Services;

/// Production image store (wired + verified in the infra plan). Container: product-images.
public sealed class AzureBlobImageStore(BlobContainerClient container) : IImageStore
{
    public async Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct)
    {
        if (!ImageContentTypes.TryGetExtension(contentType, out var extension))
        {
            throw new ArgumentException($"Unsupported image content type '{contentType}'.", nameof(contentType));
        }
        var blobName = $"products/{productId}/{Guid.NewGuid():N}{extension}";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
        return blobName;
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct) =>
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
}
```

```csharp
// src/BakedManila.Api/Services/FileSystemImageStore.cs
using BakedManila.Core.Services;

namespace BakedManila.Api.Services;

/// Dev/test image store: writes under a local root, mirroring blob-name layout.
public sealed class FileSystemImageStore(string root) : IImageStore
{
    public async Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct)
    {
        if (!ImageContentTypes.TryGetExtension(contentType, out var extension))
        {
            throw new ArgumentException($"Unsupported image content type '{contentType}'.", nameof(contentType));
        }
        var blobName = $"products/{productId}/{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, blobName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return blobName;
    }

    public Task DeleteAsync(string blobName, CancellationToken ct)
    {
        var path = Path.Combine(root, blobName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
```

```csharp
// src/BakedManila.Api/Dtos/ProductImageAdminDto.cs
namespace BakedManila.Api.Dtos;

public sealed record ProductImageAdminDto(int Id, string Url, int SortOrder);
```

`AdminProductsController` additions (inject `IImageStore images`, `ILogger<AdminProductsController> logger`; add `using BakedManila.Core.Services;`, `using Microsoft.AspNetCore.Http;` implicit):

```csharp
private const long MaxImageBytes = 5 * 1024 * 1024;

[HttpPost("{id:int}/images")]
public async Task<ActionResult<ProductImageAdminDto>> UploadImage(int id, IFormFile file, CancellationToken ct)
{
    var product = await products.GetByIdAsync(id, ct);
    if (product is null)
    {
        return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
    }
    if (!ImageContentTypes.TryGetExtension(file.ContentType, out _))
    {
        return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Unsupported image type", detail: "Use JPEG, PNG, or WebP.");
    }
    if (file.Length is 0 or > MaxImageBytes)
    {
        return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Invalid image size", detail: "Images must be between 1 byte and 5 MB.");
    }

    await using var stream = file.OpenReadStream();
    var blobName = await images.SaveAsync(stream, file.ContentType, id, ct); // store first — orphan blobs are harmless
    var image = new ProductImage
    {
        ProductId = id,
        BlobName = blobName,
        SortOrder = product.Images.Count == 0 ? 1 : product.Images.Max(i => i.SortOrder) + 1,
    };
    product.Images.Add(image);
    await products.SaveChangesAsync(ct);

    return CreatedAtAction(nameof(List), null,
        new ProductImageAdminDto(image.Id, $"{ImageBaseUrl}/{image.BlobName}", image.SortOrder));
}

[HttpDelete("{id:int}/images/{imageId:int}")]
public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken ct)
{
    var product = await products.GetByIdAsync(id, ct);
    var image = product?.Images.SingleOrDefault(i => i.Id == imageId);
    if (product is null || image is null)
    {
        return Problem(statusCode: StatusCodes.Status404NotFound, title: "Image not found");
    }

    product.Images.Remove(image);
    await products.SaveChangesAsync(ct);
    try
    {
        await images.DeleteAsync(image.BlobName, ct);
    }
    catch (Exception ex) // deliberate broad catch: an orphaned stored file is harmless; a failed API call is not
    {
        logger.LogError(ex, "Failed to delete stored image {BlobName}", image.BlobName);
    }
    return NoContent();
}
```

`Program.cs` — image store DI + dev static files (usings: `BakedManila.Api.Services`, `Azure.Storage.Blobs`, `Microsoft.Extensions.FileProviders`):

```csharp
// after the notification/repository registrations
if (builder.Configuration["Images:Provider"] == "AzureBlob")
{
    builder.Services.AddSingleton<IImageStore>(_ => new AzureBlobImageStore(
        new BlobContainerClient(
            builder.Configuration.GetConnectionString("BlobStorage")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:BlobStorage."),
            "product-images")));
}
else
{
    var imagesRoot = builder.Configuration["Images:FileSystemRoot"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "images");
    builder.Services.AddSingleton<IImageStore>(_ => new FileSystemImageStore(imagesRoot));
}
```

```csharp
// in the Development block of the pipeline (with MapOpenApi/Scalar)
var imagesServeRoot = app.Configuration["Images:FileSystemRoot"]
    ?? Path.Combine(app.Environment.ContentRootPath, "App_Data", "images");
Directory.CreateDirectory(imagesServeRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesServeRoot),
    RequestPath = "/images",
});
```

Add package: `dotnet add src/BakedManila.Core package Azure.Storage.Blobs`

`appsettings.Development.json` — set `"Storage": { "PublicBaseUrl": "http://localhost:5127/images" }` (replaces the empty dev value) and add `"Images": { "Provider": "FileSystem" }`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AdminProductImagesEndpointTests` → 4 tests PASS. Then full suite → 60/60.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(admin): add product image upload/delete via IImageStore seam" -m "FileSystemImageStore for dev/tests; AzureBlobImageStore wired for prod in infra plan." -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: ACS email sender, README, final verification

**Files:**
- Create: `src/BakedManila.Core/Services/OrderPlacedEmail.cs`, `src/BakedManila.Core/Services/AcsEmailNotificationSender.cs`
- Modify: `src/BakedManila.Api/Program.cs`, `src/BakedManila.Api/appsettings.json`, `README.md`
- Test: `tests/BakedManila.Core.Tests/Services/OrderPlacedEmailTests.cs`

**Interfaces:**
- Consumes: `OrderPlaced` record, `INotificationSender`.
- Produces:
  - `static class OrderPlacedEmail { static (string Subject, string PlainText) Build(OrderPlaced n); }` — subject `New order {OrderNumber} — {CustomerName}` (prefix `[RUSH] ` when `IsRush`); body lists customer, phone, preferred date, each item `{Quantity}× {ProductName} — ₱{UnitPrice:N2}`, and `Total: ₱{Subtotal:N2}`.
  - `sealed class AcsEmailNotificationSender(EmailClient client, IConfiguration config, ILogger<AcsEmailNotificationSender> logger) : INotificationSender` — sends via `Email:From` → `Email:To`, `WaitUntil.Started` (fire-and-forget; OrderService already guards failures).
  - Program.cs selection: `Email:ConnectionString` non-empty → ACS sender (+ singleton `EmailClient`); else keep `LoggingNotificationSender`.
- Package: `dotnet add src/BakedManila.Core package Azure.Communication.Email`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Services/OrderPlacedEmailTests.cs
using BakedManila.Core.Services;

namespace BakedManila.Core.Tests.Services;

public class OrderPlacedEmailTests
{
    private static OrderPlaced Notification(bool isRush = false) => new(
        "BM-2026-0042", "Maria Santos", "09171234567",
        new DateOnly(2026, 7, 10), isRush, 910m,
        [new OrderPlacedItem("Classic Chocolate Chip", 280m, 2),
         new OrderPlacedItem("Chocolate Chunk Banana Bread", 350m, 1)]);

    [Fact]
    public void Build_FormatsSubjectAndBody()
    {
        var (subject, body) = OrderPlacedEmail.Build(Notification());

        Assert.Equal("New order BM-2026-0042 — Maria Santos", subject);
        Assert.Contains("09171234567", body);
        Assert.Contains("2026-07-10", body);
        Assert.Contains("2× Classic Chocolate Chip — ₱280.00", body);
        Assert.Contains("1× Chocolate Chunk Banana Bread — ₱350.00", body);
        Assert.Contains("Total: ₱910.00", body);
    }

    [Fact]
    public void Build_PrefixesRushOrders()
    {
        var (subject, _) = OrderPlacedEmail.Build(Notification(isRush: true));
        Assert.StartsWith("[RUSH] ", subject);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter OrderPlacedEmailTests`
Expected: compile error — `OrderPlacedEmail` not defined.

- [ ] **Step 3: Implement**

```csharp
// src/BakedManila.Core/Services/OrderPlacedEmail.cs
using System.Globalization;
using System.Text;

namespace BakedManila.Core.Services;

public static class OrderPlacedEmail
{
    public static (string Subject, string PlainText) Build(OrderPlaced n)
    {
        var subject = $"{(n.IsRush ? "[RUSH] " : "")}New order {n.OrderNumber} — {n.CustomerName}";

        var body = new StringBuilder()
            .AppendLine($"Order: {n.OrderNumber}")
            .AppendLine($"Customer: {n.CustomerName}")
            .AppendLine($"Phone: {n.Phone}")
            .AppendLine($"Preferred date: {n.PreferredDate:yyyy-MM-dd}")
            .AppendLine();
        foreach (var item in n.Items)
        {
            body.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{item.Quantity}× {item.ProductName} — ₱{item.UnitPrice:N2}"));
        }
        body.AppendLine()
            .AppendLine(string.Create(CultureInfo.InvariantCulture, $"Total: ₱{n.Subtotal:N2}"));

        return (subject, body.ToString());
    }
}
```

```csharp
// src/BakedManila.Core/Services/AcsEmailNotificationSender.cs
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BakedManila.Core.Services;

public sealed class AcsEmailNotificationSender(
    EmailClient client,
    IConfiguration config,
    ILogger<AcsEmailNotificationSender> logger) : INotificationSender
{
    public async Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct)
    {
        var from = config["Email:From"]
            ?? throw new InvalidOperationException("Missing Email:From configuration.");
        var to = config["Email:To"]
            ?? throw new InvalidOperationException("Missing Email:To configuration.");

        var (subject, plainText) = OrderPlacedEmail.Build(notification);
        _ = await client.SendAsync(WaitUntil.Started, from, to, subject, plainText: plainText,
            cancellationToken: ct);
        logger.LogInformation("Order email queued for {OrderNumber}", notification.OrderNumber);
    }
}
```

`Program.cs` — replace the `INotificationSender` registration:

```csharp
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
```

(using `Azure.Communication.Email`.)

`appsettings.json` — add key documentation: `"Email": { "ConnectionString": "", "From": "", "To": "" }`.

- [ ] **Step 4: Run tests to verify they pass; full suite**

Run: `dotnet test` → 62/62 green, 0 warnings.

- [ ] **Step 5: README + manual verification**

README: add an **Admin API** section — login endpoint + dev credentials reference (`Admin:Email`/`Admin:Password` from appsettings.Development.json), admin endpoint list, image storage config (`Images:Provider`), email config (`Email:*` — logging fallback when unset), and how to authorize in Scalar (Auth button → Bearer token from `/api/auth/login`).

Manual verification (background `dotnet run`, then stop): login with dev admin creds → token; create a product; upload a JPEG to it; GET `/api/products` shows the image URL and the URL serves the bytes; PATCH an order to Confirmed. Capture the transcript in the task report.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(core): add ACS email notification sender with config-driven selection" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Plan Self-Review Notes

- Spec coverage (Plan 2 slice): admin login/JWT ✔ (T1), admin order list + status + payment ✔ (T2–T3), product CRUD with soft delete ✔ (T4), image upload/delete with server-generated names, type/size validation, store-before-DB ✔ (T5), ACS email behind `INotificationSender` ✔ (T6). Deferred per spec: real Blob wiring + Key Vault (Plan 5), lookup-endpoint throttling and exception-handler HTTP tests (carry-forwards recorded in the ledger).
- Type consistency: `AdminAuth` helper produced in T1 is consumed in T2/T4/T5; `GetByIdAsync`/`SaveChangesAsync` added in T2 (orders) and T4 (products) match their later uses; `ApiFactory(configureHost:)` optional param (T5) is backward compatible.
- Known repo law is restated in Global Constraints (record validation attrs, xunit v2, commit style) so implementers don't rediscover Plan 1's traps.
