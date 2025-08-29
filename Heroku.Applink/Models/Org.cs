using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Heroku.Applink.Data;
using Heroku.Applink.Bulk;
using Heroku.Applink.DataCloud;

namespace Heroku.Applink.Models;

/// <summary>
/// Represents a resolved Salesforce/Data Cloud org authorization and exposes
/// typed helpers for Data API, Bulk API v2, and optional Data Cloud.
/// </summary>
public sealed class Org
{
    /// <summary>OAuth access token for the org.</summary>
    public string AccessToken { get; }
    /// <summary>REST API version (e.g., 62.0).</summary>
    public string ApiVersion { get; }
    /// <summary>Base instance URL for the org.</summary>
    public string DomainUrl { get; }
    /// <summary>Org Id.</summary>
    public string Id { get; }
    /// <summary>Org namespace (if any).</summary>
    public string Namespace { get; }
    /// <summary>Current user info.</summary>
    public User User { get; }
    /// <summary>Org type (e.g., Standard, DataCloudOrg).</summary>
    public string OrgType { get; }
    /// <summary>Salesforce REST Data API helper.</summary>
    public DataApi DataApi { get; }
    /// <summary>Bulk API v2 helper.</summary>
    public BulkApi BulkApi { get; }
    /// <summary>Data Cloud API helper when available.</summary>
    public DataCloudApi? DataCloudApi { get; }

    /// <summary>
    /// Constructs an <see cref="Org"/> from resolved authorization details.
    /// </summary>
    public Org(
        string accessToken,
        string apiVersion,
        string? @namespace,
        string orgId,
        string orgDomainUrl,
        string userId,
        string username,
        string orgType)
    {
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        ApiVersion = apiVersion?.TrimStart('v', 'V') ?? throw new ArgumentNullException(nameof(apiVersion));
        DomainUrl = orgDomainUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? orgDomainUrl : $"https://{orgDomainUrl}";
        Id = orgId ?? throw new ArgumentNullException(nameof(orgId));
        Namespace = string.IsNullOrWhiteSpace(@namespace) || string.Equals(@namespace, "null", StringComparison.OrdinalIgnoreCase) ? string.Empty : @namespace!;
        OrgType = orgType ?? "";
        User = new User(userId, username);

        BulkApi = new BulkApi(AccessToken, ApiVersion, DomainUrl);
        DataApi = new DataApi(AccessToken, ApiVersion, DomainUrl);
        if (string.Equals(OrgType, "DataCloudOrg", StringComparison.OrdinalIgnoreCase) || string.Equals(OrgType, "DatacloudOrg", StringComparison.OrdinalIgnoreCase))
        {
            DataCloudApi = new DataCloudApi(AccessToken, DomainUrl);
        }
    }

    /// <summary>
    /// Sends an authenticated request to a full URL or path under the org domain.
    /// Adds the Authorization header if missing.
    /// </summary>
    public Task<HttpResponseMessage> RequestAsync(HttpClient httpClient, string fullUrlOrUrlPart, HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var url = fullUrlOrUrlPart.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? fullUrlOrUrlPart
            : $"{DomainUrl.TrimEnd('/')}/{fullUrlOrUrlPart.TrimStart('/')}";

        request.RequestUri = new Uri(url);
        if (!request.Headers.Contains("Authorization"))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {AccessToken}");
        }

        return httpClient.SendAsync(request, cancellationToken);
    }
}
