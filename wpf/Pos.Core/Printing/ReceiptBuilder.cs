using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pos.Core.Printing;

/// <summary>One printable cart/order line.</summary>
public sealed record PrintLine(string Name, long Qty, double Price, bool IsParcel);

/// <summary>Printer + branding values pulled from Settings at print time.</summary>
public sealed class PrintConfig
{
    public string PrinterName { get; init; } = "";
    public string PaperSize { get; init; } = "80mm";
    public int Copies { get; init; } = 1;

    public string StoreName { get; init; } = "";
    public string Website { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public string GstNo { get; init; } = "";
    public string FoodLicenseNo { get; init; } = "";
    public string Address { get; init; } = "";

    public bool ShowName { get; init; } = true;
    public bool ShowWebsite { get; init; }
    public bool ShowPhone { get; init; } = true;
    public bool ShowEmail { get; init; }
    public bool ShowGst { get; init; } = true;
    public bool ShowFoodLicense { get; init; }
    public bool ShowAddress { get; init; } = true;

    public string QrImagePath { get; init; } = "";
    public bool PrintQrOnBill { get; init; }

    public bool Is58mm => PaperSize.Contains("58");
}

/// <summary>
/// Builds the monospace KOT / customer-bill text for thermal receipts.
///
/// Column layout mirrors the Electron app's receipt output so kitchen staff and
/// customers see an identical ticket after the WPF migration. Pure string work —
/// no WPF dependency — so it can be exercised from the dev harness.
/// </summary>
public sealed class ReceiptBuilder
{
    private readonly PrintConfig _cfg;

    public ReceiptBuilder(PrintConfig cfg) => _cfg = cfg;

    // 80mm ≈ 42 monospace columns, 58mm ≈ 32.
    public int Cols => _cfg.Is58mm ? 32 : 42;
    private int KotNameWidth => _cfg.Is58mm ? 26 : 36;
    private const int KotQtyWidth = 6;
    private int BillNameWidth => _cfg.Is58mm ? 16 : 24;
    private int BillQtyWidth => _cfg.Is58mm ? 5 : 6;
    private int BillAmtWidth => _cfg.Is58mm ? 11 : 12;

    private string Line => new('-', Cols);

    private static string Fit(string s, int width) =>
        (s.Length > width ? s[..width] : s).PadRight(width);

    private static string RightFit(string s, int width) =>
        (s.Length > width ? s[^width..] : s).PadLeft(width);

    private static string Centre(string s, int width) =>
        s.Length >= width ? s[..width] : s.PadLeft((width - s.Length) / 2 + s.Length).PadRight(width);

    /// <summary>Strips existing parcel tags and re-applies a centred "P#n" marker for parcel lines.</summary>
    private static string ItemNameCell(PrintLine item, int width, bool showParcelHash)
    {
        var name = item.Name ?? "";
        var hash = "";

        if (item.IsParcel)
        {
            if (showParcelHash)
            {
                var qty = item.Qty;
                var m = Regex.Match(name, @"#(\d+)");
                if (m.Success && long.TryParse(m.Groups[1].Value, out var parsed)) qty = parsed;
                hash = $"#{qty}";
            }
            name = Regex.Replace(name, @"\s*#\d*|\s*[\[\(]parcel[\]\)]", "", RegexOptions.IgnoreCase).Trim();
        }

        if (!item.IsParcel) return Fit(name, width);

        var tag = "P" + hash;
        var maxBase = width - tag.Length - 2;
        var baseName = name.Length > maxBase && maxBase > 0 ? name[..maxBase] : name;
        var remaining = width - baseName.Length;
        if (remaining >= tag.Length + 2)
        {
            var spaces = remaining - tag.Length;
            var left = spaces / 2;
            return baseName + new string(' ', left) + tag + new string(' ', spaces - left);
        }
        return Fit(baseName + " " + tag, width);
    }

    // ── KOT ──────────────────────────────────────────────────────────────────
    public string BuildKot(IReadOnlyList<PrintLine> items, string tableLabel, string? note, DateTime? now = null)
    {
        var stamp = now ?? DateTime.Now;
        var sb = new StringBuilder();
        sb.AppendLine(Centre("KOT", Cols));

        var time = stamp.ToString("HH:mm");
        var date = stamp.ToString("ddMMM", CultureInfo.InvariantCulture);

        // left = table, centre = time, right = date
        var side = (Cols - 12) / 2;
        sb.AppendLine(Fit(tableLabel, side) + Centre(time, 12) + RightFit(date, Cols - side - 12));

        sb.AppendLine(Line);
        sb.AppendLine(Fit("Item Name", KotNameWidth) + RightFit("qty", KotQtyWidth));
        sb.AppendLine(Line);

        var parcels = items.Where(i => i.IsParcel).ToList();
        var dineIn = items.Where(i => !i.IsParcel).ToList();

        if (parcels.Count > 0)
        {
            foreach (var i in dineIn) sb.AppendLine(KotRow(i, true));
            sb.AppendLine();
            sb.AppendLine(Centre("Parcel#", Cols));
            foreach (var i in parcels) sb.AppendLine(KotRow(i, false));
        }
        else
        {
            foreach (var i in items) sb.AppendLine(KotRow(i, true));
        }

        sb.AppendLine(Line);
        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.AppendLine($"Note: {note}");
            sb.AppendLine(Line);
        }
        return sb.ToString();
    }

