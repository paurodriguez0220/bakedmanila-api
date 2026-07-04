namespace BakedManila.Core.Domain.Exceptions;

public sealed class InvalidStatusTransitionException(OrderStatus from, OrderStatus to)
    : Exception($"Cannot transition order from {from} to {to}.")
{
    public OrderStatus From { get; } = from;
    public OrderStatus To { get; } = to;
}
