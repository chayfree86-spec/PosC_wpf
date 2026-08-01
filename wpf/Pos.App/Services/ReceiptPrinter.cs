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
    /// Prints <paramref name="text"/> once per configured copy.
    /// Returns null on success, or an error message the caller can surface.
    /// </summary>
    public string? Print(PrintConfig cfg, string text, bool withQr = false)
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
                var doc = BuildDocument(cfg, text, withQr);
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

    private void Release()
    {
        _queue?.Dispose();
        _server?.Dispose();
        _queue = null;
        _server = null;
        _queueName = "";
    }

    public void Dispose() => Release();

    private static FlowDocument BuildDocument(PrintConfig cfg, string text, bool withQr)
    {
        double width = cfg.Is58mm ? 200 : 280;
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

        if (withQr && cfg.PrintQrOnBill && !string.IsNullOrWhiteSpace(cfg.QrImagePath) && File.Exists(cfg.QrImagePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(cfg.QrImagePath);
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
