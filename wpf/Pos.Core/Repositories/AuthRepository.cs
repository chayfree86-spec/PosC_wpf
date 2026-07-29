using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Repositories;

/// <summary>
/// The operator who is signed in, and the business they bill for.
///
/// The two travel together because they are decided together: one counter serves Daal Roti and
/// Chay Chaupal, and which brand the next bill belongs to is settled by who just signed in.
/// </summary>
public sealed record AuthUser(
    long Id, string Name, string? Phone, string? Email, string? Role,
    long ClientId, string ClientSlug, string ClientName);

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

    /// <summary>
    /// True once the staff list has been pulled at least once — before that there is nobody to
    /// check a PIN against and sign-in has to wait for the first sync.
    ///
    /// Counts across every business on this till, not one: the counter is unlocked as soon as
    /// there is someone who can unlock it, whichever brand they belong to.
    /// </summary>
    public bool HasUsers()
    {
        using var conn = _db.OpenConnection();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM users WHERE is_active = 1") > 0;
    }

    /// <summary>
    /// A business signs in under the contact number on its own profile.
    ///
    /// The counter is shared, and each brand is one account: whatever number is showing in
    /// Settings → Profile is the number its manager types here. Keeping the two in step by
    /// COPYING one into the other would need a way to push a changed phone back to the server —
    /// there isn't one, and the next sync would undo it — so sign-in reads the profile directly
    /// instead. Change the contact number, and the login name changes with it.
    /// </summary>
    private const string MatchByProfileContact =
        @"u.client_id IN (
              SELECT cs.client_id FROM client_settings cs
              WHERE cs.key = 'restaurant_profile'
                AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                      IFNULL(json_extract(cs.value_json, '$.contactNumber'), ''),
                      ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
                    LIKE '%' || @lastTen
          )";

    /// <summary>
    /// Who a typed number belongs to: the staff row's own mobile, or the contact number on a
    /// business's profile. Matched on the last ten digits, the same way the server does it, so
    /// "+91 96287 17175" still signs in when typed as "9628717175".
    ///
    /// Shared by <see cref="Login"/> and <see cref="LookupName"/> so the two cannot drift — a
    /// lookup even slightly more forgiving than the sign-in would name one operator on screen
    /// and then check the PIN against another.
    ///
    /// Not scoped to a client: the operator types a number, not a brand, and that number is
    /// what decides which business the till serves for this shift.
    /// </summary>
    private const string MatchByLastTen =
        @"u.is_active = 1
          AND (
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(IFNULL(u.phone, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
                    LIKE '%' || @lastTen
             OR " + MatchByProfileContact + @"
          )";

    /// <summary>
    /// Which account answers when more than one could.
    ///
    /// The staff row's own number wins over a business's contact number, so a waiter whose
    /// mobile happens to be the shop's does not get signed in as the manager. Among accounts
    /// reached through the profile the manager is taken, since that is the business's own login.
    /// </summary>
    private const string PreferOwnNumberThenManager =
        @"ORDER BY
            CASE WHEN REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(IFNULL(u.phone, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
                      LIKE '%' || @lastTen THEN 0 ELSE 1 END,
            CASE WHEN LOWER(IFNULL(u.role, '')) = 'manager' THEN 0 ELSE 1 END,
            u.id";

    /// <summary>
    /// The staff name behind a mobile number, or null when it is not on this till's list.
    ///
    /// Read while the operator is still typing, so the login screen can confirm whose account
    /// is about to be opened before a PIN is entered — mistyping a digit is otherwise only
    /// discovered after the PIN has been keyed in and rejected.
    /// </summary>
    public StaffLookup? LookupName(string mobile)
    {
        var digits = Digits(mobile);
        if (digits.Length < 10)
        {
            return null;
        }

        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<StaffLookup>(
            $@"SELECT u.name, IFNULL(c.name, '') AS client_name
               FROM users u
               LEFT JOIN clients c ON c.id = u.client_id
               WHERE {MatchByLastTen}
               {PreferOwnNumberThenManager}
               LIMIT 1",
            new { lastTen = digits[^10..] });
    }

    public LoginResult Login(string mobile, string pin)
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

        var row = conn.QueryFirstOrDefault<UserRow>(
            $@"SELECT u.id, u.name, u.phone, u.email, u.role, u.pin, u.is_active,
                      u.client_id, IFNULL(c.slug, '') AS client_slug, IFNULL(c.name, '') AS client_name
               FROM users u
               LEFT JOIN clients c ON c.id = u.client_id
               WHERE {MatchByLastTen}
               {PreferOwnNumberThenManager}
               LIMIT 1",
            new { lastTen = digits[^10..] });

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

        if (!ok)
        {
            return new LoginResult(false, null, "PIN galat hai.");
        }

        if (row.ClientId <= 0)
        {
            // A staff row with no business behind it would bill into nowhere — better to say so
            // than to quietly file the shift's takings under client 1.
            return new LoginResult(false, null, "Is user ka business set nahi hai — pehle sync karein.");
        }

        return new LoginResult(true, new AuthUser(
            row.Id, row.Name, row.Phone, row.Email, row.Role,
            row.ClientId, row.ClientSlug ?? "", row.ClientName ?? ""), null);
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
        public long ClientId { get; set; }
        public string? ClientSlug { get; set; }
        public string? ClientName { get; set; }
    }
}

/// <summary>What the login screen shows while a number is being typed: whose account it is,
/// and which business they will be billing for.</summary>
public sealed class StaffLookup
{
    public string Name { get; set; } = "";
    public string ClientName { get; set; } = "";
}
