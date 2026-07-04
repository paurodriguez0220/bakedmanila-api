using Microsoft.Extensions.Logging;

namespace BakedManila.Core.Services;

/// Placeholder sender until ACS Email lands (Plan 2). Logs the notification.
public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger)
    : INotificationSender
{
    public Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct)
    {
        logger.LogInformation(
            "Order placed: {OrderNumber} by {CustomerName} ({Phone}) for {PreferredDate}, subtotal {Subtotal}",
            notification.OrderNumber, notification.CustomerName, notification.Phone,
            notification.PreferredDate, notification.Subtotal);
        return Task.CompletedTask;
    }
}
