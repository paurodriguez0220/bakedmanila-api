using System.ComponentModel.DataAnnotations;

namespace BakedManila.Api.Dtos;

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(128)] string Password);
