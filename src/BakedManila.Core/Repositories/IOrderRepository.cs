using BakedManila.Core.Domain;

namespace BakedManila.Core.Repositories;

public interface IOrderRepository
{
    void Add(Order order);
    Task<Order?> GetByNumberAndPhoneAsync(string orderNumber, string phone, CancellationToken ct);
    Task<long> GetNextOrderSequenceAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    Task<List<Order>> GetFilteredAsync(OrderStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<Order?> GetByIdAsync(int id, CancellationToken ct);
}
