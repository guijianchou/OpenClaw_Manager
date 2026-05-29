// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal static class WebViewCommandScripts
{
    private const string StopInjectionResourceName = "OpenClaw.Services.WebViewCommands.StopInjection.js";
    private const string AbortRunResourceName = "OpenClaw.Services.WebViewCommands.AbortRun.js";

    private static readonly Lazy<string> StopInjectionScript = new(() => Load(StopInjectionResourceName));
    private static readonly Lazy<string> AbortRunScript = new(() => Load(AbortRunResourceName));

    public static string StopInjection => StopInjectionScript.Value;

    public static string AbortRun => AbortRunScript.Value;

    private static string Load(string resourceName)
    {
        var assembly = typeof(WebViewCommandScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded WebView command script: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
