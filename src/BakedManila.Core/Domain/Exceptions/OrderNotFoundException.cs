namespace BakedManila.Core.Domain.Exceptions;

public sealed class OrderNotFoundException(int id) : Exception($"Order {id} was not found.")
{
    public int Id { get; } = id;
}
