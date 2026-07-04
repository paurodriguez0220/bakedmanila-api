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
