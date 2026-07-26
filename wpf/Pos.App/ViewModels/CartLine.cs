using CommunityToolkit.Mvvm.ComponentModel;

namespace Pos.App.ViewModels;

public partial class CartLine : ObservableObject
{
    public long? ItemId { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty]
    private bool _isParcel;

    [ObservableProperty]
    private double _price;

    [ObservableProperty]
    private string _priceText = "";

    [ObservableProperty]
    private long _qty = 1;

    [ObservableProperty]
    private string _qtyText = "1";

    [ObservableProperty]
    private bool _isSaved;

    public double LineTotal => Price * Math.Max(1, Qty);

    private bool _isUpdatingQtyFromText;
    private bool _isUpdatingPriceFromText;

    partial void OnPriceChanged(double value)
    {
        if (!_isUpdatingPriceFromText)
        {
            _priceText = value.ToString("0.##");
            OnPropertyChanged(nameof(PriceText));
        }
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnPriceTextChanged(string value)
    {
        if (double.TryParse(value, out double parsed))
        {
            _isUpdatingPriceFromText = true;
            try
            {
                Price = parsed;
            }
            finally
            {
                _isUpdatingPriceFromText = false;
            }
        }
    }

    partial void OnQtyChanged(long value)
    {
        if (!_isUpdatingQtyFromText)
        {
            _qtyText = value.ToString();
            OnPropertyChanged(nameof(QtyText));
        }
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnQtyTextChanged(string value)
    {
        if (long.TryParse(value, out long parsed))
        {
            if (parsed > 999) parsed = 999;
            _isUpdatingQtyFromText = true;
            try
            {
                if (parsed > 0)
                {
                    Qty = parsed;
                }
            }
            finally
            {
                _isUpdatingQtyFromText = false;
            }
        }
    }
}
