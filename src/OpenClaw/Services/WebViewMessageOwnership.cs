// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

internal sealed class WebViewMessageOwnership
{
    private readonly object _gate = new();
    private string _ownerToken = CreateToken();
    private string? _acceptedPageToken;
    private string? _acceptedSourceOrigin;
    private int _version;

    public string OwnerToken
    {
        get
        {
            lock (_gate)
            {
                return _ownerToken;
            }
        }
    }

    public string ResetForNewWebView()
    {
        lock (_gate)
        {
            _ownerToken = CreateToken();
            _acceptedPageToken = null;
            _acceptedSourceOrigin = null;
            _version++;
            return _ownerToken;
        }
    }

    public void BeginNavigation()
    {
        lock (_gate)
        {
            _acceptedPageToken = null;
            _acceptedSourceOrigin = null;
            _version++;
        }
    }

    public bool AcceptPageToken(string? source, string? pageToken)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
        {
            return false;
        }

        lock (_gate)
        {
            _acceptedPageToken = pageToken;
            _acceptedSourceOrigin = NormalizeOrigin(source);
            _version++;
            return true;
        }
    }

    public int CaptureAcceptedPageVersion()
    {
        lock (_gate)
        {
            return string.IsNullOrWhiteSpace(_acceptedPageToken) ? 0 : _version;
        }
    }

    public bool IsCurrentAcceptedPageVersion(int version)
    {
        if (version == 0)
        {
            return false;
        }

        lock (_gate)
        {
            return _version == version && !string.IsNullOrWhiteSpace(_acceptedPageToken);
        }
    }

    public bool IsCurrent(CoreWebView2WebMessageReceivedEventArgs args, string messageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(messageJson);
            return IsCurrent(args, document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public bool IsCurrent(CoreWebView2WebMessageReceivedEventArgs args, JsonElement root)
    {
        return TryCaptureCurrentVersion(args, root, out _);
    }

    public bool TryCaptureCurrentVersion(
        CoreWebView2WebMessageReceivedEventArgs args,
        JsonElement root,
        out int version)
    {
        version = 0;
        var ownerToken = GetString(root, "nativeOwnerToken");
        var pageToken = GetString(root, "nativePageToken");
        var sourceOrigin = NormalizeOrigin(args.Source);

        lock (_gate)
        {
            if (!string.Equals(ownerToken, _ownerToken, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_acceptedPageToken) ||
                !string.Equals(pageToken, _acceptedPageToken, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_acceptedSourceOrigin) &&
                !string.Equals(sourceOrigin, _acceptedSourceOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            version = _version;
            return true;
        }
    }

    private static string CreateToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string? NormalizeOrigin(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
