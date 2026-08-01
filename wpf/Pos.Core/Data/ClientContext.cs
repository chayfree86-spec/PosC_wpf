namespace Pos.Core.Data;

/// <summary>
/// Which business this till is billing for right now.
///
/// One counter serves more than one brand — Daal Roti and Chay Chaupal share the same tables
/// and the same menu, but each keeps its own bills, its own bill-number series, its own staff
/// and its own printed header. Which one is live follows from who signed in, so this is set
/// once at login and read by every repository afterwards.
///
/// It exists as a single object rather than a parameter threaded through each call because a
/// till writes orders, table states, customers and settings from dozens of places; one missed
/// argument would file a Chay Chaupal bill under Daal Roti, and nothing about the screen would
/// look wrong until the day's totals were counted.
/// </summary>
public sealed class ClientContext
{
    /// <summary>Falls back to 1 so a till whose staff list has never synced — and therefore
    /// signs nobody in — still reads and writes the client it has always had.</summary>
    public long ClientId { get; private set; } = 1;

    /// <summary>The server's identifier for this business. It no longer decides the bill
    /// prefix — that is abbreviated from the NAME and kept in clients.bill_prefix, so adding a
    /// third shop doesn't mean adding another slug to a list somewhere.</summary>
    public string Slug { get; private set; } = "";

    public string Name { get; private set; } = "";

    /// <summary>Raised after a different business takes over the till, so anything holding
    /// loaded data can throw it away rather than show the previous brand's.</summary>
    public event Action? Changed;

    public void Use(long clientId, string? slug, string? name)
    {
        if (clientId <= 0)
        {
            return;
        }

        ClientId = clientId;
        Slug = slug ?? "";
        Name = name ?? "";
        Changed?.Invoke();
    }
}
