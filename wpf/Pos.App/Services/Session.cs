using Pos.Core.Repositories;

namespace Pos.App.Services;

/// <summary>
/// Who is standing at the till right now.
///
/// Deliberately a plain static rather than a DI singleton: the sign-in window runs before the
/// main window exists and the logout path runs after it has been hidden, so there is no view
/// model alive across both ends to hang this off. Nothing here is persisted — closing the app
/// signs the operator out, which is the behaviour a shared counter wants.
/// </summary>
public static class Session
{
    public static AuthUser? User { get; private set; }

    /// <summary>True once someone has signed in. False on a till whose staff list has never
    /// synced, where sign-in is skipped rather than blocking the counter.</summary>
    public static bool IsSignedIn => User is not null;

    public static string DisplayName =>
        User?.Name is { Length: > 0 } name ? name : "Operator";

    /// <summary>The business this shift is billing for — Daal Roti or Chay Chaupal.</summary>
    public static string BusinessName =>
        User?.ClientName is { Length: > 0 } name ? name : "";

    public static void SignIn(AuthUser user) => User = user;

    public static void SignOut() => User = null;
}
