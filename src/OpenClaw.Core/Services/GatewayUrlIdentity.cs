// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace OpenClaw.Services;

/// <summary>
/// Centralizes Gateway URL identity rules used by settings, recovery, and WebView2 profile isolation.
/// </summary>
public static class GatewayUrlIdentity
{
    private const string ProfileMarkerPrefix = "sha256:";

    public static bool IsSupportedGatewayUrl(string? gatewayUrl)
    {
        return TryCreateSupportedUri(gatewayUrl, out _);
    }

    public static string CreateProfileIdentityHash(string gatewayUrl)
    {
        var normalized = NormalizeForProfileIdentityHash(gatewayUrl);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string CreateProfileIdentityMarker(string gatewayUrl)
    {
        return $"{ProfileMarkerPrefix}{CreateProfileIdentityHash(gatewayUrl)}";
    }

    public static bool ProfileIdentityMarkerMatches(string? marker, string gatewayUrl)
    {
        if (string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        var trimmedMarker = marker.Trim();
        if (string.Equals(trimmedMarker, CreateProfileIdentityMarker(gatewayUrl), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryCreateSupportedUri(gatewayUrl, out var uri) &&
            HasSecretScopedProfileIdentity(uri))
        {
            return false;
        }

        return string.Equals(
            trimmedMarker,
            NormalizeForLegacyReadableProfileMarker(gatewayUrl),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameGatewayRoute(string currentGatewayUrl, string candidateUri)
    {
        if (!TryCreateSupportedUri(currentGatewayUrl, out var current) ||
            !TryCreateSupportedUri(candidateUri, out var candidate))
        {
            return string.Equals(
                currentGatewayUrl.Trim().TrimEnd('/'),
                candidateUri.Trim().TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(current.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.Host, candidate.Host, StringComparison.OrdinalIgnoreCase) ||
            current.Port != candidate.Port)
        {
            return false;
        }

        var basePath = NormalizeRoutePath(current.AbsolutePath);
        if (basePath == "/")
        {
            return true;
        }

        var candidatePath = NormalizeRoutePath(candidate.AbsolutePath);
        return string.Equals(candidatePath, basePath, StringComparison.Ordinal) ||
            candidatePath.StartsWith($"{basePath}/", StringComparison.Ordinal);
    }

    private static bool TryCreateSupportedUri(string? gatewayUrl, out Uri uri)
    {
        if (Uri.TryCreate(gatewayUrl?.Trim(), UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string NormalizeForProfileIdentityHash(string gatewayUrl)
    {
        if (!Uri.TryCreate(gatewayUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return gatewayUrl.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };

        return TrimRootPath(builder.Uri.AbsoluteUri);
    }

    private static string NormalizeForLegacyReadableProfileMarker(string gatewayUrl)
    {
        if (!Uri.TryCreate(gatewayUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return gatewayUrl.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool HasSecretScopedProfileIdentity(Uri uri)
    {
        return !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query);
    }

    private static string TrimRootPath(string absoluteUri)
    {
        return absoluteUri.EndsWith("/", StringComparison.Ordinal) &&
            !absoluteUri.Contains("?", StringComparison.Ordinal)
            ? absoluteUri.TrimEnd('/')
            : absoluteUri;
    }

    private static string NormalizeRoutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        return path.TrimEnd('/');
    }
}
