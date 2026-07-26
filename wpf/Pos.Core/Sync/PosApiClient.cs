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

    public PosApiClient(string baseUrl, string clientSlug = "", TimeSpan? timeout = null)
    {
        BaseUrl = (baseUrl ?? "").TrimEnd('/');
        ClientSlug = clientSlug ?? "";
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(ClientSlug))
        {
            _http.DefaultRequestHeaders.Add("X-POS-Client", ClientSlug);
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
    public Task<JsonElement?> PutJsonAsync(string path, string json, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Put, path, json, ct);

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

    private async Task<JsonElement?> SendJsonAsync(HttpMethod method, string path, string json, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Url(path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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
