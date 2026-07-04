using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;

namespace BakedManila.Core.Tests.Domain;

public class OrderStatusTransitionTests
{
    private static Order NewOrder(OrderStatus status) => new()
    {
        OrderNumber = "BM-2026-0001",
        Status = status,
        CustomerName = "Test",
        Phone = "09171234567",
        PreferredDate = new DateOnly(2026, 7, 10),
        FulfillmentType = FulfillmentType.Pickup,
        PaymentMethod = PaymentMethodType.ManualGcash,
    };

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Ready)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Ready, OrderStatus.Completed)]
    [InlineData(OrderStatus.Ready, OrderStatus.Cancelled)]
    public void TransitionTo_AllowsValidTransitions(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder(from);
        order.TransitionTo(to);
        Assert.Equal(to, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Ready)]
    [InlineData(OrderStatus.Pending, OrderStatus.Completed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Pending)]
    public void TransitionTo_ThrowsOnInvalidTransitions(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder(from);
        var ex = Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(to));
        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
    }
}
