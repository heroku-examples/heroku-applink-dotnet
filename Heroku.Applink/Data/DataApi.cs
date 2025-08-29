using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Heroku.Applink.Data;

/// <summary>
/// Minimal Salesforce REST Data API client for CRUD, SOQL, and a small
/// unit-of-work helper built on the composite API.
/// </summary>
public sealed class DataApi
{
    private readonly string _accessToken;
    private readonly string _apiVersion;
    private readonly string _domainUrl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new Data API client.
    /// </summary>
    /// <param name="accessToken">OAuth access token.</param>
    /// <param name="apiVersion">API version, with or without leading 'v'.</param>
    /// <param name="domainUrl">Instance base URL.</param>
    public DataApi(string accessToken, string apiVersion, string domainUrl)
    {
        _accessToken = accessToken;
        _apiVersion = apiVersion.TrimStart('v', 'V');
        _domainUrl = domainUrl.TrimEnd('/');
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_domainUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    /// <summary>Creates a new sObject record.</summary>
    /// <param name="record">Record payload with type and fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the created record Id.</returns>
    public async Task<RecordModificationResult> CreateAsync(RecordForCreate record, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/sobjects/{record.Type}";
        var payload = BuildCreateFields(record);
        using var resp = await client.PostAsync(url, JsonContent(payload), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException($"Create failed: {json}");
        return new RecordModificationResult { Id = id! };
    }

    /// <summary>Executes a SOQL query.</summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RecordQueryResult> QueryAsync(string soql, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/query?q={Uri.EscapeDataString(soql)}";
        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var raw = await JsonSerializer.DeserializeAsync<QueryResponse>(stream, JsonOptions, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("Invalid query response");
        var records = new List<QueriedRecord>(raw.Records.Count);
        foreach (var rec in raw.Records)
        {
            var type = rec.TryGetValue("attributes", out var attrObj) && attrObj is JsonElement jeAttr && jeAttr.ValueKind == JsonValueKind.Object && jeAttr.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == null && rec.TryGetValue("attributes", out var obj2) && obj2 is Dictionary<string, object?>) { /* fallback */ }
            type ??= (rec["attributes"] as Dictionary<string, object?>)?["type"] as string ?? "";

            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var subQueries = new Dictionary<string, RecordQueryResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var (k, v) in rec)
            {
                if (k == "attributes" || v is null) continue;
                if (v is JsonElement je && je.ValueKind == JsonValueKind.Object)
                {
                    if (je.TryGetProperty("attributes", out _))
                    {
                        // nested sobject
                        fields[k] = JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText(), JsonOptions);
                    }
                    else
                    {
                        // potential subquery
                        var sub = JsonSerializer.Deserialize<QueryResponse>(je.GetRawText(), JsonOptions);
                        if (sub != null && sub.Records != null)
                        {
                            subQueries[k] = new RecordQueryResult
                            {
                                Done = sub.Done,
                                TotalSize = sub.TotalSize,
                                NextRecordsUrl = sub.NextRecordsUrl,
                                Records = new List<QueriedRecord>() // flatten omitted for brevity
                            };
                        }
                    }
                }
                else
                {
                    fields[k] = v is JsonElement jv ? ExtractJsonElement(jv) : v;
                }
            }

            records.Add(new QueriedRecord
            {
                Type = type,
                Fields = fields,
                SubQueryResults = subQueries.Count > 0 ? subQueries : null
            });
        }

