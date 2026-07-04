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
            e.ToTable("ProductImages");
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
            e.ToTable("OrderItems");
            e.Property(i => i.ProductName).HasMaxLength(100).IsRequired();
            e.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
            e.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
