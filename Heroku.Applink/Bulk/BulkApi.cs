using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace Heroku.Applink.Bulk;

/// <summary>
/// Simplified Salesforce Bulk API v2 helper for ingest and query, including
/// CSV helpers and utilities for splitting payloads near 100MB limits.
/// </summary>
public sealed class BulkApi
{
    private readonly string _accessToken;
    private readonly string _apiVersion;
    private readonly string _instanceUrl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new Bulk API client.
    /// </summary>
    /// <param name="accessToken">OAuth access token.</param>
    /// <param name="apiVersion">API version, with or without leading 'v'.</param>
    /// <param name="instanceUrl">Org instance URL.</param>
    public BulkApi(string accessToken, string apiVersion, string instanceUrl)
    {
        _accessToken = accessToken;
        _apiVersion = apiVersion.TrimStart('v', 'V');
        _instanceUrl = instanceUrl.TrimEnd('/');
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_instanceUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    // Job references
    /// <summary>
    /// Reference to a Bulk ingest job.
    /// </summary>
    public record IngestJobReference(string Id)
    {
        /// <summary>Identifier of the job.</summary>
        public string Type => "ingestJob";
    }
    /// <summary>
    /// Reference to a Bulk query job.
    /// </summary>
    public record QueryJobReference(string Id)
    {
        /// <summary>Identifier of the job.</summary>
        public string Type => "queryJob";
    }

    // DataTable
    /// <summary>
    /// Tabular data container used for CSV upload and query results.
    /// </summary>
    public sealed class DataTable : List<Dictionary<string, string?>>
    {
        /// <summary>Column names for the table.</summary>
        public required string[] Columns { get; init; }
    }

    /// <summary>
    /// Fluent builder for <see cref="DataTable"/> instances.
    /// </summary>
    public sealed class DataTableBuilder
    {
        private readonly string[] _columns;
        private readonly List<Dictionary<string, string?>> _rows = new();
        /// <summary>Initialize a new builder with column names.</summary>
        public DataTableBuilder(params string[] columns) { _columns = columns; }
        /// <summary>Adds a row by column name.</summary>
        public DataTableBuilder AddRow(Dictionary<string, string?> row)
        {
            var mapped = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var c in _columns)
            {
                if (row.TryGetValue(c, out var v)) mapped[c] = v; else mapped[c] = null;
            }
            _rows.Add(mapped);
            return this;
        }
        /// <summary>Adds a row by ordered values.</summary>
        public DataTableBuilder AddRow(string?[] row)
        {
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (int i = 0; i < _columns.Length && i < row.Length; i++) map[_columns[i]] = row[i];
            _rows.Add(map);
            return this;
        }
        /// <summary>Builds the <see cref="DataTable"/>.</summary>
        public DataTable Build() => new DataTable { Columns = _columns, Capacity = _rows.Count }.Also(dt => dt.AddRange(_rows));
    }

    /// <summary>
    /// Creates a <see cref="DataTableBuilder"/> with the specified columns.
    /// </summary>
    public DataTableBuilder CreateDataTableBuilder(params string[] columns) => new(columns);

    /// <summary>
    /// Splits a table into multiple tables whose CSV payloads fit under ~100MB.
    /// </summary>
    public IEnumerable<DataTable> SplitDataTable(DataTable dataTable)
    {
        const int SIZE_100_MB = 100_000_000;
        var csvHeader = ToCsv(new[] { dataTable.Columns });
        var headerBytes = Encoding.UTF8.GetByteCount(csvHeader);
        var builder = CreateDataTableBuilder(dataTable.Columns);
        var currentSize = headerBytes;
        foreach (var row in dataTable)
        {
            var rowValues = dataTable.Columns.Select(c => row.TryGetValue(c, out var v) ? v : null).ToArray();
            var line = ToCsv(new[] { rowValues });
            var rowBytes = Encoding.UTF8.GetByteCount(line);
            if (currentSize + rowBytes >= SIZE_100_MB)
            {
                yield return builder.Build();
                builder = CreateDataTableBuilder(dataTable.Columns);
                currentSize = headerBytes + rowBytes;
            }
            else
            {
                currentSize += rowBytes;
            }
            builder.AddRow(rowValues);
        }
        yield return builder.Build();
    }

