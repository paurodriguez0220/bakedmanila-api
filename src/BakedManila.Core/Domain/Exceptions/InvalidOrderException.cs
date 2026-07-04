namespace BakedManila.Core.Domain.Exceptions;

public sealed class InvalidOrderException(string message) : Exception(message);
