namespace BakedManila.Api.Dtos;

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc);
