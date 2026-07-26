namespace Pos.App.ViewModels;

/// <summary>An area filter chip in the table view (e.g. "ALL 30", "GARDEN 4").</summary>
public sealed class AreaTab
{
    public string Name { get; init; } = "";
    public string? AreaValue { get; init; }   // null = ALL
    public int Count { get; init; }
}
