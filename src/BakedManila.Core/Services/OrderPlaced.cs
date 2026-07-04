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
