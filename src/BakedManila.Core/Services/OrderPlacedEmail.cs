using System.Globalization;
using System.Text;

namespace BakedManila.Core.Services;

public static class OrderPlacedEmail
{
    public static (string Subject, string PlainText) Build(OrderPlaced n)
    {
        var subject = $"{(n.IsRush ? "[RUSH] " : "")}New order {n.OrderNumber} — {n.CustomerName}";

        var body = new StringBuilder()
            .AppendLine($"Order: {n.OrderNumber}")
            .AppendLine($"Customer: {n.CustomerName}")
            .AppendLine($"Phone: {n.Phone}")
            .AppendLine($"Preferred date: {n.PreferredDate:yyyy-MM-dd}")
            .AppendLine();
        foreach (var item in n.Items)
        {
            body.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{item.Quantity}× {item.ProductName} — ₱{item.UnitPrice:N2}"));
        }
        body.AppendLine()
            .AppendLine(string.Create(CultureInfo.InvariantCulture, $"Total: ₱{n.Subtotal:N2}"));

        return (subject, body.ToString());
    }
}
