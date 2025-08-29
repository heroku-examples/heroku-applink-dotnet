using System.Text.Json.Serialization;

namespace Heroku.Applink.Data;

/// <summary>
/// Base record payload used for create/update operations.
/// </summary>
public class Record
{
    /// <summary>sObject API name (e.g., Account).</summary>
    public required string Type { get; init; }
    /// <summary>Field dictionary for the record.</summary>
    public required Dictionary<string, object?> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional binary field values to be base64-encoded.</summary>
    public Dictionary<string, byte[]>? BinaryFields { get; init; }
}

/// <summary>Record payload for create operations.</summary>
public sealed class RecordForCreate : Record { }

/// <summary>Record payload for update operations.</summary>
public sealed class RecordForUpdate : Record { }

/// <summary>Result for create/update/delete operations.</summary>
public sealed class RecordModificationResult
{
    /// <summary>Resulting record Id.</summary>
    public required string Id { get; init; }
}

/// <summary>Query result for SOQL queries.</summary>
public sealed class RecordQueryResult
{
    /// <summary>True when the full result set has been returned.</summary>
    public required bool Done { get; init; }
    /// <summary>Total number of records.</summary>
    public required int TotalSize { get; init; }
    /// <summary>Records included in this page.</summary>
    public required List<QueriedRecord> Records { get; init; } = new();
    /// <summary>URL for fetching the next page, if any.</summary>
    public string? NextRecordsUrl { get; init; }
}

/// <summary>Record returned by SOQL queries.</summary>
public sealed class QueriedRecord
{
    /// <summary>sObject API name of the record.</summary>
    public required string Type { get; init; }
    /// <summary>Field dictionary for the record.</summary>
    public required Dictionary<string, object?> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional binary field values.</summary>
    public Dictionary<string, byte[]>? BinaryFields { get; init; }
    /// <summary>Optional subquery results keyed by relationship name.</summary>
    public Dictionary<string, RecordQueryResult>? SubQueryResults { get; init; }
}

internal sealed class QueryResponse
{
    [JsonPropertyName("done")] public required bool Done { get; init; }
    [JsonPropertyName("totalSize")] public required int TotalSize { get; init; }
    [JsonPropertyName("nextRecordsUrl")] public string? NextRecordsUrl { get; init; }
    [JsonPropertyName("records")] public required List<Dictionary<string, object?>> Records { get; init; }
}
