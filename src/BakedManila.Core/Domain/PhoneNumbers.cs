namespace BakedManila.Core.Domain;

public static class PhoneNumbers
{
    /// Canonical PH mobile format is 09XXXXXXXXX; +639XXXXXXXXX is folded into it.
    public static string NormalizePh(string phone) =>
        phone.StartsWith("+63", StringComparison.Ordinal) ? "0" + phone[3..] : phone;
}
