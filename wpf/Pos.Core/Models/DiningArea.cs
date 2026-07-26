namespace Pos.Core.Models;

public sealed class DiningArea
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string Name { get; set; } = "";
    public long SortOrder { get; set; }
    public long IsActive { get; set; } = 1;

    public override string ToString() => Name;
}

/// <summary>Editable table row (raw restaurant_tables columns, not the computed view).</summary>
public sealed class TableEdit
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string TableNumber { get; set; } = "";
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
}
