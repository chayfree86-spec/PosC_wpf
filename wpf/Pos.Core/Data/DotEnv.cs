namespace Pos.Core.Data;

/// <summary>
/// Deployment settings read from a plain <c>.env</c> file, in the same <c>KEY=value</c> format the
/// PHP side uses.
///
/// This is how a till is pointed at its server without rebuilding the app: drop a <c>.env</c>
/// beside the executable, set <c>POS_API_URL</c>, done. The alternative — baking the address into
/// the binary — meant a different build for the shop's live server than for a development
/// machine, and nothing to change on site when the address moved.
///
/// Two locations are searched, in order:
/// <list type="number">
/// <item>next to the executable — the file an installer or deployment writes;</item>
/// <item><c>Documents\ChayChaupalPOS\.env</c> — the app's own data folder, which survives a
/// rebuild (the bin folder does not) and so is the convenient place during development.</item>
/// </list>
///
/// Read once and cached: these are deployment settings, fixed for the run of the app.
/// </summary>
public static class DotEnv
{
    private static Dictionary<string, string>? _values;
    private static readonly object Gate = new();

    /// <summary>The value for <paramref name="key"/>, or null when no .env defines it.</summary>
    public static string? Get(string key)
    {
        lock (Gate)
        {
            _values ??= Load();
            return _values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
        }
    }

    /// <summary>Forgets the cached file. Only needed by tests — the app reads its .env once.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _values = null;
        }
    }

    /// <summary>Where the app looks for its .env, most specific first.</summary>
    public static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, ".env");

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
        {
            yield return Path.Combine(documents, "ChayChaupalPOS", ".env");
        }
    }

    private static Dictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    // Blank lines and # comments are skipped, as in any .env.
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    var eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    var key = line[..eq].Trim();
                    // Quotes are stripped so POS_API_URL="https://…" reads the same as unquoted.
                    var value = line[(eq + 1)..].Trim().Trim('"', '\'');

                    // First file wins: the executable's own .env beats the shared one in Documents.
                    if (key.Length > 0 && !values.ContainsKey(key))
                    {
                        values[key] = value;
                    }
                }
            }
            catch
            {
                // An unreadable .env must never stop the till from starting; the caller falls
                // back to its own default.
            }
        }

        return values;
    }
}
