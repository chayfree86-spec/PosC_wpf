using System;
using System.Collections.Generic;

namespace Pos.Core.Models;

public sealed class QuickNote
{
    public long Id { get; set; }
    public long ClientId { get; set; } = 1;
    public string CustomerName { get; set; } = "";
    public string CustomerMobile { get; set; } = "";
    public string SavedTime { get; set; } = "";
    public string TargetTime { get; set; } = "";
    public int TotalQty { get; set; }
    public double GrandTotal { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<QuickNoteItem> ItemsList
    {
        get
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<QuickNoteItem>>(ItemsJson) ?? new();
            }
            catch
            {
                return new();
            }
        }
    }
}

public sealed class QuickNoteItem
{
    public long ItemId { get; set; }
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public int Qty { get; set; }
    public bool IsParcel { get; set; }
    public double LineTotal => Price * Qty;
}
