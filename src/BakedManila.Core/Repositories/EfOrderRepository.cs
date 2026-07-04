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
        (await db.Database
            .SqlQueryRaw<long>("SELECT NEXT VALUE FOR OrderNumberSeq AS [Value]")
            .ToListAsync(ct)).Single();

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

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
}
