# BakedManila API Core Implementation Plan (Plan 1 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A locally runnable, fully tested ASP.NET Core API with the public ordering flow: browse products, place a pre-order, look up order status.

**Architecture:** Pragmatic layered monolith, two projects. `BakedManila.Core` holds domain entities (with behavior), EF Core DbContext, repositories, and the order-placement service. `BakedManila.Api` holds controllers, DTOs, ProblemDetails, and rate limiting. Spec: `docs/superpowers/specs/2026-07-04-bakedmanila-design.md`.

**Tech Stack:** .NET 10, ASP.NET Core (controllers), EF Core 10 (SQL Server provider, code-first, Fluent API), ASP.NET Core Identity (schema only in this plan), xUnit, SQL Server LocalDB for dev/integration tests.

## Global Constraints

- `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every csproj.
- Every `decimal` property: explicit `HasColumnType("decimal(18,2)")`. Every string: explicit `HasMaxLength`.
- No lazy loading; explicit `.Include()`. Filter in query, materialize last.
- Async all the way; every public async method accepts `CancellationToken`.
- DTOs are `record` types. EF entities never cross the API boundary.
- Public endpoints never expose int IDs — products by `Slug`, orders by `OrderNumber` + phone.
- API money fields are integer **centavos** (`PriceCentavos`); domain/DB use `decimal(18,2)` PHP.
- Errors: RFC 7807 ProblemDetails only. Catch specific exceptions; never swallow silently (the one documented exception: notification failure after order commit is logged, not rethrown).
- Conventional Commits; commit after every green task. Never commit red.
- Local DB: `Server=(localdb)\MSSQLLocalDB` (present on Windows dev machines with VS Build Tools).
- Commit footer: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Solution scaffold

**Files:**
- Create: `BakedManila.sln`, `NuGet.config`, `.gitignore`, `src/BakedManila.Core/BakedManila.Core.csproj`, `src/BakedManila.Api/BakedManila.Api.csproj`, `tests/BakedManila.Core.Tests/BakedManila.Core.Tests.csproj`

**Interfaces:**
- Produces: compiling empty solution; project references Api→Core, Tests→Api+Core.

- [ ] **Step 1: Scaffold projects** (run from repo root `bakedmanila-api/`)

```powershell
dotnet new gitignore
dotnet new sln --name BakedManila
dotnet new classlib --name BakedManila.Core --output src/BakedManila.Core --framework net10.0
dotnet new webapi --name BakedManila.Api --output src/BakedManila.Api --framework net10.0 --use-controllers
dotnet new xunit --name BakedManila.Core.Tests --output tests/BakedManila.Core.Tests --framework net10.0
dotnet sln add src/BakedManila.Core src/BakedManila.Api tests/BakedManila.Core.Tests
dotnet add src/BakedManila.Api reference src/BakedManila.Core
dotnet add tests/BakedManila.Core.Tests reference src/BakedManila.Core src/BakedManila.Api
Remove-Item src/BakedManila.Core/Class1.cs
```

- [ ] **Step 2: Create `NuGet.config`** at repo root (corporate-feed isolation):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

- [ ] **Step 3: Harden csproj files.** In all three csproj files ensure inside the first `<PropertyGroup>`:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

Delete the WeatherForecast sample: `Remove-Item src/BakedManila.Api/Controllers/WeatherForecastController.cs, src/BakedManila.Api/WeatherForecast.cs -ErrorAction SilentlyContinue` (file names vary by template; delete whatever sample controller exists).

- [ ] **Step 4: Verify build and tests**

Run: `dotnet build; dotnet test`
Expected: Build succeeded, 0 warnings. 1 placeholder test passes.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "chore: scaffold BakedManila solution (Core, Api, Tests)"
```

---

### Task 2: Order status enums and transition rules

**Files:**
- Create: `src/BakedManila.Core/Domain/OrderStatus.cs`, `src/BakedManila.Core/Domain/FulfillmentType.cs`, `src/BakedManila.Core/Domain/PaymentMethodType.cs`, `src/BakedManila.Core/Domain/PaymentStatus.cs`, `src/BakedManila.Core/Domain/Exceptions/InvalidStatusTransitionException.cs`
- Test: `tests/BakedManila.Core.Tests/Domain/OrderStatusTransitionTests.cs` (tests `Order.TransitionTo`, entity created in Task 3 — **this task creates enums + exception only, tests come with Task 3**)

**Interfaces:**
- Produces: `enum OrderStatus { Pending, Confirmed, Ready, Completed, Cancelled }`, `enum FulfillmentType { Pickup, Delivery }`, `enum PaymentMethodType { ManualGcash, ManualBankTransfer, Cod }`, `enum PaymentStatus { Unpaid, Paid }`, `class InvalidStatusTransitionException(OrderStatus from, OrderStatus to) : Exception`.

- [ ] **Step 1: Create the four enum files** (one type per file):

```csharp
// src/BakedManila.Core/Domain/OrderStatus.cs
namespace BakedManila.Core.Domain;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Ready = 2,
    Completed = 3,
    Cancelled = 4,
}
```

```csharp
// src/BakedManila.Core/Domain/FulfillmentType.cs
namespace BakedManila.Core.Domain;

public enum FulfillmentType
{
    Pickup = 0,
    Delivery = 1,
}
```

```csharp
// src/BakedManila.Core/Domain/PaymentMethodType.cs
namespace BakedManila.Core.Domain;

public enum PaymentMethodType
{
    ManualGcash = 0,
    ManualBankTransfer = 1,
    Cod = 2,
}
```

```csharp
// src/BakedManila.Core/Domain/PaymentStatus.cs
namespace BakedManila.Core.Domain;

public enum PaymentStatus
{
    Unpaid = 0,
    Paid = 1,
}
```

- [ ] **Step 2: Create the exception**

```csharp
// src/BakedManila.Core/Domain/Exceptions/InvalidStatusTransitionException.cs
namespace BakedManila.Core.Domain.Exceptions;

public sealed class InvalidStatusTransitionException(OrderStatus from, OrderStatus to)
    : Exception($"Cannot transition order from {from} to {to}.")
{
    public OrderStatus From { get; } = from;
    public OrderStatus To { get; } = to;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: success, 0 warnings.

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "feat(domain): add order enums and InvalidStatusTransitionException"
```

---

### Task 3: Domain entities with behavior

**Files:**
- Create: `src/BakedManila.Core/Domain/Product.cs`, `src/BakedManila.Core/Domain/ProductImage.cs`, `src/BakedManila.Core/Domain/Order.cs`, `src/BakedManila.Core/Domain/OrderItem.cs`
- Test: `tests/BakedManila.Core.Tests/Domain/OrderStatusTransitionTests.cs`

