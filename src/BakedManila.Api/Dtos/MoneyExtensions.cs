namespace BakedManila.Api.Dtos;

public static class MoneyExtensions
{
    public static int ToCentavos(this decimal amount) => (int)Math.Round(amount * 100);
}
