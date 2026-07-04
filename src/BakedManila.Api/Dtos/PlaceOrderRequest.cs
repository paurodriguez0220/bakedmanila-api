using System.ComponentModel.DataAnnotations;
using BakedManila.Core.Domain;
using BakedManila.Core.Services;

namespace BakedManila.Api.Dtos;

public sealed record PlaceOrderItemRequest(
    [Required, MaxLength(120)] string ProductSlug,
    [Range(1, 100)] int Quantity);

public sealed record PlaceOrderRequest(
    [Required, MinLength(1)] List<PlaceOrderItemRequest> Items,
    [Required, MaxLength(100)] string CustomerName,
    [Required, MaxLength(20), RegularExpression(@"^(\+63|0)9\d{9}$",
        ErrorMessage = "Phone must be a PH mobile number, e.g. 09171234567.")] string Phone,
    [EmailAddress, MaxLength(256)] string? Email,
    [MaxLength(100)] string? MessengerHandle,
    [Required] DateOnly PreferredDate,
    bool IsRush,
    [MaxLength(1000)] string? Notes,
    [Required] FulfillmentType FulfillmentType,
    [Required] PaymentMethodType PaymentMethod)
{
    public NewOrder ToCommand() => new(
        Items.Select(i => new NewOrderItem(i.ProductSlug, i.Quantity)).ToList(),
        CustomerName, Phone, Email, MessengerHandle, PreferredDate, IsRush, Notes,
        FulfillmentType, PaymentMethod);
}