**Interfaces:**
- Consumes: enums + exception from Task 2.
- Produces:
  - `class Product { int Id; string Name; string Slug; string Description; decimal Price; bool IsAvailable; bool IsDeleted; int SortOrder; DateTime CreatedAt; DateTime UpdatedAt; List<ProductImage> Images }`
  - `class ProductImage { int Id; int ProductId; string BlobName; int SortOrder }`
  - `class Order { int Id; string OrderNumber; OrderStatus Status; string CustomerName; string Phone; string? Email; string? MessengerHandle; DateOnly PreferredDate; bool IsRush; string? Notes; FulfillmentType FulfillmentType; decimal Subtotal; PaymentMethodType PaymentMethod; PaymentStatus PaymentStatus; string? CustomerId; DateTime CreatedAt; List<OrderItem> Items; void TransitionTo(OrderStatus next); void MarkPayment(PaymentStatus status) }`
  - `class OrderItem { int Id; int OrderId; int ProductId; string ProductName; decimal UnitPrice; int Quantity }`

- [ ] **Step 1: Write the failing tests** — transition matrix:

```csharp
// tests/BakedManila.Core.Tests/Domain/OrderStatusTransitionTests.cs
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;

namespace BakedManila.Core.Tests.Domain;

public class OrderStatusTransitionTests
{
    private static Order NewOrder(OrderStatus status) => new()
    {
        OrderNumber = "BM-2026-0001",
        Status = status,
        CustomerName = "Test",
        Phone = "09171234567",
        PreferredDate = new DateOnly(2026, 7, 10),
        FulfillmentType = FulfillmentType.Pickup,
        PaymentMethod = PaymentMethodType.ManualGcash,
    };

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Ready)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Ready, OrderStatus.Completed)]
    [InlineData(OrderStatus.Ready, OrderStatus.Cancelled)]
    public void TransitionTo_AllowsValidTransitions(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder(from);
        order.TransitionTo(to);
        Assert.Equal(to, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Ready)]
    [InlineData(OrderStatus.Pending, OrderStatus.Completed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Pending)]
    public void TransitionTo_ThrowsOnInvalidTransitions(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder(from);
        var ex = Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(to));
        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter OrderStatusTransitionTests`
Expected: compile error — `Order` not defined.

- [ ] **Step 3: Implement the entities**

```csharp
// src/BakedManila.Core/Domain/Product.cs
namespace BakedManila.Core.Domain;

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsDeleted { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ProductImage> Images { get; set; } = [];
}
```

```csharp
// src/BakedManila.Core/Domain/ProductImage.cs
namespace BakedManila.Core.Domain;

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string BlobName { get; set; }
    public int SortOrder { get; set; }
}
```

```csharp
// src/BakedManila.Core/Domain/OrderItem.cs
namespace BakedManila.Core.Domain;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public required string ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
```

```csharp
// src/BakedManila.Core/Domain/Order.cs
using BakedManila.Core.Domain.Exceptions;

namespace BakedManila.Core.Domain;

public class Order
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Ready, OrderStatus.Cancelled],
        [OrderStatus.Ready] = [OrderStatus.Completed, OrderStatus.Cancelled],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
    };

    public int Id { get; set; }
    public required string OrderNumber { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public required string CustomerName { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string? MessengerHandle { get; set; }
    public DateOnly PreferredDate { get; set; }
    public bool IsRush { get; set; }
    public string? Notes { get; set; }
    public FulfillmentType FulfillmentType { get; set; }
    public decimal Subtotal { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public string? CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];

    public void TransitionTo(OrderStatus next)
    {
        if (!AllowedTransitions[Status].Contains(next))
        {
            throw new InvalidStatusTransitionException(Status, next);
        }
        Status = next;
    }

    public void MarkPayment(PaymentStatus status) => PaymentStatus = status;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter OrderStatusTransitionTests`
Expected: 13 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(domain): add Product, Order, OrderItem entities with status transition rules"
```

---

### Task 4: DbContext, Fluent configuration, Identity renames, initial migration

**Files:**
- Create: `src/BakedManila.Core/Data/BakedManilaDbContext.cs`, `src/BakedManila.Core/Data/Migrations/` (generated)
- Modify: `src/BakedManila.Api/Program.cs` (register DbContext), `src/BakedManila.Api/appsettings.Development.json` (connection string)
- Test: `tests/BakedManila.Core.Tests/Data/DbContextSmokeTests.cs`

**Interfaces:**
- Consumes: entities from Task 3.
- Produces: `class BakedManilaDbContext : IdentityDbContext<IdentityUser>` with `DbSet<Product> Products`, `DbSet<Order> Orders`; SQL sequence `OrderNumberSeq`; global query filter `!IsDeleted` on Product; helper `tests/.../TestDb.cs` with `static string NewConnectionString()` for later tasks.

- [ ] **Step 1: Add packages**

```powershell
dotnet add src/BakedManila.Core package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/BakedManila.Core package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/BakedManila.Api package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef
```

- [ ] **Step 2: Write the failing smoke test**

```csharp
// tests/BakedManila.Core.Tests/Data/TestDb.cs
namespace BakedManila.Core.Tests.Data;

public static class TestDb
{
    public static string NewConnectionString() =>
        $@"Server=(localdb)\MSSQLLocalDB;Database=BakedManila.Tests.{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true";
}
```

```csharp
// tests/BakedManila.Core.Tests/Data/DbContextSmokeTests.cs
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Data;

