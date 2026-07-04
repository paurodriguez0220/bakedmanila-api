namespace BakedManila.Core.Services;

public interface INotificationSender
{
    Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct);
}
