using System.Collections.Concurrent;
using System.Threading;
using Pos.Core.Printing;

namespace Pos.App.Services;

/// <summary>
/// Runs every receipt through one background printer thread.
///
/// Printing on the UI thread froze the till: a thermal printer that is off, out of paper —
/// or a driver like "Print to PDF" that opens a save dialog — blocks inside the print call,
/// and the whole counter stops responding with a half-finished bill on screen. Bills are
/// saved to SQLite first and the paper follows from here, so a printer problem can never
/// hold up the next customer.
///
/// The thread is STA because WPF's printing stack requires it, and single because thermal
/// printers do not take kindly to two jobs at once.
/// </summary>
public sealed class PrintSpooler : IDisposable
{
    private readonly BlockingCollection<Job> _jobs = new();
    private readonly Thread _worker;

    /// <summary>Raised on the printer thread when a job fails — marshal before touching UI.</summary>
    public event Action<string, string>? Failed;

    public PrintSpooler()
    {
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "pos-print",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>Queues a receipt. Returns immediately — the caller never waits on paper.</summary>
    public void Enqueue(string what, PrintConfig config, string text, bool withQr = false)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _jobs.Add(new Job(what, config, text, withQr));
        }
    }

    private void Run()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                var error = new ReceiptPrinter(job.Config).Print(job.Text, job.WithQr);
                if (error is not null)
                {
                    Failed?.Invoke(job.What, error);
                }
            }
            catch (Exception ex)
            {
                Failed?.Invoke(job.What, ex.Message);
            }
        }
    }

    public void Dispose() => _jobs.CompleteAdding();

    private sealed record Job(string What, PrintConfig Config, string Text, bool WithQr);
}