public sealed class DbContextSmokeTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_CreateSchema_AndSoftDeleteFilterHidesDeletedProducts()
    {
        _db.Products.AddRange(
            new Product { Name = "Classic Chip", Slug = "classic-chip", Price = 280m },
            new Product { Name = "Old Flavor", Slug = "old-flavor", Price = 300m, IsDeleted = true });
        await _db.SaveChangesAsync();

        var visible = await _db.Products.ToListAsync();

        Assert.Single(visible);
        Assert.Equal("classic-chip", visible[0].Slug);
    }

    [Fact]
    public async Task OrderNumberSequence_Exists()
    {
        var next = await _db.Database
            .SqlQueryRaw<int>("SELECT CAST(NEXT VALUE FOR OrderNumberSeq AS int) AS [Value]")
            .SingleAsync();
        Assert.True(next >= 1);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter DbContextSmokeTests`
Expected: compile error — `BakedManilaDbContext` not defined.

- [ ] **Step 4: Implement the DbContext**

```csharp
// src/BakedManila.Core/Data/BakedManilaDbContext.cs
using BakedManila.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Data;

public class BakedManilaDbContext(DbContextOptions<BakedManilaDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Dedicated database — strip the AspNet prefix (code-style.md).
        builder.Entity<IdentityUser>().ToTable("Users");
        builder.Entity<IdentityRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");

        builder.HasSequence<long>("OrderNumberSeq").StartsAt(1).IncrementsBy(1);

        builder.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(120).IsRequired();
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.Description).HasMaxLength(2000);
            e.Property(p => p.Price).HasColumnType("decimal(18,2)").IsRequired();
            e.HasQueryFilter(p => !p.IsDeleted);
            e.HasMany(p => p.Images)
                .WithOne()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductImage>(e =>
        {
            e.Property(i => i.BlobName).HasMaxLength(260).IsRequired();
        });

        builder.Entity<Order>(e =>
        {
            e.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.Property(o => o.CustomerName).HasMaxLength(100).IsRequired();
            e.Property(o => o.Phone).HasMaxLength(20).IsRequired();
            e.Property(o => o.Email).HasMaxLength(256);
            e.Property(o => o.MessengerHandle).HasMaxLength(100);
            e.Property(o => o.Notes).HasMaxLength(1000);
            e.Property(o => o.Subtotal).HasColumnType("decimal(18,2)").IsRequired();
            e.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderItem>(e =>
        {
            e.Property(i => i.ProductName).HasMaxLength(100).IsRequired();
            e.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
            e.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
```

- [ ] **Step 5: Register DbContext in `Program.cs`** — add before `builder.Build()`:

```csharp
using BakedManila.Core.Data;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<BakedManilaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BakedManila")));
```

And in `src/BakedManila.Api/appsettings.Development.json` add:

```json
"ConnectionStrings": {
  "BakedManila": "Server=(localdb)\\MSSQLLocalDB;Database=BakedManila.Dev;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

- [ ] **Step 6: Generate the migration**

```powershell
dotnet ef migrations add InitialCreate --project src/BakedManila.Core --startup-project src/BakedManila.Api --output-dir Data/Migrations
```

Review the generated migration: Identity tables named without prefix, `OrderNumberSeq` sequence present, all decimals `decimal(18,2)`, FK delete behaviors as configured. If EF inferred anything else, fix the model, remove and re-add the migration.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter DbContextSmokeTests`
Expected: 2 tests PASS (requires LocalDB; `sqllocaldb info MSSQLLocalDB` to verify it exists).

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(data): add BakedManilaDbContext, Identity renames, initial migration"
```

---

### Task 5: Repositories

**Files:**
- Create: `src/BakedManila.Core/Repositories/IProductRepository.cs`, `src/BakedManila.Core/Repositories/IOrderRepository.cs`, `src/BakedManila.Core/Repositories/EfProductRepository.cs`, `src/BakedManila.Core/Repositories/EfOrderRepository.cs`
- Test: `tests/BakedManila.Core.Tests/Repositories/ProductRepositoryTests.cs`, `tests/BakedManila.Core.Tests/Repositories/OrderRepositoryTests.cs`

**Interfaces:**
- Consumes: `BakedManilaDbContext`, entities.
- Produces:

```csharp
public interface IProductRepository
{
    Task<List<Product>> GetAvailableAsync(CancellationToken ct);
    Task<Product?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct);
}

public interface IOrderRepository
{
    void Add(Order order);
    Task<Order?> GetByNumberAndPhoneAsync(string orderNumber, string phone, CancellationToken ct);
    Task<long> GetNextOrderSequenceAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Repositories/ProductRepositoryTests.cs
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Repositories;

public sealed class ProductRepositoryTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;
    private EfProductRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
        _repo = new EfProductRepository(_db);

        _db.Products.AddRange(
            new Product { Name = "B Second", Slug = "b-second", Price = 300m, SortOrder = 2 },
            new Product { Name = "A First", Slug = "a-first", Price = 280m, SortOrder = 1 },
            new Product { Name = "Sold Out", Slug = "sold-out", Price = 280m, IsAvailable = false },
            new Product { Name = "Gone", Slug = "gone", Price = 100m, IsDeleted = true });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task GetAvailableAsync_ReturnsOnlyAvailable_SortedBySortOrder()
    {
        var products = await _repo.GetAvailableAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["a-first", "b-second"], products.Select(p => p.Slug).ToArray());
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_ForUnknownSlug()
    {
        Assert.Null(await _repo.GetBySlugAsync("nope", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBySlugsAsync_ReturnsMatchingNonDeleted()
    {
        var products = await _repo.GetBySlugsAsync(["a-first", "gone"], TestContext.Current.CancellationToken);
        Assert.Single(products);
        Assert.Equal("a-first", products[0].Slug);
    }
}
```

```csharp
// tests/BakedManila.Core.Tests/Repositories/OrderRepositoryTests.cs
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Repositories;

public sealed class OrderRepositoryTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;
    private EfOrderRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
        _repo = new EfOrderRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private static Order NewOrder() => new()
    {
        OrderNumber = "BM-2026-0001",
        CustomerName = "Maria",
        Phone = "09171234567",
        PreferredDate = new DateOnly(2026, 7, 10),
        FulfillmentType = FulfillmentType.Pickup,
        PaymentMethod = PaymentMethodType.ManualGcash,
        Subtotal = 280m,
        Items = [new OrderItem { ProductId = 0, ProductName = "snap", UnitPrice = 280m, Quantity = 1 }],
    };

    [Fact]
    public async Task GetByNumberAndPhone_ReturnsOrderWithItems_OnExactMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.Products.Add(new Product { Id = 0, Name = "P", Slug = "p", Price = 280m });
        await _db.SaveChangesAsync(ct);
        var order = NewOrder();
        order.Items[0].ProductId = _db.Products.Local.First().Id;
        _repo.Add(order);
        await _repo.SaveChangesAsync(ct);

        var found = await _repo.GetByNumberAndPhoneAsync("BM-2026-0001", "09171234567", ct);
        Assert.NotNull(found);
        Assert.Single(found.Items);

        Assert.Null(await _repo.GetByNumberAndPhoneAsync("BM-2026-0001", "09990000000", ct));
        Assert.Null(await _repo.GetByNumberAndPhoneAsync("BM-2026-9999", "09171234567", ct));
    }

    [Fact]
    public async Task GetNextOrderSequence_Increments()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _repo.GetNextOrderSequenceAsync(ct);
        var second = await _repo.GetNextOrderSequenceAsync(ct);
        Assert.Equal(first + 1, second);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "ProductRepositoryTests|OrderRepositoryTests"`
Expected: compile error — repositories not defined.

- [ ] **Step 3: Implement repositories**

```csharp
// src/BakedManila.Core/Repositories/IProductRepository.cs
using BakedManila.Core.Domain;

namespace BakedManila.Core.Repositories;

public interface IProductRepository
{
    Task<List<Product>> GetAvailableAsync(CancellationToken ct);
    Task<Product?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct);
}
```

```csharp
// src/BakedManila.Core/Repositories/IOrderRepository.cs
using BakedManila.Core.Domain;

namespace BakedManila.Core.Repositories;

public interface IOrderRepository
{
    void Add(Order order);
    Task<Order?> GetByNumberAndPhoneAsync(string orderNumber, string phone, CancellationToken ct);
    Task<long> GetNextOrderSequenceAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
```

```csharp
// src/BakedManila.Core/Repositories/EfProductRepository.cs
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Repositories;

public sealed class EfProductRepository(BakedManilaDbContext db) : IProductRepository
{
    public Task<List<Product>> GetAvailableAsync(CancellationToken ct) =>
        db.Products
            .Where(p => p.IsAvailable)
            .Include(p => p.Images)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);

    public Task<Product?> GetBySlugAsync(string slug, CancellationToken ct) =>
        db.Products
            .Include(p => p.Images)
            .SingleOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct) =>
        db.Products
            .Where(p => slugs.Contains(p.Slug))
            .ToListAsync(ct);
}
```

```csharp
// src/BakedManila.Core/Repositories/EfOrderRepository.cs
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Repositories;

public sealed class EfOrderRepository(BakedManilaDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task<Order?> GetByNumberAndPhoneAsync(string orderNumber, string phone, CancellationToken ct) =>
        db.Orders
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.OrderNumber == orderNumber && o.Phone == phone, ct);

    public async Task<long> GetNextOrderSequenceAsync(CancellationToken ct) =>
        await db.Database
            .SqlQueryRaw<long>("SELECT NEXT VALUE FOR OrderNumberSeq AS [Value]")
            .SingleAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "ProductRepositoryTests|OrderRepositoryTests"`
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(data): add product and order repositories"
```

---

### Task 6: Payment strategy, notification seam, and OrderService

**Files:**
- Create: `src/BakedManila.Core/Services/IPaymentMethod.cs`, `src/BakedManila.Core/Services/ManualPayment.cs`, `src/BakedManila.Core/Services/INotificationSender.cs`, `src/BakedManila.Core/Services/OrderPlaced.cs`, `src/BakedManila.Core/Services/LoggingNotificationSender.cs`, `src/BakedManila.Core/Services/NewOrder.cs`, `src/BakedManila.Core/Services/OrderService.cs`, `src/BakedManila.Core/Domain/Exceptions/ProductUnavailableException.cs`, `src/BakedManila.Core/Domain/Exceptions/InvalidOrderException.cs`
- Test: `tests/BakedManila.Core.Tests/Services/OrderServiceTests.cs`

**Interfaces:**
- Consumes: repositories (Task 5), entities/enums.
- Produces:

```csharp
public interface IPaymentMethod
{
    PaymentMethodType Type { get; }
    PaymentStatus Initialize(Order order);
}

public interface INotificationSender
{
    Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct);
}

public sealed record OrderPlacedItem(string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderPlaced(string OrderNumber, string CustomerName, string Phone,
    DateOnly PreferredDate, bool IsRush, decimal Subtotal, IReadOnlyList<OrderPlacedItem> Items);

public sealed record NewOrderItem(string ProductSlug, int Quantity);
public sealed record NewOrder(IReadOnlyList<NewOrderItem> Items, string CustomerName, string Phone,
    string? Email, string? MessengerHandle, DateOnly PreferredDate, bool IsRush, string? Notes,
    FulfillmentType FulfillmentType, PaymentMethodType PaymentMethod);

public sealed class OrderService
{
    public OrderService(IProductRepository products, IOrderRepository orders,
        IEnumerable<IPaymentMethod> paymentMethods, INotificationSender notifier,
        ILogger<OrderService> logger, TimeProvider time);
    Task<Order> PlaceOrderAsync(NewOrder request, CancellationToken ct);
}
```

- Throws: `ProductNotFoundException` (unknown slug), `ProductUnavailableException` (IsAvailable=false), `InvalidOrderException` (empty items, quantity < 1, PreferredDate in the past).
- `ProductNotFoundException` is created here too: `src/BakedManila.Core/Domain/Exceptions/ProductNotFoundException.cs`.

- [ ] **Step 1: Write the failing tests** (hand-rolled fakes — no mocking library):

```csharp
// tests/BakedManila.Core.Tests/Services/OrderServiceTests.cs
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BakedManila.Core.Tests.Services;

file sealed class FakeProductRepository(List<Product> products) : IProductRepository
{
    public Task<List<Product>> GetAvailableAsync(CancellationToken ct) =>
        Task.FromResult(products.Where(p => p.IsAvailable && !p.IsDeleted).ToList());
    public Task<Product?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(products.FirstOrDefault(p => p.Slug == slug && !p.IsDeleted));
    public Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct) =>
        Task.FromResult(products.Where(p => slugs.Contains(p.Slug) && !p.IsDeleted).ToList());
}

file sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Added { get; } = [];
    public int SaveCount { get; private set; }
    private long _seq;

    public void Add(Order order) => Added.Add(order);
    public Task<Order?> GetByNumberAndPhoneAsync(string orderNumber, string phone, CancellationToken ct) =>
        Task.FromResult(Added.FirstOrDefault(o => o.OrderNumber == orderNumber && o.Phone == phone));
    public Task<long> GetNextOrderSequenceAsync(CancellationToken ct) => Task.FromResult(++_seq);
    public Task SaveChangesAsync(CancellationToken ct) { SaveCount++; return Task.CompletedTask; }
}

file sealed class RecordingNotificationSender : INotificationSender
{
    public List<OrderPlaced> Sent { get; } = [];
    public bool ThrowOnSend { get; set; }

    public Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct)
    {
        if (ThrowOnSend) throw new InvalidOperationException("smtp down");
        Sent.Add(notification);
        return Task.CompletedTask;
    }
}

public class OrderServiceTests
{
    private static readonly DateOnly Today = new(2026, 7, 4);

    private readonly FakeOrderRepository _orders = new();
    private readonly RecordingNotificationSender _notifier = new();
    private readonly FakeProductRepository _products = new([
        new Product { Id = 1, Name = "Classic Chip", Slug = "classic-chip", Price = 280m },
        new Product { Id = 2, Name = "Banana Bread", Slug = "banana-bread", Price = 350m },
        new Product { Id = 3, Name = "Sold Out", Slug = "sold-out", Price = 300m, IsAvailable = false },
    ]);

    private OrderService CreateSut()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 4, 8, 0, 0, TimeSpan.FromHours(8)));
        return new OrderService(_products, _orders, [new ManualPayment()], _notifier,
            NullLogger<OrderService>.Instance, time);
    }

    private static NewOrder ValidOrder() => new(
        Items: [new NewOrderItem("classic-chip", 2), new NewOrderItem("banana-bread", 1)],
        CustomerName: "Maria", Phone: "09171234567", Email: null, MessengerHandle: null,
        PreferredDate: Today.AddDays(3), IsRush: false, Notes: null,
        FulfillmentType: FulfillmentType.Pickup, PaymentMethod: PaymentMethodType.ManualGcash);

    [Fact]
    public async Task PlaceOrder_RepricesServerSide_SnapshotsItems_AndSaves()
    {
        var order = await CreateSut().PlaceOrderAsync(ValidOrder(), CancellationToken.None);

        Assert.Equal(280m * 2 + 350m, order.Subtotal);
        Assert.Equal(2, order.Items.Count);
        var chip = order.Items.Single(i => i.ProductName == "Classic Chip");
        Assert.Equal(280m, chip.UnitPrice);
        Assert.Equal(1, chip.ProductId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(1, _orders.SaveCount);
        Assert.Equal("BM-2026-0001", order.OrderNumber);
    }

    [Fact]
    public async Task PlaceOrder_SendsNotification_AfterSave()
    {
        await CreateSut().PlaceOrderAsync(ValidOrder(), CancellationToken.None);
        var sent = Assert.Single(_notifier.Sent);
        Assert.Equal("BM-2026-0001", sent.OrderNumber);
        Assert.Equal(2, sent.Items.Count);
    }

    [Fact]
    public async Task PlaceOrder_NotificationFailure_DoesNotFailOrder()
    {
        _notifier.ThrowOnSend = true;
        var order = await CreateSut().PlaceOrderAsync(ValidOrder(), CancellationToken.None);
        Assert.Equal(1, _orders.SaveCount);
        Assert.NotNull(order.OrderNumber);
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForUnknownProduct()
    {
        var request = ValidOrder() with { Items = [new NewOrderItem("nope", 1)] };
        await Assert.ThrowsAsync<ProductNotFoundException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForUnavailableProduct()
    {
        var request = ValidOrder() with { Items = [new NewOrderItem("sold-out", 1)] };
        await Assert.ThrowsAsync<ProductUnavailableException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PlaceOrder_Throws_ForInvalidQuantity(int quantity)
    {
        var request = ValidOrder() with { Items = [new NewOrderItem("classic-chip", quantity)] };
        await Assert.ThrowsAsync<InvalidOrderException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForEmptyItems()
    {
        var request = ValidOrder() with { Items = [] };
        await Assert.ThrowsAsync<InvalidOrderException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForPastPreferredDate()
    {
        var request = ValidOrder() with { PreferredDate = Today.AddDays(-1) };
        await Assert.ThrowsAsync<InvalidOrderException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }
}
```

Add the test-support package: `dotnet add tests/BakedManila.Core.Tests package Microsoft.Extensions.TimeProvider.Testing`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter OrderServiceTests`
Expected: compile error — `OrderService` not defined.

- [ ] **Step 3: Implement**

```csharp
// src/BakedManila.Core/Domain/Exceptions/ProductNotFoundException.cs
namespace BakedManila.Core.Domain.Exceptions;

public sealed class ProductNotFoundException(string slug)
    : Exception($"Product '{slug}' was not found.")
{
    public string Slug { get; } = slug;
}
```

```csharp
// src/BakedManila.Core/Domain/Exceptions/ProductUnavailableException.cs
namespace BakedManila.Core.Domain.Exceptions;

public sealed class ProductUnavailableException(string slug)
    : Exception($"Product '{slug}' is not available right now.")
{
    public string Slug { get; } = slug;
}
```

```csharp
// src/BakedManila.Core/Domain/Exceptions/InvalidOrderException.cs
namespace BakedManila.Core.Domain.Exceptions;

public sealed class InvalidOrderException(string message) : Exception(message);
```

```csharp
// src/BakedManila.Core/Services/IPaymentMethod.cs
using BakedManila.Core.Domain;

namespace BakedManila.Core.Services;

public interface IPaymentMethod
{
    PaymentMethodType Type { get; }
    PaymentStatus Initialize(Order order);
}
```

```csharp
// src/BakedManila.Core/Services/ManualPayment.cs
using BakedManila.Core.Domain;

namespace BakedManila.Core.Services;

/// Covers all v1 methods settled outside the app (GCash, bank transfer, COD).
public sealed class ManualPayment : IPaymentMethod
{
    public PaymentMethodType Type => PaymentMethodType.ManualGcash;
    public PaymentStatus Initialize(Order order) => PaymentStatus.Unpaid;
}
```

```csharp
// src/BakedManila.Core/Services/OrderPlaced.cs
namespace BakedManila.Core.Services;

public sealed record OrderPlacedItem(string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderPlaced(
    string OrderNumber,
    string CustomerName,
    string Phone,
    DateOnly PreferredDate,
    bool IsRush,
    decimal Subtotal,
    IReadOnlyList<OrderPlacedItem> Items);
```

```csharp
// src/BakedManila.Core/Services/INotificationSender.cs
namespace BakedManila.Core.Services;

public interface INotificationSender
{
    Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct);
}
```

```csharp
// src/BakedManila.Core/Services/LoggingNotificationSender.cs
using Microsoft.Extensions.Logging;

namespace BakedManila.Core.Services;

/// Placeholder sender until ACS Email lands (Plan 2). Logs the notification.
public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger)
    : INotificationSender
{
    public Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct)
    {
        logger.LogInformation(
            "Order placed: {OrderNumber} by {CustomerName} ({Phone}) for {PreferredDate}, subtotal {Subtotal}",
            notification.OrderNumber, notification.CustomerName, notification.Phone,
            notification.PreferredDate, notification.Subtotal);
        return Task.CompletedTask;
    }
}
```

```csharp
// src/BakedManila.Core/Services/NewOrder.cs
using BakedManila.Core.Domain;

namespace BakedManila.Core.Services;

public sealed record NewOrderItem(string ProductSlug, int Quantity);

public sealed record NewOrder(
    IReadOnlyList<NewOrderItem> Items,
    string CustomerName,
    string Phone,
    string? Email,
    string? MessengerHandle,
    DateOnly PreferredDate,
    bool IsRush,
    string? Notes,
    FulfillmentType FulfillmentType,
    PaymentMethodType PaymentMethod);
```

```csharp
// src/BakedManila.Core/Services/OrderService.cs
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
using BakedManila.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace BakedManila.Core.Services;

public sealed class OrderService(
    IProductRepository products,
    IOrderRepository orders,
    IEnumerable<IPaymentMethod> paymentMethods,
    INotificationSender notifier,
    ILogger<OrderService> logger,
    TimeProvider time)
{
    public async Task<Order> PlaceOrderAsync(NewOrder request, CancellationToken ct)
    {
        Validate(request);

        var slugs = request.Items.Select(i => i.ProductSlug).Distinct().ToList();
        var found = await products.GetBySlugsAsync(slugs, ct);
        var bySlug = found.ToDictionary(p => p.Slug);

        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(ct),
            CustomerName = request.CustomerName,
            Phone = request.Phone,
            Email = request.Email,
            MessengerHandle = request.MessengerHandle,
            PreferredDate = request.PreferredDate,
            IsRush = request.IsRush,
            Notes = request.Notes,
            FulfillmentType = request.FulfillmentType,
            PaymentMethod = request.PaymentMethod,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        foreach (var item in request.Items)
        {
            if (!bySlug.TryGetValue(item.ProductSlug, out var product))
            {
                throw new ProductNotFoundException(item.ProductSlug);
            }
            if (!product.IsAvailable)
            {
                throw new ProductUnavailableException(item.ProductSlug);
            }
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price, // server-side price, never the client's
                Quantity = item.Quantity,
            });
        }

        order.Subtotal = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        order.PaymentStatus = paymentMethods.Single().Initialize(order);

        orders.Add(order);
        await orders.SaveChangesAsync(ct);

        await NotifySafelyAsync(order, ct);
        return order;
    }

    private void Validate(NewOrder request)
    {
        if (request.Items.Count == 0)
        {
            throw new InvalidOrderException("Order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity < 1))
        {
            throw new InvalidOrderException("Item quantities must be at least 1.");
        }
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (request.PreferredDate < today)
        {
            throw new InvalidOrderException("Preferred date cannot be in the past.");
        }
    }

    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct)
    {
        var seq = await orders.GetNextOrderSequenceAsync(ct);
        var year = time.GetUtcNow().Year;
        return $"BM-{year}-{seq:D4}";
    }

    private async Task NotifySafelyAsync(Order order, CancellationToken ct)
    {
        try
        {
            var items = order.Items
                .Select(i => new OrderPlacedItem(i.ProductName, i.UnitPrice, i.Quantity))
                .ToList();
            await notifier.SendOrderPlacedAsync(new OrderPlaced(order.OrderNumber, order.CustomerName,
                order.Phone, order.PreferredDate, order.IsRush, order.Subtotal, items), ct);
        }
        catch (Exception ex) // deliberate broad catch: notification must never fail a committed order
        {
            logger.LogError(ex, "Failed to send OrderPlaced notification for {OrderNumber}", order.OrderNumber);
        }
    }
}
```

Note: `IEnumerable<IPaymentMethod>` + `.Single()` is the strategy seam — when PayMongo lands, selection becomes `Single(m => m.Type == request.PaymentMethod)`. YAGNI keeps it `.Single()` today.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter OrderServiceTests`
Expected: 9 tests PASS.

- [ ] **Step 5: Run the whole suite, then commit**

Run: `dotnet test`
Expected: all green.

```powershell
git add -A
git commit -m "feat(core): add OrderService with server-side pricing, payment strategy, notification seam"
```

---

### Task 7: Products endpoints + ProblemDetails wiring

**Files:**
- Create: `src/BakedManila.Api/Dtos/ProductDto.cs`, `src/BakedManila.Api/Controllers/ProductsController.cs`, `src/BakedManila.Api/Middleware/DomainExceptionHandler.cs`
- Modify: `src/BakedManila.Api/Program.cs` (DI registrations, ProblemDetails, exception handler)
- Test: `tests/BakedManila.Core.Tests/Api/ApiFactory.cs`, `tests/BakedManila.Core.Tests/Api/ProductsEndpointTests.cs`

**Interfaces:**
- Consumes: `IProductRepository`, entities.
- Produces:
  - `record ProductImageDto(string Url)`; `record ProductDto(string Slug, string Name, string Description, int PriceCentavos, bool IsAvailable, IReadOnlyList<ProductImageDto> Images)` with `static ProductDto FromEntity(Product p, string imageBaseUrl)`.
  - `GET /api/products` → `200 [ProductDto]`; `GET /api/products/{slug}` → `200 ProductDto | 404 ProblemDetails`.
  - `ApiFactory : WebApplicationFactory<Program>` creating a fresh LocalDB per instance — reused by Task 8.
  - `DomainExceptionHandler : IExceptionHandler` mapping `ProductNotFoundException`→404, `ProductUnavailableException`→409, `InvalidOrderException`→422, `InvalidStatusTransitionException`→409.
- Config key: `Storage:PublicBaseUrl` (default `""` in appsettings; image URLs are `{base}/{BlobName}`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Api/ApiFactory.cs
using BakedManila.Core.Data;
using BakedManila.Core.Tests.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace BakedManila.Core.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = TestDb.NewConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:BakedManila", _connectionString);
        builder.UseSetting("Storage:PublicBaseUrl", "https://img.test");
    }

    public async Task<BakedManilaDbContext> CreateDbAsync()
    {
        var db = new BakedManilaDbContext(new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(_connectionString).Options);
        await db.Database.MigrateAsync();
        return db;
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var db = await CreateDbAsync())
        {
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }
}
```

```csharp
// tests/BakedManila.Core.Tests/Api/ProductsEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using BakedManila.Core.Domain;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class ProductsEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync();
        db.Products.AddRange(
            new Product
            {
                Name = "Classic Chip", Slug = "classic-chip", Price = 280m, SortOrder = 1,
                Description = "Crisp edges", Images = [new ProductImage { BlobName = "products/1/a.jpg" }],
            },
            new Product { Name = "Hidden", Slug = "hidden", Price = 300m, IsAvailable = false });
        await db.SaveChangesAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GetProducts_ReturnsAvailableOnly_WithCentavosAndImageUrls()
    {
        var products = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products");
        var p = Assert.Single(products!);
        Assert.Equal("classic-chip", p.Slug);
        Assert.Equal(28000, p.PriceCentavos);
        Assert.Equal("https://img.test/products/1/a.jpg", Assert.Single(p.Images).Url);
    }

    [Fact]
    public async Task GetProductBySlug_Returns404ProblemDetails_ForUnknown()
    {
        var response = await _client.GetAsync("/api/products/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task GetProductBySlug_ReturnsUnavailableProductToo()
    {
        // detail page still viewable when sold out; storefront shows "sold out"
        var p = await _client.GetFromJsonAsync<ProductDto>("/api/products/hidden");
        Assert.False(p!.IsAvailable);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ProductsEndpointTests`
Expected: compile error — `ProductDto` not defined. (`Program` must be visible: add `public partial class Program;` as the last line of `Program.cs` in Step 3.)

- [ ] **Step 3: Implement DTO, controller, exception handler, Program wiring**

```csharp
// src/BakedManila.Api/Dtos/ProductDto.cs
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record ProductImageDto(string Url);

public sealed record ProductDto(
    string Slug,
    string Name,
    string Description,
    int PriceCentavos,
    bool IsAvailable,
    IReadOnlyList<ProductImageDto> Images)
{
    public static ProductDto FromEntity(Product p, string imageBaseUrl) => new(
        p.Slug,
        p.Name,
        p.Description,
        (int)(p.Price * 100),
        p.IsAvailable,
        p.Images.OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto($"{imageBaseUrl}/{i.BlobName}"))
            .ToList());
}
```

```csharp
// src/BakedManila.Api/Controllers/ProductsController.cs
using BakedManila.Api.Dtos;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IProductRepository products, IConfiguration config)
    : ControllerBase
{
    private string ImageBaseUrl => config["Storage:PublicBaseUrl"] ?? string.Empty;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAvailable(CancellationToken ct)
    {
        var list = await products.GetAvailableAsync(ct);
        return Ok(list.Select(p => ProductDto.FromEntity(p, ImageBaseUrl)).ToList());
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ProductDto>> GetBySlug(string slug, CancellationToken ct)
    {
        var product = await products.GetBySlugAsync(slug, ct);
        return product is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
            : Ok(ProductDto.FromEntity(product, ImageBaseUrl));
    }
}
```

```csharp
// src/BakedManila.Api/Middleware/DomainExceptionHandler.cs
using BakedManila.Core.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Middleware;

public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        (int status, string title) = exception switch
        {
            ProductNotFoundException => (StatusCodes.Status404NotFound, "Product not found"),
            ProductUnavailableException => (StatusCodes.Status409Conflict, "Product unavailable"),
            InvalidStatusTransitionException => (StatusCodes.Status409Conflict, "Invalid status transition"),
            InvalidOrderException => (StatusCodes.Status422UnprocessableEntity, "Invalid order"),
            _ => (0, string.Empty),
        };
        if (status == 0)
        {
            return false; // not ours — global handler logs and returns 500
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
            },
        });
    }
}
```

`Program.cs` (full file after this task):

```csharp
using BakedManila.Api.Middleware;
using BakedManila.Core.Data;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<BakedManilaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BakedManila")));

builder.Services.AddScoped<IProductRepository, EfProductRepository>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IPaymentMethod, ManualPayment>();
builder.Services.AddScoped<INotificationSender, LoggingNotificationSender>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ProductsEndpointTests`
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(api): add public products endpoints with ProblemDetails handling"
```

---

### Task 8: Orders endpoints (place + lookup) with rate limiting

**Files:**
- Create: `src/BakedManila.Api/Dtos/PlaceOrderRequest.cs`, `src/BakedManila.Api/Dtos/OrderDto.cs`, `src/BakedManila.Api/Controllers/OrdersController.cs`
- Modify: `src/BakedManila.Api/Program.cs` (rate limiter)
- Test: `tests/BakedManila.Core.Tests/Api/OrdersEndpointTests.cs`

**Interfaces:**
- Consumes: `OrderService.PlaceOrderAsync(NewOrder, CancellationToken)`, `IOrderRepository.GetByNumberAndPhoneAsync`, `ApiFactory` (Task 7).
- Produces:
  - `record PlaceOrderItemRequest(string ProductSlug, int Quantity)`; `record PlaceOrderRequest(...)` (below) with DataAnnotations.
  - `record OrderItemDto(string ProductName, int UnitPriceCentavos, int Quantity)`; `record OrderDto(string OrderNumber, string Status, DateOnly PreferredDate, string FulfillmentType, int SubtotalCentavos, IReadOnlyList<OrderItemDto> Items)` with `static OrderDto FromEntity(Order o)` (Status/FulfillmentType as `ToString()` strings).
  - `POST /api/orders` → `201` + OrderDto, `Location: /api/orders/{number}`; `GET /api/orders/{orderNumber}?phone=` → `200 | 404`.
  - Rate limit policy name: `"orders"` — fixed window, permit 5 per 10 min, partitioned by client IP, 429 + `Retry-After: 600`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BakedManila.Core.Tests/Api/OrdersEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;

namespace BakedManila.Core.Tests.Api;

public sealed class OrdersEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync();
        db.Products.Add(new Product { Name = "Classic Chip", Slug = "classic-chip", Price = 280m });
        await db.SaveChangesAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object ValidBody(string date) => new
    {
        items = new[] { new { productSlug = "classic-chip", quantity = 2 } },
        customerName = "Maria",
        phone = "09171234567",
        preferredDate = date,
        isRush = false,
        fulfillmentType = "Pickup",
        paymentMethod = "ManualGcash",
    };

    private static string FutureDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");

    [Fact]
    public async Task PlaceOrder_Returns201_WithServerPricedOrder()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.StartsWith("BM-", order!.OrderNumber);
        Assert.Equal(56000, order.SubtotalCentavos);
        Assert.Equal("Pending", order.Status);
        Assert.Contains($"/api/orders/{order.OrderNumber}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PlaceOrder_Returns422_ForPastDate()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", ValidBody("2020-01-01"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_Returns400_ForMissingPhone()
    {
        var body = new
        {
            items = new[] { new { productSlug = "classic-chip", quantity = 1 } },
            customerName = "Maria",
            preferredDate = FutureDate,
            fulfillmentType = "Pickup",
            paymentMethod = "ManualGcash",
        };
        var response = await _client.PostAsJsonAsync("/api/orders", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_RequiresMatchingPhone()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate)))
            .Content.ReadFromJsonAsync<OrderDto>();

        var ok = await _client.GetAsync($"/api/orders/{created!.OrderNumber}?phone=09171234567");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var wrongPhone = await _client.GetAsync($"/api/orders/{created.OrderNumber}?phone=09990000000");
        Assert.Equal(HttpStatusCode.NotFound, wrongPhone.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_RateLimits_After5PerWindow()
    {
        for (var i = 0; i < 5; i++)
        {
            var ok = await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }
        var limited = await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter OrdersEndpointTests`
Expected: compile error — `OrderDto` not defined.

- [ ] **Step 3: Implement DTOs, controller, rate limiter**

```csharp
// src/BakedManila.Api/Dtos/PlaceOrderRequest.cs
using System.ComponentModel.DataAnnotations;
using BakedManila.Core.Domain;
using BakedManila.Core.Services;

namespace BakedManila.Api.Dtos;

public sealed record PlaceOrderItemRequest(
    [property: Required, MaxLength(120)] string ProductSlug,
    [property: Range(1, 100)] int Quantity);

public sealed record PlaceOrderRequest(
    [property: Required, MinLength(1)] List<PlaceOrderItemRequest> Items,
    [property: Required, MaxLength(100)] string CustomerName,
    [property: Required, MaxLength(20), RegularExpression(@"^(\+63|0)9\d{9}$",
        ErrorMessage = "Phone must be a PH mobile number, e.g. 09171234567.")] string Phone,
    [property: EmailAddress, MaxLength(256)] string? Email,
    [property: MaxLength(100)] string? MessengerHandle,
    [property: Required] DateOnly PreferredDate,
    bool IsRush,
    [property: MaxLength(1000)] string? Notes,
    [property: Required] FulfillmentType FulfillmentType,
    [property: Required] PaymentMethodType PaymentMethod)
{
    public NewOrder ToCommand() => new(
        Items.Select(i => new NewOrderItem(i.ProductSlug, i.Quantity)).ToList(),
        CustomerName, Phone, Email, MessengerHandle, PreferredDate, IsRush, Notes,
        FulfillmentType, PaymentMethod);
}
```

```csharp
// src/BakedManila.Api/Dtos/OrderDto.cs
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record OrderItemDto(string ProductName, int UnitPriceCentavos, int Quantity);

public sealed record OrderDto(
    string OrderNumber,
    string Status,
    DateOnly PreferredDate,
    string FulfillmentType,
    int SubtotalCentavos,
    IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto FromEntity(Order o) => new(
        o.OrderNumber,
        o.Status.ToString(),
        o.PreferredDate,
        o.FulfillmentType.ToString(),
        (int)(o.Subtotal * 100),
        o.Items.Select(i => new OrderItemDto(i.ProductName, (int)(i.UnitPrice * 100), i.Quantity))
            .ToList());
}
```

```csharp
// src/BakedManila.Api/Controllers/OrdersController.cs
using BakedManila.Api.Dtos;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderService orderService, IOrderRepository orders)
    : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("orders")]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var order = await orderService.PlaceOrderAsync(request.ToCommand(), ct);
        var dto = OrderDto.FromEntity(order);
        return CreatedAtAction(nameof(Lookup), new { orderNumber = dto.OrderNumber }, dto);
    }

    [HttpGet("{orderNumber}")]
    public async Task<ActionResult<OrderDto>> Lookup(
        string orderNumber, [FromQuery] string phone, CancellationToken ct)
    {
        var order = await orders.GetByNumberAndPhoneAsync(orderNumber, phone, ct);
        return order is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
            : Ok(OrderDto.FromEntity(order));
    }
}
```

In `Program.cs`, add before `builder.Build()`:

```csharp
using System.Threading.RateLimiting;

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
});
```

And after `app.UseStatusCodePages();` add: `app.UseRateLimiter();`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter OrdersEndpointTests`
Expected: 5 tests PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test`
Expected: all green.

```powershell
git add -A
git commit -m "feat(api): add order placement and lookup endpoints with rate limiting"
```

---

### Task 9: Dev seed data, README, final verification

**Files:**
- Create: `src/BakedManila.Api/Data/DevSeeder.cs`, `README.md`
- Modify: `src/BakedManila.Api/Program.cs`

**Interfaces:**
- Consumes: `BakedManilaDbContext`.
- Produces: on Development startup — migrate DB + seed 4 products if the table is empty; README with run instructions.

- [ ] **Step 1: Implement the seeder**

```csharp
// src/BakedManila.Api/Data/DevSeeder.cs
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Api.Data;

public static class DevSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakedManilaDbContext>();
        await db.Database.MigrateAsync(ct);

        if (await db.Products.AnyAsync(ct))
        {
            return;
        }

        db.Products.AddRange(
            new Product { Name = "Classic Chocolate Chip", Slug = "classic-chocolate-chip", Description = "Crisp edges, chewy middle.", Price = 280m, SortOrder = 1 },
            new Product { Name = "Double Chocolate", Slug = "double-chocolate", Description = "Fudgy with molten chips.", Price = 320m, SortOrder = 2 },
            new Product { Name = "Red Velvet", Slug = "red-velvet", Description = "White chocolate buttons.", Price = 320m, SortOrder = 3 },
            new Product { Name = "Chocolate Chunk Banana Bread", Slug = "banana-bread", Description = "Fluffy loaf, melty chunks.", Price = 350m, SortOrder = 4 });
        await db.SaveChangesAsync(ct);
    }
}
```

In `Program.cs`, after `var app = builder.Build();`:

```csharp
if (app.Environment.IsDevelopment())
{
    await BakedManila.Api.Data.DevSeeder.MigrateAndSeedAsync(app.Services, CancellationToken.None);
}
```

**Note:** `ApiFactory` tests run in Development by default and hit this path — that's fine (they migrate + seed); adjust the two Task 7/8 test fixtures if seed products interfere: they don't (`classic-chip` etc. are distinct slugs from seeded ones, and assertions use exact slugs — except `ProductsEndpointTests.GetProducts_ReturnsAvailableOnly...` which asserts `Single`. Set the environment to `"Testing"` in `ApiFactory.ConfigureWebHost` via `builder.UseEnvironment("Testing");` to skip seeding, and re-run all tests.)

- [ ] **Step 2: Manually verify the API runs**

```powershell
dotnet run --project src/BakedManila.Api
```

Then in another shell: `Invoke-RestMethod http://localhost:5000/api/products` (adjust port from launch output).
Expected: 4 seeded products with `priceCentavos`.

- [ ] **Step 3: Write `README.md`**

```markdown
# bakedmanila-api

Pre-order API for BakedManila — cookies & banana bread, Quezon City.

## Architecture

Pragmatic layered monolith: Controllers → OrderService → Repositories → EF Core.
Clean Architecture/CQRS were considered and rejected per
[standards-docs/design-patterns.md](https://github.com/paurodriguez0220/standards-docs) —
one database, one consumer; the layers would be indirection without benefit.
Growth seams: `IPaymentMethod` (PayMongo later), `INotificationSender` (ACS Email in Plan 2),
`Order.CustomerId` (customer accounts later).

Spec: `docs/superpowers/specs/2026-07-04-bakedmanila-design.md`.

## Run locally

Requires .NET 10 SDK and SQL Server LocalDB (`sqllocaldb info MSSQLLocalDB`).

    dotnet run --project src/BakedManila.Api

Dev startup migrates and seeds the `BakedManila.Dev` LocalDB database.
OpenAPI: `GET /openapi/v1.json` (Development only).

## Test

    dotnet test

Integration tests create throwaway LocalDB databases and delete them on dispose.
```

- [ ] **Step 4: Full verification**

Run: `dotnet build; dotnet test`
Expected: 0 warnings, all tests green (~22 tests).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(api): add dev seeding and README; complete API core plan"
```

---

## Plan Self-Review Notes

- Spec coverage (this plan's slice): products read endpoints ✔ (Task 7), order placement with server-side pricing/snapshots/transaction ✔ (Tasks 6, 8), order lookup with phone guard ✔ (Task 8), rate limiting ✔ (Task 8), ProblemDetails everywhere ✔ (Task 7), soft delete + query filter ✔ (Task 4), Identity schema with renamed tables ✔ (Task 4), sequence-based order numbers ✔ (Tasks 4–6), notification seam with logged failure ✔ (Task 6), centavos DTOs ✔ (Tasks 7–8).
- Deliberately deferred to later plans: admin auth/JWT + admin endpoints, image upload/Blob, ACS email, frontend, infra/CI.
- Type consistency verified: `NewOrder`/`NewOrderItem` (Task 6) match `PlaceOrderRequest.ToCommand()` (Task 8); `ApiFactory` (Task 7) reused in Task 8; `TestDb.NewConnectionString()` (Task 4) reused in Tasks 5, 7.
