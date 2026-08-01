using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Pos.Core.Sync;

/// <summary>
/// Talks to the PHP POS API. Deliberately thin: the till is offline-first, so every call
/// here is allowed to fail and the caller carries on with local SQLite.
/// </summary>
public sealed class PosApiClient
{
    private readonly HttpClient _http;

    public string BaseUrl { get; }
    public string ClientSlug { get; }
    public long ClientId { get; }

    /// <summary>
    /// Which business every request speaks for.
    ///
    /// The server picks the client from <c>X-Client-Id</c>, then <c>X-POS-Client</c>, then the
    /// login token, and falls back to whichever client is first in the table when a request says
    /// nothing. A till carries no login token, and this was being built with neither header — so
    /// every setting the counter saved was written against that fallback client. Sign in as the
    /// second business, rename the shop, and the rename landed on the first one's row.
    /// </summary>
    public PosApiClient(string baseUrl, string clientSlug = "", long clientId = 0, TimeSpan? timeout = null)
    {
        BaseUrl = (baseUrl ?? "").TrimEnd('/');
        ClientSlug = clientSlug ?? "";
        ClientId = clientId;
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(ClientSlug))
        {
            _http.DefaultRequestHeaders.Add("X-POS-Client", ClientSlug);
        }
        if (ClientId > 0)
        {
            _http.DefaultRequestHeaders.Add("X-Client-Id", ClientId.ToString());
        }
    }

    public async Task<JsonElement?> GetAsync(string path, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(Url(path), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Posts a raw JSON body. Throws with the server's message so the queue row can
    /// record why it failed instead of a bare status code.</summary>
    public Task<JsonElement?> PostJsonAsync(string path, string json, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Post, path, json, ct);

    /// <summary>Same contract as <see cref="PostJsonAsync"/> — used for catalog updates
    /// (menu items, categories, tables, areas, GST), which the server exposes as PUT.</summary>
    /// <param name="clientIdOverride">
    /// Sends this request on behalf of a different business than the one this client was built
    /// for. Needed by the sync queue: a setting is queued under whoever was signed in when it
    /// was saved, but it may not be flushed until after a shift change — and pushing it as the
    /// new operator's business would write one shop's profile onto another's.
    /// </param>
    public Task<JsonElement?> PutJsonAsync(string path, string json, CancellationToken ct = default,
        long? clientIdOverride = null) =>
        SendJsonAsync(HttpMethod.Put, path, json, ct, clientIdOverride);

    /// <summary>Deletes a catalog row. No body — the id is in the path.</summary>
    public async Task<JsonElement?> DeleteAsync(string path, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(Url(path), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Trim(body)}");
        }
        return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<JsonElement?> SendJsonAsync(HttpMethod method, string path, string json, CancellationToken ct,
        long? clientIdOverride = null)
    {
        using var request = new HttpRequestMessage(method, Url(path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (clientIdOverride is > 0 && clientIdOverride != ClientId)
        {
            // Set on the request, which is what HttpClient sends instead of the default of the
            // same name. The slug header still names the client this object was built for, but
            // the server resolves X-Client-Id before it looks at the slug, so this wins.
            request.Headers.Add("X-Client-Id", clientIdOverride.Value.ToString());
        }
        var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Trim(body)}");
        }

        return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>True when the API answers at all — used to decide whether syncing is worth
    /// attempting at this moment.</summary>
    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var response = await probe.GetAsync(Url("/health"), ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string Url(string path) => BaseUrl + "/" + path.TrimStart('/');

    private static string Trim(string s) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s[..300] : s).Replace('\n', ' ').Replace('\r', ' ');
}
