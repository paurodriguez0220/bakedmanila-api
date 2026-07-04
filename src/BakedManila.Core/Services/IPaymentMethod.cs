using BakedManila.Core.Domain;

namespace BakedManila.Core.Services;

public interface IPaymentMethod
{
    PaymentMethodType Type { get; }
    PaymentStatus Initialize(Order order);
}
