using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Heroku.Applink.DataCloud;

/// <summary>
/// Minimal client for Salesforce Data Cloud query and ingest endpoints.
/// </summary>
public sealed class DataCloudApi
{
    private readonly string _accessToken;
    private readonly string _domainUrl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a Data Cloud API client.
    /// </summary>
    /// <param name="accessToken">OAuth access token.</param>
    /// <param name="domainUrl">Instance base URL.</param>
    public DataCloudApi(string accessToken, string domainUrl)
    {
        _accessToken = accessToken;
        _domainUrl = domainUrl.TrimEnd('/');
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_domainUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    /// <summary>Runs a Data Cloud SQL query.</summary>
    public async Task<JsonDocument> QueryAsync(string sql, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = "/api/v2/query";
        var body = new { sql };
        using var resp = await client.PostAsync(url, JsonContent(body), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return (await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false))!;
    }

    /// <summary>Retrieves the next batch for a prior query by batch Id.</summary>
    public async Task<JsonDocument> QueryNextBatchAsync(string nextBatchId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = $"/api/v2/query/{nextBatchId}";
        using var resp = await client.PostAsync(url, null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return (await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false))!;
    }

    /// <summary>Upserts data into a Data Cloud source object.</summary>
    public async Task<JsonDocument> UpsertAsync(string name, string objectName, object data, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = $"/api/v1/ingest/sources/{name}/{objectName}";
        using var resp = await client.PostAsync(url, JsonContent(data), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return (await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false))!;
    }

    private static StringContent JsonContent(object value)
        => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
}
