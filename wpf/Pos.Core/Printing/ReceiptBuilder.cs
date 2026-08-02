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

    /// <summary>Payee details for the bill's UPI QR. When <see cref="UpiId"/> is set the QR is
    /// generated per bill with the amount pre-filled; <see cref="QrImagePath"/> is only the
    /// fallback for a shop that pasted a plain static code instead.</summary>
    public string UpiId { get; init; } = "";
    public string UpiName { get; init; } = "";

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

    /// <summary>
    /// Brackets a run of text the printer should render bold. These are control characters that
    /// never occur in a receipt, so they ride along through the column maths untouched and are
    /// stripped back out when the line is drawn. Consolas bold has the same advance width as
    /// regular, so emphasising the time can't nudge the columns beside it out of line.
    /// </summary>
    public const char EmphasisOn = '\u0002';
    public const char EmphasisOff = '\u0003';
    private static string Emphasise(string s) => EmphasisOn + s + EmphasisOff;

    /// <summary>
    /// A line-leading marker: draw this whole row bigger and bold. Used for the KOT's item
    /// rows, which the kitchen reads across the counter — the name and quantity have to carry.
    /// Non-printing, stripped when the line is rendered.
    /// </summary>
    public const char BigLine = '\u0001';

    /// <summary>
    /// A line-leading marker: this row is columns, tab-separated, laid out in a real grid rather
    /// than padded with spaces. Space padding only lines up in a monospace font, and the item
    /// names here are often Hindi — Devanagari has no monospace, so its glyphs are whatever width
    /// they are and the qty/amount after them drift. A grid pins each column by pixel, so the
    /// numbers line up whatever the script: first column left-aligned (the name), the rest
    /// right-aligned (qty, amount).
    /// </summary>
    public const char Columnar = '\u0004';
    private const char ColSep = '\t';

    /// <summary>A columnar row; <paramref name="big"/> also prints it in the KOT's larger bold font.</summary>
    private static string ColRow(bool big, params string[] cells) =>
        (big ? BigLine.ToString() : "") + Columnar + string.Join(ColSep, cells);

    /// <summary>The item's display name for a grid row — no padding (the grid sizes the column),
    /// just the name with a parcel marker where needed.</summary>
    private static string ColName(PrintLine item, bool showHash)
    {
        var name = item.Name ?? "";
        if (!item.IsParcel) return name;

        var hash = "";
        if (showHash)
        {
            var m = Regex.Match(name, @"#(\d+)");
            hash = m.Success ? "#" + m.Groups[1].Value : "#" + item.Qty;
        }
        name = Regex.Replace(name, @"\s*#\d*|\s*[\[\(]parcel[\]\)]", "", RegexOptions.IgnoreCase).Trim();
        return $"{name} (P{hash})";
    }

    // 80mm ≈ 42 monospace columns, 58mm ≈ 32.
    public int Cols => _cfg.Is58mm ? 32 : 42;
    private int KotNameWidth => _cfg.Is58mm ? 26 : 36;
    private const int KotQtyWidth = 6;

    // The KOT prints entirely in a bigger font, so fewer characters fit the roll — a narrower
    // name column keeps the larger rows from running past the paper. Every KOT line uses this
    // same column count so the header, the rules and the item rows share one grid and the qty
    // lines up straight down; mixing the big rows with base-font rules is what threw it off.
    private int KotBigNameWidth => _cfg.Is58mm ? 18 : 28;
    private const int KotBigQtyWidth = 4;
    private int KotCols => KotBigNameWidth + KotBigQtyWidth;   // 80mm: 32, 58mm: 22
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

    // ── Test print ─────────────────────────────────────────────────────────────
    /// <summary>
    /// The "Test Print" ticket, built here so it goes through the very same layout — width,
    /// margins, font — as a real bill. It used to be assembled separately in the Settings view,
    /// which is how it drifted: the receipts were widened and enlarged while the test print kept
    /// the old small, narrow shape, so a test no longer showed what a bill would look like.
    /// </summary>
    public string BuildTest(string printerName, DateTime now)
    {
        var sb = new StringBuilder();
        if (StoreHeading is { } heading) sb.AppendLine(Centre(heading, Cols));
        sb.AppendLine(Centre("TEST PRINT", Cols));
        sb.AppendLine(Line);
        sb.AppendLine("Printer : " + Fit(printerName, Cols - 10).TrimEnd());
        sb.AppendLine("Paper   : " + _cfg.PaperSize);
        sb.AppendLine("Copies  : " + _cfg.Copies);
        sb.AppendLine("Time    : " + now.ToString("dd-MMM-yyyy hh:mm tt", CultureInfo.InvariantCulture));
        sb.AppendLine(Line);
        sb.AppendLine(Centre("Printer working correctly.", Cols));
        sb.AppendLine(Line);
        return sb.ToString();
    }

    // ── KOT ──────────────────────────────────────────────────────────────────
    public string BuildKot(IReadOnlyList<PrintLine> items, string tableLabel, string? note, DateTime? now = null)
    {
        var stamp = now ?? DateTime.Now;
        var cols = KotCols;
        var sb = new StringBuilder();

        // The whole KOT prints in the larger font: BigLine on every row keeps one column grid, so
        // the qty runs straight down and the rules span the same width as the rows.
        var kotLine = new string('-', cols);
        sb.AppendLine(BigLine + Centre("KOT", cols));

        var time = stamp.ToString("HH:mm");
        var date = stamp.ToString("ddMMM", CultureInfo.InvariantCulture);

        // left = table, centre = time (bold — the kitchen reads the order time off this), right = date
        const int timeW = 8;
        var side = (cols - timeW) / 2;
        sb.AppendLine(BigLine + Fit(tableLabel, side) + Emphasise(Centre(time, timeW)) + RightFit(date, cols - side - timeW));

        sb.AppendLine(BigLine + kotLine);
        sb.AppendLine(ColRow(true, "Item Name", "qty"));
        sb.AppendLine(BigLine + kotLine);

        var parcels = items.Where(i => i.IsParcel).ToList();
        var dineIn = items.Where(i => !i.IsParcel).ToList();

        if (parcels.Count > 0)
        {
            foreach (var i in dineIn) sb.AppendLine(KotRow(i, true));
            sb.AppendLine();
            sb.AppendLine(BigLine + Centre("Parcel#", cols));
            foreach (var i in parcels) sb.AppendLine(KotRow(i, false));
        }
        else
        {
            foreach (var i in items) sb.AppendLine(KotRow(i, true));
        }

        sb.AppendLine(BigLine + kotLine);
        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.AppendLine(BigLine + $"Note: {note}");
            sb.AppendLine(BigLine + kotLine);
        }
        return sb.ToString();
    }

    private static string KotRow(PrintLine i, bool showHash) =>
        ColRow(true, ColName(i, showHash), Math.Max(1, i.Qty).ToString());

    // ── Bill header (shop branding) ──────────────────────────────────────────

    /// <summary>
    /// The shop's name for the top of the bill, or null when its "print" switch is off.
    /// Kept apart from <see cref="HeaderDetailLines"/> because the on-screen preview shows
    /// the name as its heading and the rest as small print.
    /// </summary>
    public string? StoreHeading =>
        _cfg.ShowName && !string.IsNullOrWhiteSpace(_cfg.StoreName) ? _cfg.StoreName : null;

    /// <summary>
    /// Everything under the name — website, phone, email, GST, food licence, address — in
    /// paper order, and only the ones switched on in Settings → Profile.
    ///
    /// The bill preview renders these same lines, so a detail toggled on or off changes both
    /// the screen and the paper from one place instead of two lists drifting apart.
    /// </summary>
    public IReadOnlyList<string> HeaderDetailLines()
    {
        var lines = new List<string>();
        void Add(bool show, string value, string prefix = "")
        {
            if (show && !string.IsNullOrWhiteSpace(value)) lines.Add(prefix + value.Trim());
        }

        Add(_cfg.ShowWebsite, _cfg.Website);
        Add(_cfg.ShowPhone, _cfg.Phone, "Mob: ");
        Add(_cfg.ShowEmail, _cfg.Email);
        Add(_cfg.ShowGst, _cfg.GstNo, "Gst No. ");
        Add(_cfg.ShowFoodLicense, _cfg.FoodLicenseNo, "Foodlicense No. ");
        Add(_cfg.ShowAddress, _cfg.Address);
        return lines;
    }

    // ── Customer bill ────────────────────────────────────────────────────────
    public string BuildBill(IReadOnlyList<PrintLine> items, string billNumber, string tableNumber,
        double discount, double grandTotal, DateTime billedAt)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(billNumber)) sb.AppendLine($"Bill: {billNumber}");
        if (StoreHeading is { } heading) sb.AppendLine(heading);
        foreach (var line in HeaderDetailLines()) sb.AppendLine(line);

        // InvariantCulture: "/" in a format string means "culture's date separator",
        // which renders as "-" on some machines. The receipt must always read dd/MM/yy.
        //
        // The time is bolded; the width maths run on the marker-free length so the emphasis
        // can't throw the "Date … Table" spacing off.
        var datePart = billedAt.ToString("dd/MM/yy, ", CultureInfo.InvariantCulture);
        var timePart = billedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        var dateVisible = $"Date: {datePart}{timePart}";
        var datePrinted = $"Date: {datePart}{Emphasise(timePart)}";
        if (!string.IsNullOrWhiteSpace(tableNumber))
        {
            var tableLabel = $"Table No: {tableNumber}";
            var gap = Cols - dateVisible.Length - tableLabel.Length;
            if (gap >= 1) sb.AppendLine(datePrinted + new string(' ', gap) + tableLabel);
            else { sb.AppendLine(datePrinted); sb.AppendLine(tableLabel); }
        }
        else sb.AppendLine(datePrinted);

        sb.AppendLine(Line);
        sb.AppendLine(ColRow(false, "Item Name", "qty", "Rs."));
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
            sb.AppendLine(ColRow(false, "Subtotal", totalQty.ToString(), subtotal.ToString("0")));
            sb.AppendLine(ColRow(false, "Discount", "", $"-{discount:0}"));
            sb.AppendLine(Line);
        }

        sb.AppendLine(ColRow(false, "Total", totalQty.ToString(), $"Rs. {grandTotal:0}"));
        sb.AppendLine(Line);
        sb.AppendLine(Centre("Thankyou ! Visit Again", Cols));
        return sb.ToString();
    }

    private static string BillRow(PrintLine i, bool showHash)
    {
        var qty = Math.Max(1, i.Qty);
        return ColRow(false, ColName(i, showHash), qty.ToString(), (i.Price * qty).ToString("0"));
    }
}
