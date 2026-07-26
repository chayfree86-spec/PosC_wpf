using Dapper;

namespace Pos.Core.Data;

/// <summary>
/// One-time Dapper setup: map snake_case columns (order_status, table_number, …)
/// onto PascalCase POCO properties (OrderStatus, TableNumber, …).
/// </summary>
public static class DapperConfig
{
    private static bool _done;

    public static void Init()
    {
        if (_done)
        {
            return;
        }
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        _done = true;
    }
}
