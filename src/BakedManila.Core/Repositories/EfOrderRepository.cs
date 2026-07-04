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
}
