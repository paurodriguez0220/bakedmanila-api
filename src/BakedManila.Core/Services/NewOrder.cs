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
