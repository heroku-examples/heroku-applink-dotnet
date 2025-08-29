using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heroku.Applink.Models;

namespace Heroku.Applink;

/// <summary>
/// Resolves Salesforce/Data Cloud org authorization information from Heroku AppLink
/// environment variables and returns a strongly-typed <see cref="Models.Org"/>.
/// </summary>
public static class ApplinkAuth
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Resolves an authorization using the default addon name from <c>HEROKU_APPLINK_ADDON_NAME</c>
    /// (defaults to <c>HEROKU_APPLINK</c>).
    /// </summary>
    /// <param name="developerName">Developer identifier used to scope the authorization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="Org"/> with tokens, URLs, and API helpers.</returns>
    public static Task<Org> GetAuthorizationAsync(string developerName, CancellationToken cancellationToken = default)
        => GetAuthorizationAsync(developerName, Environment.GetEnvironmentVariable("HEROKU_APPLINK_ADDON_NAME") ?? "HEROKU_APPLINK", cancellationToken);

    /// <summary>
    /// Resolves an authorization by attachment name, color suffix, or full API URL.
    /// </summary>
    /// <param name="developerName">Developer identifier used to scope the authorization.</param>
    /// <param name="attachmentNameOrColorOrUrl">Attachment name, color (e.g., PURPLE), or full API URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="Org"/> with tokens, URLs, and API helpers.</returns>
    public static async Task<Org> GetAuthorizationAsync(string developerName, string attachmentNameOrColorOrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(developerName))
            throw new ArgumentException("Developer name not provided", nameof(developerName));

        var resolveByUrl = Uri.TryCreate(attachmentNameOrColorOrUrl, UriKind.Absolute, out _);
        var config = resolveByUrl
            ? AddonConfigResolver.ResolveByUrl(attachmentNameOrColorOrUrl)
            : AddonConfigResolver.ResolveByAttachmentOrColor(attachmentNameOrColorOrUrl);

        using var httpClient = new HttpClient();
        var authUrl = $"{config.ApiUrl.TrimEnd('/')}/authorizations/{Uri.EscapeDataString(developerName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, authUrl);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.Token}");
        request.Headers.TryAddWithoutValidation("X-App-UUID", config.AppUuid);
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        // Basic retry: 1 retry on transient errors (non-success)
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var payload = await JsonSerializer.DeserializeAsync<AuthorizationResponse>(contentStream, JsonOptions, cancellationToken).ConfigureAwait(false)
                              ?? throw new InvalidOperationException("Empty response from authorization service");

                var org = payload.Org;
                return new Org(
                    org.UserAuth.AccessToken,
                    org.ApiVersion,
                    null, // namespace reserved for future use
                    org.Id,
                    org.InstanceUrl,
                    org.UserAuth.UserId,
                    org.UserAuth.Username,
                    org.Type
                );
            }
            else
            {
                // Try to parse JSON error with title/detail; otherwise throw generic
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var err = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
                    if (!string.IsNullOrWhiteSpace(err?.Title) && !string.IsNullOrWhiteSpace(err?.Detail))
                        throw new InvalidOperationException($"{err!.Title} - {err!.Detail}");
                }
                catch
                {
                    // ignore JSON parse errors and fall through
                }

                if (attempt == 0)
                    continue; // one retry

                response.EnsureSuccessStatusCode();
            }
        }

        throw new InvalidOperationException("Unable to get authorization");
    }

    private sealed class AuthorizationResponse
    {
        public required OrgPayload Org { get; init; }
    }

    private sealed class OrgPayload
    {
        public required string Id { get; init; }
        public required string InstanceUrl { get; init; }
        public required string ApiVersion { get; init; }
        public required string Type { get; init; }
        public required UserAuthPayload UserAuth { get; init; }
    }

    private sealed class UserAuthPayload
    {
        public required string AccessToken { get; init; }
        public required string UserId { get; init; }
        public required string Username { get; init; }
    }

    private sealed class ErrorResponse
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
    }
}
