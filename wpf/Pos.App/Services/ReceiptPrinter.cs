using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using Pos.Core.Printing;

namespace Pos.App.Services;

/// <summary>
/// Sends receipt text built by <see cref="ReceiptBuilder"/> to a Windows print queue.
///
/// Output goes through a WPF FlowDocument on the selected queue (the same "universal
/// driver" path Test Print already uses), so any thermal printer with a Windows driver
/// works — no vendor-specific ESC/POS byte streams required.
///
/// One instance lives for the whole session on the spooler thread and holds the resolved
/// print queue open. Opening a LocalPrintServer and looking the queue up again costs about
/// 100ms, and it was being paid on every single bill.
/// </summary>
public sealed class ReceiptPrinter : IDisposable
{
    private LocalPrintServer? _server;
    private PrintQueue? _queue;
    private string _queueName = "";

    /// <summary>
    /// QR images decoded once and kept, keyed by file path.
    ///
    /// The bill's UPI QR was read and decoded from disk on every single receipt. It rarely
    /// changes — one image for the shop — so it is decoded on the first bill (or at warmup) and
    /// the frozen bitmap reused after. Frozen so it is safe to share and cheap to draw. The map
    /// is only ever touched from the one printer thread, so it needs no locking.
    /// </summary>
    private readonly Dictionary<string, BitmapImage> _qrCache = new();

    /// <summary>
    /// Prints <paramref name="text"/> once per configured copy.
    /// Returns null on success, or an error message the caller can surface.
    /// </summary>
    public string? Print(PrintConfig cfg, string text, bool withQr = false, double qrAmount = 0)
    {
        try
        {
            var queue = Resolve(cfg);
            if (queue is null)
            {
                return "Koi printer select nahi hai — Settings → Printer Settings me printer chunein.";
            }

            var copies = Math.Max(1, Math.Min(10, cfg.Copies));
            for (var c = 0; c < copies; c++)
            {
                // A fresh document per copy keeps each spool job small — large recycled
                // FlowDocuments are what previously fragmented the LOH and stalled the spooler.
                var dlg = new PrintDialog { PrintQueue = queue };
                var doc = BuildDocument(cfg, text, withQr, qrAmount);
                if (MediaTicket(queue, cfg, doc) is { } ticket)
                {
                    dlg.PrintTicket = ticket;
                }
                var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
                paginator.PageSize = new Size(doc.PageWidth, doc.PageHeight);
                dlg.PrintDocument(paginator, "POS Receipt");
            }
            return null;
        }
        catch (Exception ex)
        {
            // A queue can go stale (printer unplugged, driver reinstalled). Drop it so the
            // next bill looks it up fresh instead of failing for the rest of the session.
            Release();
            return ex.Message;
        }
    }