        return new RecordQueryResult
        {
            Done = raw.Done,
            TotalSize = raw.TotalSize,
            NextRecordsUrl = raw.NextRecordsUrl,
            Records = records
        };
    }

    /// <summary>Retrieves the next page for a previous SOQL query result.</summary>
    /// <param name="prior">Previous query result with <c>NextRecordsUrl</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RecordQueryResult> QueryMoreAsync(RecordQueryResult prior, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prior.NextRecordsUrl))
        {
            return new RecordQueryResult { Done = prior.Done, TotalSize = prior.TotalSize, Records = new(), NextRecordsUrl = prior.NextRecordsUrl };
        }
        using var client = CreateClient();
        using var resp = await client.GetAsync(prior.NextRecordsUrl, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var raw = await JsonSerializer.DeserializeAsync<QueryResponse>(stream, JsonOptions, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("Invalid query response");
        // Reuse mapping as above but simpler
        var records = raw.Records.Select(r => new QueriedRecord { Type = (r["attributes"] as Dictionary<string, object?>)?["type"] as string ?? "", Fields = new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase) }).ToList();
        return new RecordQueryResult { Done = raw.Done, TotalSize = raw.TotalSize, Records = records, NextRecordsUrl = raw.NextRecordsUrl };
    }

    /// <summary>Updates an existing sObject record.</summary>
    /// <param name="record">Record payload including Id in fields.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RecordModificationResult> UpdateAsync(RecordForUpdate record, CancellationToken ct = default)
    {
        if (!TryNormalizeId(record.Fields, out var id)) throw new ArgumentException("Record fields must include id");
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/sobjects/{record.Type}/{id}";
        var payload = BuildCreateFields(record);
        using var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent(payload) };
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return new RecordModificationResult { Id = id };
    }

    /// <summary>Deletes a record by type and Id.</summary>
    /// <param name="type">sObject API name.</param>
    /// <param name="id">Record Id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RecordModificationResult> DeleteAsync(string type, string id, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/sobjects/{type}/{id}";
        using var resp = await client.DeleteAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return new RecordModificationResult { Id = id };
    }

    // Unit of work (composite graph)
    /// <summary>Creates a new unit-of-work for composite operations.</summary>
    public UnitOfWork NewUnitOfWork() => new(_apiVersion);

    /// <summary>
    /// Commits the unit-of-work via the REST composite graph endpoint.
    /// Throws if any subrequest fails.
    /// </summary>
    public async Task<Dictionary<ReferenceId, RecordModificationResult>> CommitUnitOfWorkAsync(UnitOfWork uow, CancellationToken ct = default)
    {
        if (uow.Subrequests.Count == 0) return new();
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/composite/graph";
        var body = new
        {
            graphs = new[]
            {
                new
                {
                    graphId = "graph0",
                    compositeRequest = uow.Subrequests.Select(s => new
                    {
                        referenceId = s.ReferenceId.ToString(),
                        method = s.Method,
                        url = s.BuildUri(_apiVersion),
                        body = s.Body
                    }).ToArray()
                }
            }
        };
        using var resp = await client.PostAsync(url, JsonContent(body), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var graph = doc.RootElement.GetProperty("graphs")[0].GetProperty("graphResponse").GetProperty("compositeResponse");
        var results = new Dictionary<ReferenceId, RecordModificationResult>();
        foreach (var item in graph.EnumerateArray())
        {
            var refId = item.GetProperty("referenceId").GetString()!;
            var status = item.GetProperty("httpStatusCode").GetInt32();
            var bodyEl = item.GetProperty("body");
            if (status >= 200 && status < 300)
            {
                var id = bodyEl.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    results[new ReferenceId(refId)] = new RecordModificationResult { Id = id! };
                }
            }
            else
            {
                var errMsg = bodyEl.TryGetProperty("message", out var msg) ? msg.GetString() : bodyEl.ToString();
                throw new InvalidOperationException($"Composite subrequest {refId} failed: {errMsg}");
            }
        }
        return results;
    }

    private static bool TryNormalizeId(Dictionary<string, object?> fields, out string id)
    {
        foreach (var key in new[] { "id", "Id", "ID", "iD" })
        {
            if (fields.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
            {
                id = s;
                fields.Remove(key);
                return true;
            }
        }
        id = "";
        return false;
    }

    private static object? ExtractJsonElement(JsonElement e)
        => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<object>(e.GetRawText()),
            _ => null
        };

    private static StringContent JsonContent(object value)
        => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static Dictionary<string, object?> BuildCreateFields(Record record)
    {
        var fields = new Dictionary<string, object?>(record.Fields, StringComparer.OrdinalIgnoreCase);
        if (record.BinaryFields != null)
        {
            foreach (var (name, data) in record.BinaryFields)
            {
                if (fields.ContainsKey(name))
                    throw new InvalidOperationException($"{name} provided in both fields and binaryFields of {record.Type}");
                fields[name] = Convert.ToBase64String(data);
            }
        }
        return fields;
    }
}

