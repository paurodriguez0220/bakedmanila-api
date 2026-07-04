using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BakedManila.Core.Services;

public sealed class AcsEmailNotificationSender(
    EmailClient client,
    IConfiguration config,
    ILogger<AcsEmailNotificationSender> logger) : INotificationSender
{
    public async Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct)
    {
        var from = config["Email:From"]
            ?? throw new InvalidOperationException("Missing Email:From configuration.");
        var to = config["Email:To"]
            ?? throw new InvalidOperationException("Missing Email:To configuration.");

        var (subject, plainText) = OrderPlacedEmail.Build(notification);
        var message = new EmailMessage(from, to, new EmailContent(subject) { PlainText = plainText });
        _ = await client.SendAsync(WaitUntil.Started, message, ct);
        logger.LogInformation("Order email queued for {OrderNumber}", notification.OrderNumber);
    }
}
