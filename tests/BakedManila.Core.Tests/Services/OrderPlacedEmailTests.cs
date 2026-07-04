using BakedManila.Core.Services;

namespace BakedManila.Core.Tests.Services;

public class OrderPlacedEmailTests
{
    private static OrderPlaced Notification(bool isRush = false) => new(
        "BM-2026-0042", "Maria Santos", "09171234567",
        new DateOnly(2026, 7, 10), isRush, 910m,
        [new OrderPlacedItem("Classic Chocolate Chip", 280m, 2),
         new OrderPlacedItem("Chocolate Chunk Banana Bread", 350m, 1)]);

    [Fact]
    public void Build_FormatsSubjectAndBody()
    {
        var (subject, body) = OrderPlacedEmail.Build(Notification());

        Assert.Equal("New order BM-2026-0042 — Maria Santos", subject);
        Assert.Contains("09171234567", body);
        Assert.Contains("2026-07-10", body);
        Assert.Contains("2× Classic Chocolate Chip — ₱280.00", body);
        Assert.Contains("1× Chocolate Chunk Banana Bread — ₱350.00", body);
        Assert.Contains("Total: ₱910.00", body);
    }

    [Fact]
    public void Build_PrefixesRushOrders()
    {
        var (subject, _) = OrderPlacedEmail.Build(Notification(isRush: true));
        Assert.StartsWith("[RUSH] ", subject);
    }
}
