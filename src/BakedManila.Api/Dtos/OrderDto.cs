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
