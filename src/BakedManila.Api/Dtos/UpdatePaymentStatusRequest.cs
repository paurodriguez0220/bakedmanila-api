using System.ComponentModel.DataAnnotations;
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record UpdatePaymentStatusRequest([Required] PaymentStatus? PaymentStatus);
