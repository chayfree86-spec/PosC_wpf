using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pos.Core.Models;

// POCOs mapped by Dapper. Column names are snake_case; we enable
// Dapper.DefaultTypeMap.MatchNamesWithUnderscores so they bind to these
// PascalCase properties (see DapperConfig).

public sealed class Category
{
    public long Id { get; set; }
    public string? Uuid { get; set; }
    public long ClientId { get; set; }
    public string Name { get; set; } = "";
    public string? Image { get; set; }
    public long? ParentId { get; set; }
    public long SortOrder { get; set; }
    public long IsActive { get; set; }

    // Combo boxes bound with DisplayMemberPath="Name" still fall back to this when the
    // generated item template can't be resolved for the closed selection box — without it
    // they render the raw type name instead of the category's name.
    public override string ToString() => Name;
}

public sealed class MenuItem
{
    public long Id { get; set; }
    public string? Uuid { get; set; }
    public long ClientId { get; set; }
    public long? CategoryId { get; set; }
    public long? SubCategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public double Price { get; set; }
    public string? Description { get; set; }
    public string? Image { get; set; }
    public string Type { get; set; } = "veg";
    public long IsAvailable { get; set; }
    public long IsParcel { get; set; }
    public long SortOrder { get; set; }
}

public sealed class GstRate
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string Name { get; set; } = "";
    public double Rate { get; set; }
    public long IsActive { get; set; }
}

/// <summary>Row shape returned by TableRepository.All() — restaurant_tables plus
/// the computed status/amount/timestamp from table_client_states.</summary>
public sealed class TableView : INotifyPropertyChanged
{
    private string _status = "available";
    private double _amount;

    public long Id { get; set; }
    public long ClientId { get; set; }
    public string TableNumber { get; set; } = "";
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public double Amount
    {
        get => _amount;
        set
        {
            if (_amount != value)
            {
                _amount = value;
                OnPropertyChanged();
            }
        }
    }

    public long? OrderTimestamp { get; set; }

    public string FormattedTime
    {
        get
        {
            if (OrderTimestamp == null || OrderTimestamp == 0) return "";
            try
            {
                long ts = OrderTimestamp.Value;
                var dt = ts > 10000000000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
                var elapsed = DateTime.Now - dt;
                if (elapsed.TotalMinutes < 1) return "Just now";
                if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} m ago";
                return dt.ToString("hh:mm tt");
            }
            catch
            {
                return "";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class Order
{
    public long Id { get; set; }
    public string? Uuid { get; set; }
    public long ClientId { get; set; }
    public long? TableId { get; set; }
    public string OrderStatus { get; set; } = "pending";
    public double TotalAmount { get; set; }
    public double DiscountAmount { get; set; }
    public string? DiscountType { get; set; }
    public double DiscountValue { get; set; }
    public string? DiscountLabel { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public string? BillNote { get; set; }
    public long IsKotOnly { get; set; }
    public long ReportVisible { get; set; }
    public string? BilledAt { get; set; }
    public long? BillNumber { get; set; }
    public long IsParcelMode { get; set; }
    public string? TableNumber { get; set; }
    public string? CreatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

public sealed class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long? ItemId { get; set; }
    public string? ClientItemId { get; set; }
    public string? ItemName { get; set; }
    public double Price { get; set; }
    public long Quantity { get; set; }
    public long IsParcel { get; set; }
    public double Total { get; set; }
    public double DiscountAmount { get; set; }
    public string? DiscountType { get; set; }
    public double DiscountValue { get; set; }
    public string? DiscountLabel { get; set; }
}

public sealed class Customer
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Address { get; set; }
    public double Balance { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>Positive = customer owes (Dr / udhaar), negative = advance.</summary>
    public string FormattedBalance =>
        Balance > 0 ? $"₹{Balance:N0} Dr"
        : Balance < 0 ? $"₹{System.Math.Abs(Balance):N0} Adv"
        : "₹0";

    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "No mobile added" : Phone;
    public string AddressDisplay => string.IsNullOrWhiteSpace(Address) ? "No address updated" : Address!;
}

public sealed class LedgerEntry
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public long CustomerId { get; set; }
    public string Type { get; set; } = "gave";
    public double Amount { get; set; }
    public string PaymentMode { get; set; } = "cash";
    public string? Remarks { get; set; }
    public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ── display helpers (for the transaction log) ──
    public bool IsDebit => (Type ?? "").ToLowerInvariant() is "gave" or "debit";
    public string TypeLabel => IsDebit ? "DEBIT" : "CREDIT";
    public string SignedAmountText => (IsDebit ? "+  ₹" : "−  ₹") + Amount.ToString("0.##");
    public string Description => string.IsNullOrWhiteSpace(Remarks) ? TypeLabel : Remarks!;
    public string DateShort =>
        DateTime.TryParse(CreatedAt, out var d) ? d.ToString("dd/MM/yy  HH:mm:ss") : CreatedAt;
}
