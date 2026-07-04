using BakedManila.Core.Domain.Exceptions;

namespace BakedManila.Core.Domain;

public class Order
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Ready, OrderStatus.Cancelled],
        [OrderStatus.Ready] = [OrderStatus.Completed, OrderStatus.Cancelled],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
    };

    public int Id { get; set; }
    public required string OrderNumber { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public required string CustomerName { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string? MessengerHandle { get; set; }
    public DateOnly PreferredDate { get; set; }
    public bool IsRush { get; set; }
    public string? Notes { get; set; }
    public FulfillmentType FulfillmentType { get; set; }
    public decimal Subtotal { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public string? CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];

    public void TransitionTo(OrderStatus next)
    {
        if (!AllowedTransitions[Status].Contains(next))
        {
            throw new InvalidStatusTransitionException(Status, next);
        }
        Status = next;
    }

    public void MarkPayment(PaymentStatus status) => PaymentStatus = status;
}
