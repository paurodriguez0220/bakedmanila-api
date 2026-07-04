using BakedManila.Core.Domain;

namespace BakedManila.Core.Services;

/// Covers all v1 methods settled outside the app (GCash, bank transfer, COD).
public sealed class ManualPayment : IPaymentMethod
{
    public PaymentMethodType Type => PaymentMethodType.ManualGcash;
    public PaymentStatus Initialize(Order order) => PaymentStatus.Unpaid;
}
