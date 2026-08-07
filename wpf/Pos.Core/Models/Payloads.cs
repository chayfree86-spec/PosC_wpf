namespace Pos.Core.Models;

/// <summary>One line item as sent from the UI when saving a KOT / bill.</summary>
public sealed class OrderItemInput
{
    public long? ItemId { get; set; }
    public string? ClientItemId { get; set; }
    public string? ItemName { get; set; }
    public double Price { get; set; }
    public long Quantity { get; set; } = 1;
    public bool IsParcel { get; set; }
    public double DiscountAmount { get; set; }
    public string? DiscountType { get; set; }
    public double DiscountValue { get; set; }
    public string? DiscountLabel { get; set; }
}

/// <summary>
/// Payload for OrderRepository.SaveTableOrder — mirrors the JS saveTableOrder
/// input. Handles KOT (status "ordered"), final bill (status "completed"/
/// "available" with items) and table clear (status "available", no items).
/// </summary>
public sealed class TableOrderPayload
{
    public long? TableId { get; set; }
    public string TableStatus { get; set; } = "ordered";
    public double? TotalAmount { get; set; }
    public double DiscountAmount { get; set; }
    public string? DiscountType { get; set; }
    public double DiscountValue { get; set; }
    public string? DiscountLabel { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public string? BillNote { get; set; }
    public long? BillNumber { get; set; }
    /// <summary>Who billed it. Left null by almost every caller — the repository fills it in
    /// from the signed-in operator. Set it only to credit someone other than whoever is at
    /// the till.</summary>
    public long? CreatedBy { get; set; }
    public bool IsParcelMode { get; set; }
    public bool MergeItems { get; set; }
    public long? OrderTimestamp { get; set; }
    public string? BilledAt { get; set; }
    public bool AssignBillNumber { get; set; }
    public List<OrderItemInput> Items { get; set; } = new();
}

public sealed class NextBillNumberResult
{
    public long NextBillNumber { get; set; }
    public long BillNumber { get; set; }
    /// <summary>Empty until a business name is known — a bill prints #0007 rather than being
    /// stamped with some other shop's letters.</summary>
    public string BillPrefix { get; set; } = "";
    public string FormattedBillNumber { get; set; } = "";
}

public sealed class SaveOrderResult
{
    public bool Success { get; set; }
    public long? Id { get; set; }
    public long? BillNumber { get; set; }
    public long? TableId { get; set; }
    public string? OrderStatus { get; set; }
    public string? TableStatus { get; set; }
    public double TotalAmount { get; set; }
    public bool Cleared { get; set; }

    /// <summary>
    /// The bill number as it should be shown and printed — <c>#CC-0007</c>.
    ///
    /// Carried on the result rather than rebuilt by each caller: the printer used to receive
    /// the bare number while Reports showed the prefixed one, so the paper handed to the
    /// customer and the row in the sales log named the same bill differently. Empty for a save
    /// that produced no bill number, such as a KOT or a cleared table.
    /// </summary>
    public string BillPrefix { get; set; } = "";
    public string FormattedBillNumber { get; set; } = "";
}