/// <summary>Opaque reference Id for unit-of-work subrequests.</summary>
public sealed class ReferenceId
{
    private readonly string _id;
    /// <summary>Creates a new reference Id.</summary>
    public ReferenceId(string id) { _id = id; }
    /// <summary>Returns the reference as a string.</summary>
    public override string ToString() => _id;
}

/// <summary>Accumulates create, update, and delete operations.</summary>
public sealed class UnitOfWork
{
    internal List<CompositeSubrequest> Subrequests { get; } = new();
    private readonly string _apiVersion;
    /// <summary>Initializes a new unit-of-work.</summary>
    public UnitOfWork(string apiVersion) { _apiVersion = apiVersion; }

    /// <summary>Registers a create operation.</summary>
    /// <param name="type">sObject API name.</param>
    /// <param name="fields">Field dictionary.</param>
    public ReferenceId RegisterCreate(string type, Dictionary<string, object?> fields)
    {
        var refId = new ReferenceId(Guid.NewGuid().ToString("N"));
        Subrequests.Add(CompositeSubrequest.Create(refId, type, fields));
        return refId;
    }

    /// <summary>Registers an update operation by Id.</summary>
    /// <param name="type">sObject API name.</param>
    /// <param name="id">Record Id.</param>
    /// <param name="fields">Field dictionary.</param>
    public ReferenceId RegisterUpdate(string type, string id, Dictionary<string, object?> fields)
    {
        var refId = new ReferenceId(Guid.NewGuid().ToString("N"));
        fields = new(fields) { ["Id"] = id };
        Subrequests.Add(CompositeSubrequest.Update(refId, type, fields));
        return refId;
    }

    /// <summary>Registers a delete operation by Id.</summary>
    /// <param name="type">sObject API name.</param>
    /// <param name="id">Record Id.</param>
    public ReferenceId RegisterDelete(string type, string id)
    {
        var refId = new ReferenceId(Guid.NewGuid().ToString("N"));
        Subrequests.Add(CompositeSubrequest.Delete(refId, type, id));
        return refId;
    }
}

internal sealed class CompositeSubrequest
{
    public required ReferenceId ReferenceId { get; init; }
    public required string Method { get; init; }
    public required string Url { get; set; }
    public object? Body { get; init; }

    public string BuildUri(string apiVersion) => Url.Replace("{version}", apiVersion);

    public static CompositeSubrequest Create(ReferenceId refId, string type, Dictionary<string, object?> body) => new CompositeSubrequest
    {
        ReferenceId = refId,
        Method = HttpMethod.Post.Method,
        Url = $"/services/data/v{_VERSION_PLACEHOLDER}/sobjects/{type}",
        Body = body
    }.Fix();

    public static CompositeSubrequest Update(ReferenceId refId, string type, Dictionary<string, object?> body) => new CompositeSubrequest
    {
        ReferenceId = refId,
        Method = HttpMethod.Patch.Method,
        Url = $"/services/data/v{_VERSION_PLACEHOLDER}/sobjects/{type}/@{{Id}}",
        Body = body
    }.Fix();

    public static CompositeSubrequest Delete(ReferenceId refId, string type, string id) => new CompositeSubrequest
    {
        ReferenceId = refId,
        Method = HttpMethod.Delete.Method,
        Url = $"/services/data/v{_VERSION_PLACEHOLDER}/sobjects/{type}/{id}"
    }.Fix();

    private const string _VERSION_PLACEHOLDER = "{version}";

    private CompositeSubrequest Fix()
    {
        Url = Url.Replace($"v{_VERSION_PLACEHOLDER}", "v{version}");
        return this;
    }
}
