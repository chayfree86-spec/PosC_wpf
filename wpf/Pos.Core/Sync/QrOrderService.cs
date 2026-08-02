using System.Globalization;
using System.Text.Json;

namespace Pos.Core.Sync;

/// <summary>One line of a customer's QR order, as the mobile menu sent it.</summary>
public sealed class QrOrderLine
{
    public long ItemId { get; set; }
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public long Quantity { get; set; }
    public string? Note { get; set; }

    public string QtyText => $"{Quantity} x";
    public string AmountText => "₹" + (Price * Quantity).ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// An order a customer placed by scanning the table's QR code, waiting for the counter to accept
/// it. Mirrors the server's <c>qr_orders</c> row.
/// </summary>
public sealed class QrOrder
{
    public long Id { get; set; }
    public long? TableId { get; set; }
    public string TableNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerMobile { get; set; } = "";
    public double TotalAmount { get; set; }
    public string Status { get; set; } = "pending";
    public string CreatedAt { get; set; } = "";
    public List<QrOrderLine> Items { get; set; } = new();

    public bool IsPending => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase);

    public string TableText => string.IsNullOrWhiteSpace(TableNumber) ? "No table" : $"Table {TableNumber}";
    public string TotalText => "₹" + TotalAmount.ToString("0.##", CultureInfo.InvariantCulture);
    public string StatusText => IsPending ? "NEW" : Status.ToUpperInvariant();

    /// <summary>Who ordered, for the card's subtitle — blank when the customer gave no details.</summary>
    public string CustomerText =>
        string.IsNullOrWhiteSpace(CustomerName) && string.IsNullOrWhiteSpace(CustomerMobile) ? ""
        : string.IsNullOrWhiteSpace(CustomerMobile) ? CustomerName
        : string.IsNullOrWhiteSpace(CustomerName) ? CustomerMobile
        : $"{CustomerName} · {CustomerMobile}";

    public string TimeText =>
        DateTime.TryParse(CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("hh:mm tt", CultureInfo.InvariantCulture)
            : "";
}

/// <summary>
/// The till's half of the QR-ordering bridge: reads the orders customers placed from the mobile
/// menu, and tells the server whether the counter took them.
///
/// Read-through only — nothing is cached in SQLite. A QR order is a request, not a sale: it
/// becomes real once the operator accepts it and it is written as a table order like any other,
/// so there is nothing here worth surviving a restart.
/// </summary>
public sealed class QrOrderService
{
    private readonly PosApiClient _api;

    public QrOrderService(PosApiClient api) => _api = api;

    /// <summary>
    /// The board: this business's pending and accepted QR orders, newest first. Returns an empty
    /// list when the server can't be reached — the screen shows its empty state rather than an
    /// error, and the next poll picks things up.
    /// </summary>
    public async Task<IReadOnlyList<QrOrder>> GetBoardAsync(CancellationToken ct = default)
    {
        try
        {
            var root = await _api.GetAsync("/qr-orders", ct);
            if (root is null)
            {
                return Array.Empty<QrOrder>();
            }

            var data = root.Value.TryGetProperty("data", out var d) ? d : root.Value;
            if (data.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<QrOrder>();
            }

            return data.EnumerateArray().Select(Parse).ToList();
        }
        catch
        {
            return Array.Empty<QrOrder>();
        }
    }

    /// <summary>
    /// Moves an order to accepted, rejected or settled on the server. Answers false when the call
    /// didn't get through, so the caller can leave the card on the board rather than pretending
    /// the kitchen was told.
    /// </summary>
    public async Task<bool> SetStatusAsync(long id, string status, CancellationToken ct = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { status });
            await _api.PatchJsonAsync($"/qr-orders/{id}/status", body, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── JSON helpers: the API answers with mixed string/number types ──────────
    private static QrOrder Parse(JsonElement e) => new()
    {
        Id = Num(e, "id"),
        TableId = NumOrNull(e, "table_id"),
        TableNumber = Str(e, "table_number"),
        CustomerName = Str(e, "customer_name"),
        CustomerMobile = Str(e, "customer_mobile"),
        TotalAmount = Dbl(e, "total_amount"),
        Status = Str(e, "status") is { Length: > 0 } s ? s : "pending",
        CreatedAt = Str(e, "created_at"),
        Items = ParseItems(e),
    };

    private static List<QrOrderLine> ParseItems(JsonElement e)
    {
        var lines = new List<QrOrderLine>();
        if (!e.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }

        foreach (var i in items.EnumerateArray())
        {
            if (i.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            lines.Add(new QrOrderLine
            {
                ItemId = Num(i, "id"),
                Name = Str(i, "name"),
                Price = Dbl(i, "price"),
                // The mobile menu always sends a quantity, but a hand-made row might not.
                Quantity = Math.Max(1, Num(i, "quantity")),
                Note = Str(i, "specialInstructions") is { Length: > 0 } n ? n : null,
            });
        }
        return lines;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    private static long? NumOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? n : (long)v.GetDouble();
        return long.TryParse(Str(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    private static long Num(JsonElement e, string name) => NumOrNull(e, name) ?? 0;

    private static double Dbl(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return double.TryParse(Str(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
    }
}
