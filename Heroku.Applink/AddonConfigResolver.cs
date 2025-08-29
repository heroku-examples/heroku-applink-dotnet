using System;
using System.Collections;

namespace Heroku.Applink;

internal sealed record AddonConfig(string ApiUrl, string Token, string AppUuid);

internal static class AddonConfigResolver
{
    public static AddonConfig ResolveByAttachmentOrColor(string attachmentOrColor)
    {
        var appUuid = Environment.GetEnvironmentVariable("HEROKU_APP_ID");
        if (string.IsNullOrWhiteSpace(appUuid))
            throw new InvalidOperationException("Heroku Applink app UUID not found");

        var addon = Environment.GetEnvironmentVariable("HEROKU_APPLINK_ADDON_NAME") ?? "HEROKU_APPLINK";

        string? apiUrl = Environment.GetEnvironmentVariable($"{attachmentOrColor.ToUpperInvariant()}_API_URL");
        string? token = Environment.GetEnvironmentVariable($"{attachmentOrColor.ToUpperInvariant()}_TOKEN");

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(token))
        {
            apiUrl = Environment.GetEnvironmentVariable($"{addon}_{attachmentOrColor.ToUpperInvariant()}_API_URL");
            token = Environment.GetEnvironmentVariable($"{addon}_{attachmentOrColor.ToUpperInvariant()}_TOKEN");
        }

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Heroku Applink config not found under attachment or color {attachmentOrColor}");

        return new AddonConfig(apiUrl!, token!, appUuid);
    }

    public static AddonConfig ResolveByUrl(string url)
    {
        var appUuid = Environment.GetEnvironmentVariable("HEROKU_APP_ID");
        if (string.IsNullOrWhiteSpace(appUuid))
            throw new InvalidOperationException("Heroku Applink app UUID not found");

        var env = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
        string? matchedApiUrlVar = null;
        foreach (DictionaryEntry entry in env)
        {
            var key = entry.Key?.ToString() ?? string.Empty;
            var value = entry.Value?.ToString() ?? string.Empty;
            if (key.EndsWith("_API_URL", StringComparison.Ordinal) && string.Equals(value, url, StringComparison.OrdinalIgnoreCase))
            {
                matchedApiUrlVar = key;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(matchedApiUrlVar))
            throw new InvalidOperationException($"Heroku Applink config not found for API URL: {url}");

        var prefix = matchedApiUrlVar[..^"_API_URL".Length];
        var token = Environment.GetEnvironmentVariable($"{prefix}_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Heroku Applink token not found for API URL: {url}");

        var apiUrl = Environment.GetEnvironmentVariable(matchedApiUrlVar)!;
        return new AddonConfig(apiUrl, token!, appUuid);
    }
}

