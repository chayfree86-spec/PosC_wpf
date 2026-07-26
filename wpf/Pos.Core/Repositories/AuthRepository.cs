using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Repositories;

/// <summary>The operator who is signed in.</summary>
public sealed record AuthUser(long Id, string Name, string? Phone, string? Email, string? Role);

/// <summary>What a sign-in attempt produced.</summary>
public sealed record LoginResult(bool Ok, AuthUser? User, string? Error);

/// <summary>
/// Signs an operator in against the till's own copy of the staff list — mobile number plus PIN.
///
/// This is deliberately local. The counter has to open when the line is down, which is exactly
/// when a network round trip would leave someone standing at the till unable to start the day.
/// The PIN is checked against the bcrypt hash the server sends with the staff list; the PIN
/// itself is never stored anywhere on this machine.
/// </summary>
public sealed class AuthRepository
{
    private readonly DatabaseService _db;

    public AuthRepository(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    /// <summary>True once the staff list has been pulled at least once — before that there is
    /// nobody to check a PIN against and sign-in has to wait for the first sync.</summary>
    public bool HasUsers(long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM users WHERE client_id = @clientId AND is_active = 1", new { clientId }) > 0;
    }

    public LoginResult Login(string mobile, string pin, long clientId = 1)
    {
        var digits = Digits(mobile);
        if (digits.Length < 10)
        {
            return new LoginResult(false, null, "Mobile number pura daalein.");
        }
        if (string.IsNullOrWhiteSpace(pin))
        {
            return new LoginResult(false, null, "PIN daalein.");
        }

        using var conn = _db.OpenConnection();

        // Matched on the last ten digits, the same way the server does it, so a number saved
        // as "+91 96287 17175" still signs in when it's typed as "9628717175".
        var row = conn.QueryFirstOrDefault<UserRow>(
            @"SELECT id, name, phone, email, role, pin, is_active
              FROM users
              WHERE client_id = @clientId
                AND is_active = 1
                AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(IFNULL(phone, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
                    LIKE '%' || @lastTen
              ORDER BY id
              LIMIT 1",
            new { clientId, lastTen = digits[^10..] });

        if (row is null)
        {
            return new LoginResult(false, null, "Is number se koi user nahi mila.");
        }

        if (string.IsNullOrWhiteSpace(row.Pin))
        {
            // The staff list arrived before PINs were being sent down, or this user has none set.
            return new LoginResult(false, null, "Is user ka PIN set nahi hai — pehle sync karein.");
        }

        bool ok;
        try
        {
            ok = BCrypt.Net.BCrypt.Verify(pin, row.Pin);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A stored value that isn't a bcrypt hash at all — refuse rather than guess.
            return new LoginResult(false, null, "PIN verify nahi ho paya — sync karke dobara try karein.");
        }

        return ok
            ? new LoginResult(true, new AuthUser(row.Id, row.Name, row.Phone, row.Email, row.Role), null)
            : new LoginResult(false, null, "PIN galat hai.");
    }

    private static string Digits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private sealed class UserRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string? Pin { get; set; }
        public long IsActive { get; set; }
    }
}
