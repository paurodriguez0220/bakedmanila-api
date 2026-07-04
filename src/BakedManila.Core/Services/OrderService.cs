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
