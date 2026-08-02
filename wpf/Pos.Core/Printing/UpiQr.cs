using System.Globalization;
using QRCoder;

namespace Pos.Core.Printing;

/// <summary>
/// Builds the UPI QR the customer scans to pay.
///
/// The point is the amount: a UPI link that carries <c>am</c> makes the payment app open with
/// the bill total already filled in, so the customer only confirms — no typing the figure, no
/// paying the wrong amount. The QR is therefore made fresh for each bill from the shop's UPI id
/// and that bill's total, not uploaded once and reused.
/// </summary>
public static class UpiQr
{
    /// <summary>
    /// The <c>upi://pay</c> link a QR for this bill encodes. <c>pa</c> is the payee VPA, <c>pn</c>
    /// the payee name, <c>am</c> the amount (omitted when zero, which turns it back into a plain
    /// "pay this shop any amount" code), <c>cu</c> the currency.
    /// </summary>
    public static string BuildUri(string upiId, string? name, double amount)
    {
        // The VPA goes in raw — that is how every UPI QR writes it (pa=name@bank). Escaping the
        // '@' to %40, as a generic URL encoder does, is what some apps then fail to read back.
        var uri = "upi://pay?pa=" + upiId.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            uri += "&pn=" + Uri.EscapeDataString(name.Trim());
        }
        if (amount > 0)
        {
            // Invariant so the decimal is always a dot — a comma here makes some apps read the
            // amount as a whole number ten or a hundred times too big.
            uri += "&am=" + amount.ToString("0.00", CultureInfo.InvariantCulture);
        }
        uri += "&cu=INR";
        return uri;
    }

    /// <summary>
    /// The QR for a bill as PNG bytes, or null when there is no UPI id to encode. Pure managed
    /// (<see cref="PngByteQRCode"/>), so it needs no System.Drawing at runtime.
    /// </summary>
    public static byte[]? PngForBill(string? upiId, string? name, double amount, int pixelsPerModule = 12)
    {
        if (string.IsNullOrWhiteSpace(upiId))
        {
            return null;
        }
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(BuildUri(upiId, name, amount), QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