    /// <summary>Formats a date as yyyy-MM-dd (UTC).</summary>
    public string FormatDate(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    /// <summary>Formats a datetime as ISO-8601 (UTC).</summary>
    public string FormatDateTime(DateTime value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    /// <summary>Returns the CSV null token (#N/A).</summary>
    public string FormatNullValue() => "#N/A";

    // Ingest
    /// <summary>
    /// Creates ingest jobs as needed and uploads CSV parts for the given object.
    /// Returns job references or error descriptors per part.
    /// </summary>
    public async Task<List<object>> IngestAsync(string objectApiName, DataTable dataTable, string operation = "insert", CancellationToken ct = default)
    {
        var results = new List<object>();
        foreach (var part in SplitDataTable(dataTable))
        {
            var job = await CreateIngestJobAsync(objectApiName, operation, ct).ConfigureAwait(false);
            try
            {
                await UploadIngestDataAsync(job.Id, part, ct).ConfigureAwait(false);
                await CompleteIngestJobAsync(job.Id, ct).ConfigureAwait(false);
                results.Add(job);
            }
            catch (Exception ex)
            {
                results.Add(new { error = ex.Message, jobReference = job });
            }
        }
        return results;
    }

    /// <summary>Aborts a running Bulk job (ingest or query).</summary>
    public async Task AbortAsync(object jobRef, CancellationToken ct = default)
    {
        switch (jobRef)
        {
            case IngestJobReference ij:
                await PatchJobAsync($"/services/data/v{_apiVersion}/jobs/ingest/{ij.Id}", new { state = "Aborted" }, ct);
                break;
            case QueryJobReference qj:
                await PatchJobAsync($"/services/data/v{_apiVersion}/jobs/query/{qj.Id}", new { state = "Aborted" }, ct);
                break;
        }
    }

    /// <summary>Deletes a Bulk job (ingest or query).</summary>
    public async Task DeleteAsync(object jobRef, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = jobRef switch
        {
            IngestJobReference ij => $"/services/data/v{_apiVersion}/jobs/ingest/{ij.Id}",
            QueryJobReference qj => $"/services/data/v{_apiVersion}/jobs/query/{qj.Id}",
            _ => throw new ArgumentException("Unknown job reference")
        };
        using var resp = await client.DeleteAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Gets job info for an ingest or query job.</summary>
    public async Task<JsonDocument> GetInfoAsync(object jobRef, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = jobRef switch
        {
            IngestJobReference ij => $"/services/data/v{_apiVersion}/jobs/ingest/{ij.Id}",
            QueryJobReference qj => $"/services/data/v{_apiVersion}/jobs/query/{qj.Id}",
            _ => throw new ArgumentException("Unknown job reference")
        };
        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return (await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Result page for a Bulk query job (CSV parsed to a table).
    /// </summary>
    public sealed class QueryJobResults
    {
        /// <summary>Data table for this page of results.</summary>
        public required DataTable DataTable { get; init; }
        /// <summary>Server-provided locator for pagination; null when done.</summary>
        public string? Locator { get; init; }
        /// <summary>Number of records in this page.</summary>
        public int NumberOfRecords { get; init; }
        /// <summary>Associated query job reference.</summary>
        public required QueryJobReference JobReference { get; init; }
        /// <summary>True when no further pages are available.</summary>
        public bool Done => string.IsNullOrWhiteSpace(Locator);
    }

    /// <summary>Creates a Bulk query job for a SOQL statement.</summary>
    public async Task<QueryJobReference> QueryAsync(string soql, string operation = "query", CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/jobs/query";
        var body = new { operation, query = soql };
        using var resp = await client.PostAsync(url, JsonContent(body), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetString()!;
        return new QueryJobReference(id);
    }

    /// <summary>Fetches a page of results for a query job.</summary>
    public async Task<QueryJobResults> GetQueryResultsAsync(QueryJobReference jobRef, int? maxRecords = null, string? locator = null, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var url = new StringBuilder($"/services/data/v{_apiVersion}/jobs/query/{jobRef.Id}/results");
        var sep = '?';
        if (!string.IsNullOrWhiteSpace(locator)) { url.Append($"{sep}locator={Uri.EscapeDataString(locator)}"); sep = '&'; }
        if (maxRecords.HasValue) { url.Append($"{sep}maxRecords={maxRecords.Value}"); }
        using var req = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var columns = ParseColumnsFromHeaders(resp.Headers);
        var (table, numberOfRecords) = await ParseCsvToDataTableAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), columns, ct).ConfigureAwait(false);
        var outLocator = resp.Headers.TryGetValues("sforce-locator", out var vals) ? vals.FirstOrDefault() : null;
        return new QueryJobResults { DataTable = table, JobReference = jobRef, Locator = outLocator == "null" ? null : outLocator, NumberOfRecords = numberOfRecords };
    }

    /// <summary>Fetches the next page using the returned locator.</summary>
    public Task<QueryJobResults> GetMoreQueryResultsAsync(QueryJobResults current, int? maxRecords = null, CancellationToken ct = default)
        => GetQueryResultsAsync(current.JobReference, maxRecords, current.Locator, ct);

    /// <summary>Retrieves the successful ingest results as a table.</summary>
    public async Task<DataTable> GetSuccessfulResultsAsync(IngestJobReference jobRef, CancellationToken ct = default)
        => await GetIngestResultsAsync(jobRef, "successfulResults", ct).ConfigureAwait(false);

    /// <summary>Retrieves the failed ingest results as a table.</summary>
    public async Task<DataTable> GetFailedResultsAsync(IngestJobReference jobRef, CancellationToken ct = default)
        => await GetIngestResultsAsync(jobRef, "failedResults", ct).ConfigureAwait(false);

    /// <summary>Retrieves the unprocessed ingest records as a table.</summary>
    public async Task<DataTable> GetUnprocessedRecordsAsync(IngestJobReference jobRef, CancellationToken ct = default)
        => await GetIngestResultsAsync(jobRef, "unprocessedrecords", ct).ConfigureAwait(false);

    // Internals
    private async Task<IngestJobReference> CreateIngestJobAsync(string objectApiName, string operation, CancellationToken ct)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/jobs/ingest";
        var body = new { @object = objectApiName, operation, contentType = "CSV" };
        using var resp = await client.PostAsync(url, JsonContent(body), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetString()!;
        return new IngestJobReference(id);
    }

    private async Task UploadIngestDataAsync(string jobId, DataTable dataTable, CancellationToken ct)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/jobs/ingest/{jobId}/batches";
        var csv = ToCsv(dataTable.Select(r => dataTable.Columns.Select(c => r.TryGetValue(c, out var v) ? v : null).ToArray()));
        using var content = new StringContent(ToCsv(new[] { dataTable.Columns }) + csv, Encoding.UTF8, "text/csv");
        using var resp = await client.PutAsync(url, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    private async Task CompleteIngestJobAsync(string jobId, CancellationToken ct)
        => await PatchJobAsync($"/services/data/v{_apiVersion}/jobs/ingest/{jobId}", new { state = "UploadComplete" }, ct).ConfigureAwait(false);

    private async Task<DataTable> GetIngestResultsAsync(IngestJobReference jobRef, string resultType, CancellationToken ct)
    {
        using var client = CreateClient();
        var url = $"/services/data/v{_apiVersion}/jobs/ingest/{jobRef.Id}/{resultType}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var columns = ParseColumnsFromHeaders(resp.Headers);
        var (table, _) = await ParseCsvToDataTableAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), columns, ct).ConfigureAwait(false);
        return table;
    }

    private async Task PatchJobAsync(string url, object body, CancellationToken ct)
    {
        using var client = CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent(body) };
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    private static string[] ParseColumnsFromHeaders(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("sforce-limited-info", out var _))
        {
            // columns may still be present
        }
        if (headers.TryGetValues("content-disposition", out var vals))
        {
            // Not reliable; instead infer from CSV header
        }
        return Array.Empty<string>();
    }

    private static async Task<(DataTable Table, int Count)> ParseCsvToDataTableAsync(Stream stream, string[] columnsHint, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true
        };
        using var csv = new CsvReader(reader, config);
        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? columnsHint;
        var builder = new DataTableBuilder(headers);
        var count = 0;
        while (await csv.ReadAsync())
        {
            var row = headers.ToDictionary(h => h, h => csv.GetField(h), StringComparer.Ordinal);
            builder.AddRow(row);
            count++;
        }
        return (builder.Build(), count);
    }

    private static StringContent JsonContent(object value)
        => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    // CSV helper for splitting and uploads
    private static string ToCsv(IEnumerable<string?[]> rows)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb, CultureInfo.InvariantCulture);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false
        };
        using var csv = new CsvWriter(writer, config);
        foreach (var row in rows)
        {
            foreach (var cell in row)
            {
                csv.WriteField(cell);
            }
            csv.NextRecord();
        }
        writer.Flush();
        return sb.ToString();
    }
}

internal static class ObjectExtensions
{
    public static T Also<T>(this T self, Action<T> block) { block(self); return self; }
}
