// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public static class ControlUiProbeUriFactory
{
    private const string ControlUiConfigPath = "__openclaw__/a2ui/";

    public static Uri? TryCreateConfigUri(string? controlUiUrl)
    {
        if (string.IsNullOrWhiteSpace(controlUiUrl) ||
            !Uri.TryCreate(controlUiUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return TryCreateConfigUri(uri);
    }

    public static string? TryCreateProbeKey(string? controlUiUrl)
    {
        var probeUri = TryCreateConfigUri(controlUiUrl);
        return TryCreateProbeKey(probeUri);
    }

    public static string? TryCreateProbeKey(Uri? controlUiUri)
    {
        var probeUri = TryCreateConfigUri(controlUiUri);
        return probeUri?.AbsoluteUri;
    }

    public static Uri? TryCreateConfigUri(Uri? controlUiUri)
    {
        if (controlUiUri is null || controlUiUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var builder = new UriBuilder(controlUiUri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        var basePath = builder.Path;
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            basePath = "/";
        }
        else if (!basePath.EndsWith('/'))
        {
            basePath += "/";
        }

        if (!basePath.EndsWith($"/{ControlUiConfigPath}", StringComparison.OrdinalIgnoreCase))
        {
            basePath += ControlUiConfigPath;
        }

        builder.Path = basePath;
        return builder.Uri;
    }
}
