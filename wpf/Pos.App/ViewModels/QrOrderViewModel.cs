using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Models;
using Pos.Core.Repositories;
using Pos.Core.Sync;

namespace Pos.App.ViewModels;

/// <summary>
/// The QR Orders board — what customers ordered by scanning the table's QR code in the mobile
/// menu, waiting for the counter to take them.
///
/// Polled rather than pushed: the API has no socket, and a till that asks every few seconds is
/// simpler than one holding a connection open. Accepting an order writes it to the table as an
/// ordinary running order, which is the point of the whole bridge — from there it is billed,
/// KOT'd and reported like anything else rung at the counter.
/// </summary>
public partial class QrOrderViewModel : ObservableObject
{
    /// <summary>How often the board asks the server. Short enough that a customer isn't left
    /// waiting, long enough not to hammer the API.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly SyncCoordinator _sync;
    private readonly OrderRepository _orders;
    private readonly DispatcherTimer _timer;
    private bool _busy;

    [ObservableProperty] private bool _isRealtimeActive = true;
    [ObservableProperty] private string _statusText = "Waiting for customer QR orders…";

    public ObservableCollection<QrOrder> PendingOrders { get; } = new();
    public int PendingCount => PendingOrders.Count;
    public bool IsEmpty => PendingOrders.Count == 0;

    public QrOrderViewModel(SyncCoordinator sync, OrderRepository orders)
    {
        _sync = sync;
        _orders = orders;

        // On the UI dispatcher, so the collection this timer refreshes is touched on the thread
        // that renders it.
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    /// <summary>Raised after an accepted order is written to a table, so the Orders screen can
    /// reload and show it.</summary>
    public event Action? OrderAccepted;

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        try
        {
            var board = await new QrOrderService(_sync.CreateApiClient()).GetBoardAsync();

            PendingOrders.Clear();
            foreach (var o in board)
            {
                PendingOrders.Add(o);
            }

            IsRealtimeActive = true;
            StatusText = PendingOrders.Count == 0
                ? $"No pending orders · checked {DateTime.Now:hh:mm tt}"
                : $"{PendingOrders.Count} waiting · updated {DateTime.Now:hh:mm tt}";
            Notify();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Takes the order: writes its items onto the table as a running order, then tells the server
    /// it was accepted.
    ///
    /// The table order is written FIRST. If the status call then fails the card simply stays on
    /// the board and can be accepted again — merged into the same table order — which is far
    /// better than the reverse, where the customer would be told "accepted" for an order the
    /// counter never actually received.
    /// </summary>
    [RelayCommand]
    private async Task Accept(QrOrder? order)
    {
        if (order is null)
        {
            return;
        }

        if (order.TableId is not { } tableId || tableId <= 0)
        {
            StatusText = "Is order par koi table nahi hai — customer ne table QR se order nahi kiya.";
            return;
        }

        var payload = new TableOrderPayload
        {
            TableId = tableId,
            TableStatus = "ordered",
            CustomerName = order.CustomerName,
            CustomerMobile = order.CustomerMobile,
            // Merge, not replace: the table may already have items rung at the counter, and a QR
            // order is an addition to that sitting — replacing would wipe what is already there.
            MergeItems = true,
        };
        foreach (var i in order.Items)
        {
            payload.Items.Add(new OrderItemInput
            {
                ItemId = i.ItemId > 0 ? i.ItemId : null,
                ItemName = i.Name,
                Price = i.Price,
                Quantity = Math.Max(1, i.Quantity),
            });
        }

        try
        {
            _orders.SaveTableOrder(payload);
        }
        catch (Exception ex)
        {
            StatusText = "Table par order daalne me dikkat: " + ex.Message;
            return;
        }

        await new QrOrderService(_sync.CreateApiClient()).SetStatusAsync(order.Id, "accepted");
        OrderAccepted?.Invoke();
        StatusText = $"Accepted — {order.TableText} par order daal diya.";
        await RefreshAsync();
    }

    /// <summary>Turns the order down. Nothing is written locally — it was never a sale.</summary>
    [RelayCommand]
    private async Task Reject(QrOrder? order)
    {
        if (order is null)
        {
            return;
        }

        if (await new QrOrderService(_sync.CreateApiClient()).SetStatusAsync(order.Id, "rejected"))
        {
            StatusText = $"Rejected — {order.TableText}.";
            await RefreshAsync();
        }
        else
        {
            StatusText = "Reject nahi ho paya — server offline lag raha hai.";
        }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