    /// <summary>
    /// Pays the print stack's one-off startup cost before any customer is waiting.
    ///
    /// The first receipt of a session took ~600ms longer than every later one: WPF loads and
    /// JITs its XPS serialisation path on first use. Doing that here — at app start, against
    /// a throwaway file, with no printer involved — means the first real bill is as quick as
    /// the hundredth. Best-effort by design: a failure here must never surface to the till.
    /// </summary>
    public void Warmup(PrintConfig cfg)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"pos_warmup_{Guid.NewGuid():N}.xps");
        try
        {
            Resolve(cfg);

            // Warm the QR path too, so the first real bill doesn't pay for it: JIT the generator
            // for a UPI shop, or decode the static image for one that uploaded a code.
            if (!string.IsNullOrWhiteSpace(cfg.UpiId)) _ = UpiQr.PngForBill(cfg.UpiId, cfg.UpiName, 1);
            else if (!string.IsNullOrWhiteSpace(cfg.QrImagePath)) LoadQr(cfg.QrImagePath);

            var doc = BuildDocument(cfg, "warmup", withQr: false);
            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(doc.PageWidth, doc.PageHeight);

            var xps = new XpsDocument(scratch, FileAccess.Write);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(paginator);
            xps.Close();
        }
        catch { /* a cold first bill is better than a crash on startup */ }
        finally
        {
            try { File.Delete(scratch); } catch { /* temp file, nothing depends on it */ }
        }
    }

    /// <summary>Returns the cached queue, reopening it only when the chosen printer changes.</summary>
    private PrintQueue? Resolve(PrintConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.PrinterName))
        {
            return null;
        }

        if (_queue is not null && _queueName == cfg.PrinterName) return _queue;

        Release();
        _server = new LocalPrintServer();
        _queue = _server.GetPrintQueue(cfg.PrinterName);
        _queueName = cfg.PrinterName;
        return _queue;
    }

    /// <summary>
    /// A print ticket that pins the paper to the width chosen in Printer Settings, or null to
    /// leave the driver's own default in place.
    ///
    /// Without this the receipt is placed on whatever media the driver defaults to — on most
    /// thermal printers that is 58mm out of the box — so an 80mm bill came out shrunk to 58mm
    /// however the app formatted it.
    ///
    /// Two ways to say it, because thermal drivers vary. If the driver publishes its media
    /// sizes, the one closest in width to the selected paper is chosen — that carries the
    /// driver's own tested height. Many budget receipt drivers (the NGX/POS-80 family here among
    /// them) publish none at all; for those we send an explicit custom size, its height set to
    /// the receipt's own so the page isn't padded out to a long blank feed. Either way this is
    /// best-effort: a driver that rejects the ticket keeps its default and the bill still prints.
    /// </summary>
    private static PrintTicket? MediaTicket(PrintQueue queue, PrintConfig cfg, FlowDocument doc)
    {
        try
        {
            // mm → device-independent units (1/96 inch), the unit PageMediaSize uses.
            var paperWidth = (cfg.Is58mm ? 58.0 : 80.0) / 25.4 * 96.0;
            var ticket = queue.DefaultPrintTicket ?? new PrintTicket();

            // Leave a printer that already prints at least this wide alone — an 80mm unit on an
            // 80mm bill (its media is a continuous roll that trims to the receipt). Overriding it
            // would only risk a worse fit; the fix is for drivers that default NARROWER than the
            // chosen paper (the 72mm default on the POS-80C / TD80 units here) and would otherwise
            // shrink the receipt to their own width.
            if (ticket.PageMediaSize?.Width is double driverWidth && driverWidth >= paperWidth - 4)
            {
                return null;
            }

            ticket.PageMediaSize = new PageMediaSize(paperWidth, doc.PageHeight);
            ticket.PageOrientation = PageOrientation.Portrait;
            return ticket;
        }
        catch
        {
            return null;
        }
    }

    private void Release()
    {
        _queue?.Dispose();
        _server?.Dispose();
        _queue = null;
        _server = null;
        _queueName = "";
    }

    public void Dispose() => Release();

    private const double PadTop = 8;
    private const double PadBottom = 8;

    private const double SidePad = 8;

    /// <summary>
    /// The QR to print on this bill: generated fresh from the shop's UPI id and the bill amount
    /// so the customer's app opens with the total filled in, or — for a shop that pasted a plain
    /// static code instead — the uploaded image. Null when neither is configured.
    /// </summary>
    private BitmapImage? BillQr(PrintConfig cfg, double amount)
    {
        if (!string.IsNullOrWhiteSpace(cfg.UpiId))
        {
            var png = UpiQr.PngForBill(cfg.UpiId, cfg.UpiName, amount);
            return png is null ? null : FromBytes(png);
        }
        if (!string.IsNullOrWhiteSpace(cfg.QrImagePath))
        {
            return LoadQr(cfg.QrImagePath);
        }
        return null;
    }

    /// <summary>Decodes PNG bytes to a frozen bitmap. Fresh per bill — the amount changes, so
    /// there is nothing to cache.</summary>
    private static BitmapImage? FromBytes(byte[] png)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(png);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The QR bitmap for this path, decoded and frozen on first use, then reused.</summary>
    private BitmapImage? LoadQr(string path)
    {
        if (_qrCache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // decode now, then the file can be let go
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _qrCache[path] = bmp;
            return bmp;
        }
        catch
        {
            return null;   // a bad QR file must never block the bill
        }
    }

    private FlowDocument BuildDocument(PrintConfig cfg, string text, bool withQr, double qrAmount = 0)
    {
        // The page is sized to the width every 80mm thermal here can actually print — 72mm, not
        // the 80.1mm one of them over-reports — with an equal margin each side so nothing runs
        // off the right. The font is the largest that still fits the fixed column count inside
        // it: 42 cols at 11pt ≈ 254 units within the ~256-unit content width; 32 cols at 9pt on
        // the narrower 58mm roll.
        var (width, fontSize, lineHeight) = cfg.Is58mm ? (180.0, 9.0, 12.0) : (272.0, 11.0, 14.0);
        var doc = new FlowDocument
        {
            PageWidth = width,
            ColumnWidth = width,
            PagePadding = new Thickness(SidePad, PadTop, SidePad, PadBottom),
            FontFamily = new FontFamily("Consolas"),
            FontSize = fontSize,
            Foreground = Brushes.Black
        };

        // Fixed pixel widths for the qty and amount columns of grid rows, so those numbers line
        // up down the page no matter how wide the (often Hindi) item name beside them renders.
        var contentWidth = width - 2 * SidePad;
        var (qtyWidth, amtWidth) = cfg.Is58mm ? (34.0, 56.0) : (46.0, 80.0);

        var height = PadTop + PadBottom;
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        foreach (var raw in lines)
        {
            var (block, lineH) = BuildLine(raw, fontSize, lineHeight, contentWidth, qtyWidth, amtWidth);
            doc.Blocks.Add(block);
            height += lineH;
        }

        if (withQr && cfg.PrintQrOnBill && BillQr(cfg, qrAmount) is { } bmp)
        {
            var qrWidth = width * 0.55;
            var img = new Image { Source = bmp, Width = qrWidth, Stretch = Stretch.Uniform };
            doc.Blocks.Add(new BlockUIContainer(img) { Margin = new Thickness(0, 6, 0, 0) });
            doc.Blocks.Add(new Paragraph(new Run("Scan & Pay"))
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0),
                FontSize = 8
            });

            // QR is roughly square (stretch uniform); allow for the image, its top margin
            // and the "Scan & Pay" line.
            height += 6 + qrWidth + 14;
        }

        // Slack so a hair of rounding in the line/QR estimate can't push the last row onto a
        // second page (which on a continuous roll would look like a mid-receipt cut).
        doc.PageHeight = height + 40;
        return doc;
    }

    /// <summary>How much the KOT's big rows step up from the base font.</summary>
    private const double BigFontStep = 3;

    /// <summary>
    /// One receipt line and the vertical space it takes.
    ///
    /// A line that leads with the big-line marker is drawn a few points larger and wholly bold —
    /// the KOT the kitchen reads. A line that leads with the columnar marker is a name/qty(/amount)
    /// row laid out in a real grid so the numbers stay in line whatever width the item name
    /// renders. Everything else is a plain paragraph, with any run bracketed by the emphasis
    /// markers bold (the time). The markers never print.
    /// </summary>
    private static (Block Block, double Height) BuildLine(string raw, double baseFont, double baseLineHeight,
        double contentWidth, double qtyWidth, double amtWidth)
    {
        var big = raw.Length > 0 && raw[0] == ReceiptBuilder.BigLine;
        if (big)
        {
            raw = raw[1..];
        }

        var fontSize = big ? baseFont + BigFontStep : baseFont;
        var lineHeight = big ? baseLineHeight + BigFontStep + 1 : baseLineHeight;

        if (raw.Length > 0 && raw[0] == ReceiptBuilder.Columnar)
        {
            return (ColumnRow(raw[1..].Split('\t'), big, fontSize, lineHeight, contentWidth, qtyWidth, amtWidth), lineHeight);
        }

        var para = new Paragraph { Margin = new Thickness(0), LineHeight = lineHeight, FontSize = fontSize };
        var segment = new System.Text.StringBuilder();
        var bold = big;   // a big row is bold throughout

        void Flush()
        {
            if (segment.Length == 0) return;
            para.Inlines.Add(new Run(segment.ToString())
            {
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
            });
            segment.Clear();
        }

        foreach (var ch in raw)
        {
            if (ch == ReceiptBuilder.EmphasisOn) { Flush(); bold = true; }
            else if (ch == ReceiptBuilder.EmphasisOff) { Flush(); bold = big; }
            else segment.Append(ch);
        }
        Flush();

        if (para.Inlines.Count == 0) para.Inlines.Add(new Run(string.Empty));
        return (para, lineHeight);
    }

    /// <summary>
    /// A name/qty(/amount) row as a fixed grid: the name column takes the remaining width
    /// (left-aligned, trimmed if long), the qty and amount columns are fixed and right-aligned.
    /// This is what keeps the numbers in a straight line even when the name is Devanagari, whose
    /// glyphs are not monospace and so can't be aligned by padding with spaces.
    /// </summary>
    private static Block ColumnRow(string[] parts, bool big, double fontSize, double lineHeight,
        double contentWidth, double qtyWidth, double amtWidth)
    {
        var grid = new Grid { Width = contentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(qtyWidth) });
        if (parts.Length >= 3)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(amtWidth) });
        }

        for (var i = 0; i < parts.Length && i < grid.ColumnDefinitions.Count; i++)
        {
            var cell = new TextBlock
            {
                Text = parts[i],
                FontFamily = new FontFamily("Consolas"),
                FontSize = fontSize,
                FontWeight = big ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Brushes.Black,
                TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right,
                TextTrimming = i == 0 ? TextTrimming.CharacterEllipsis : TextTrimming.None,
                Margin = i == 0 ? new Thickness(0, 0, 4, 0) : new Thickness(0)
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return new BlockUIContainer(grid) { Margin = new Thickness(0) };
    }
}
