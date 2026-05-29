// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal static class WebViewStatusInspectionScripts
{
    private const string InspectResourceName = "OpenClaw.Services.WebViewStatusInspector.Inspect.js";

    private static readonly Lazy<string> InspectScript = new(() => Load(InspectResourceName));

    public static string Inspect => InspectScript.Value;

    private static string Load(string resourceName)
    {
        var assembly = typeof(WebViewStatusInspectionScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded WebView status inspection script: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