    private string KotRow(PrintLine i, bool showHash) =>
        ItemNameCell(i, KotNameWidth, showHash) + RightFit(Math.Max(1, i.Qty).ToString(), KotQtyWidth);

    // ── Customer bill ────────────────────────────────────────────────────────
    public string BuildBill(IReadOnlyList<PrintLine> items, string billNumber, string tableNumber,
        double discount, double grandTotal, DateTime billedAt)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(billNumber)) sb.AppendLine($"Bill: {billNumber}");
        if (_cfg.ShowName && !string.IsNullOrWhiteSpace(_cfg.StoreName)) sb.AppendLine(_cfg.StoreName);
        if (_cfg.ShowWebsite && !string.IsNullOrWhiteSpace(_cfg.Website)) sb.AppendLine(_cfg.Website);
        if (_cfg.ShowPhone && !string.IsNullOrWhiteSpace(_cfg.Phone)) sb.AppendLine($"Mob: {_cfg.Phone}");
        if (_cfg.ShowEmail && !string.IsNullOrWhiteSpace(_cfg.Email)) sb.AppendLine(_cfg.Email);
        if (_cfg.ShowGst && !string.IsNullOrWhiteSpace(_cfg.GstNo)) sb.AppendLine($"Gst No. {_cfg.GstNo}");
        if (_cfg.ShowFoodLicense && !string.IsNullOrWhiteSpace(_cfg.FoodLicenseNo)) sb.AppendLine($"Foodlicense No. {_cfg.FoodLicenseNo}");
        if (_cfg.ShowAddress && !string.IsNullOrWhiteSpace(_cfg.Address)) sb.AppendLine(_cfg.Address);

        // InvariantCulture: "/" in a format string means "culture's date separator",
        // which renders as "-" on some machines. The receipt must always read dd/MM/yy.
        var dateStr = billedAt.ToString("dd/MM/yy, HH:mm", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(tableNumber))
        {
            var dateLabel = $"Date: {dateStr}";
            var tableLabel = $"Table No: {tableNumber}";
            var gap = Cols - dateLabel.Length - tableLabel.Length;
            if (gap >= 1) sb.AppendLine(dateLabel + new string(' ', gap) + tableLabel);
            else { sb.AppendLine(dateLabel); sb.AppendLine(tableLabel); }
        }
        else sb.AppendLine($"Date: {dateStr}");

        sb.AppendLine(Line);
        sb.AppendLine(Fit("Item Name", BillNameWidth) + RightFit("qty", BillQtyWidth) + RightFit("Rs.", BillAmtWidth));
        sb.AppendLine(Line);

        var parcels = items.Where(i => i.IsParcel).ToList();
        var dineIn = items.Where(i => !i.IsParcel).ToList();

        if (parcels.Count > 0)
        {
            foreach (var i in dineIn) sb.AppendLine(BillRow(i, false));
            sb.AppendLine();
            sb.AppendLine("Parcel#");
            foreach (var i in parcels) sb.AppendLine(BillRow(i, false));
        }
        else
        {
            foreach (var i in items) sb.AppendLine(BillRow(i, true));
        }

        sb.AppendLine(Line);

        var totalQty = items.Sum(i => Math.Max(1, i.Qty));
        if (discount > 0)
        {
            var subtotal = grandTotal + discount;
            sb.AppendLine(Fit("Subtotal", BillNameWidth) + RightFit(totalQty.ToString(), BillQtyWidth)
                          + RightFit(subtotal.ToString("0"), BillAmtWidth));
            sb.AppendLine(Fit("Discount", BillNameWidth) + new string(' ', BillQtyWidth)
                          + RightFit($"-{discount:0}", BillAmtWidth));
            sb.AppendLine(Line);
        }

        sb.AppendLine(Fit("Total", BillNameWidth) + RightFit(totalQty.ToString(), BillQtyWidth)
                      + RightFit($"Rs. {grandTotal:0}", BillAmtWidth));
        sb.AppendLine(Line);
        sb.AppendLine(Centre("Thankyou ! Visit Again", Cols));
        return sb.ToString();
    }

    private string BillRow(PrintLine i, bool showHash)
    {
        var qty = Math.Max(1, i.Qty);
        return ItemNameCell(i, BillNameWidth, showHash)
               + RightFit(qty.ToString(), BillQtyWidth)
               + RightFit((i.Price * qty).ToString("0"), BillAmtWidth);
    }
}
