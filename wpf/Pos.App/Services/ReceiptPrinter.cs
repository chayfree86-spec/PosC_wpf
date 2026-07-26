using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pos.Core.Printing;

namespace Pos.App.Services;

/// <summary>
/// Sends receipt text built by <see cref="ReceiptBuilder"/> to a Windows print queue.
///
/// Output goes through a WPF FlowDocument on the selected queue (the same "universal
/// driver" path Test Print already uses), so any thermal printer with a Windows driver
/// works — no vendor-specific ESC/POS byte streams required.
/// </summary>
public sealed class ReceiptPrinter
{
    private readonly PrintConfig _cfg;

    public ReceiptPrinter(PrintConfig cfg) => _cfg = cfg;

    /// <summary>
    /// Prints <paramref name="text"/> once per configured copy.
    /// Returns null on success, or an error message the caller can surface.
    /// </summary>
    public string? Print(string text, bool withQr = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_cfg.PrinterName)
                || _cfg.PrinterName.StartsWith("Default Thermal", StringComparison.OrdinalIgnoreCase))
            {
                return "Koi printer select nahi hai — Settings → Printer Settings me printer chunein.";
            }

            using var server = new LocalPrintServer();
            var queue = server.GetPrintQueue(_cfg.PrinterName);

            var copies = Math.Max(1, Math.Min(10, _cfg.Copies));
            for (var c = 0; c < copies; c++)
            {
                // A fresh document per copy keeps each spool job small — large recycled
                // FlowDocuments are what previously fragmented the LOH and stalled the spooler.
                var dlg = new PrintDialog { PrintQueue = queue };
                var doc = BuildDocument(text, withQr);
                var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
                paginator.PageSize = new Size(doc.PageWidth, doc.PageHeight);
                dlg.PrintDocument(paginator, "POS Receipt");
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private FlowDocument BuildDocument(string text, bool withQr)
    {
        double width = _cfg.Is58mm ? 200 : 280;
        var doc = new FlowDocument
        {
            PageWidth = width,
            PageHeight = 5000,
            ColumnWidth = width,
            PagePadding = new Thickness(6, 8, 6, 8),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Foreground = Brushes.Black
        };

        foreach (var raw in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            doc.Blocks.Add(new Paragraph(new Run(raw))
            {
                Margin = new Thickness(0),
                LineHeight = 12
            });
        }

        if (withQr && _cfg.PrintQrOnBill && !string.IsNullOrWhiteSpace(_cfg.QrImagePath) && File.Exists(_cfg.QrImagePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_cfg.QrImagePath);
                bmp.EndInit();
                bmp.Freeze();

                var img = new Image { Source = bmp, Width = width * 0.55, Stretch = Stretch.Uniform };
                doc.Blocks.Add(new BlockUIContainer(img) { Margin = new Thickness(0, 6, 0, 0) });
                doc.Blocks.Add(new Paragraph(new Run("Scan & Pay"))
                {
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0),
                    FontSize = 8
                });
            }
            catch { /* a bad QR file must never block the bill */ }
        }

        return doc;
    }
}
