using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pos.App.ViewModels;

/// <summary>
/// QR Orders board — incoming orders placed by customers via the table QR code.
/// In the Electron app these are polled from the POS API (/qr-orders). That live
/// feed will be wired when the background API sync (Phase 7) lands; for now the
/// board renders its empty/ready state so the screen is fully navigable.
/// </summary>
public partial class QrOrderViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRealtimeActive = true;
    [ObservableProperty] private string _statusText = "Waiting for customer QR orders…";

    public ObservableCollection<object> PendingOrders { get; } = new();
    public int PendingCount => PendingOrders.Count;
    public bool IsEmpty => PendingOrders.Count == 0;

    [RelayCommand]
    private void Refresh()
    {
        // Placeholder poll — the live /qr-orders fetch is wired with the API sync.
        StatusText = $"Refreshed {DateTime.Now:hh:mm tt} — no pending QR orders.";
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
