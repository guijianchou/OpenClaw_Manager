// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using OpenClaw.Helpers;

namespace OpenClaw.Services;

internal static class HostedUiBridgeScript
{
    private const string BridgeScriptResourceName = "OpenClaw.Services.HostedUiBridge.Script.js";
    private const string HostMessagingResourceName = "OpenClaw.Services.HostedUiBridge.HostMessaging.js";
    private const string MutationFilterResourceName = "OpenClaw.Services.HostedUiBridge.MutationFilter.js";
    private const string ModelResolverResourceName = "OpenClaw.Services.HostedUiBridge.ModelResolver.js";
    private const string StatusInspectionResourceName = "OpenClaw.Services.HostedUiBridge.StatusInspection.js";
    private const string CommandDispatchResourceName = "OpenClaw.Services.HostedUiBridge.CommandDispatch.js";
    private const string StringsPlaceholder = "__OPENCLAW_BRIDGE_STRINGS_JSON__";
    private const string HostMessagingPlaceholder = "__OPENCLAW_HOST_MESSAGING_SCRIPT__";
    private const string MutationFilterPlaceholder = "__OPENCLAW_MUTATION_FILTER_SCRIPT__";
    private const string ModelResolverPlaceholder = "__OPENCLAW_MODEL_RESOLVER_SCRIPT__";
    private const string StatusInspectionPlaceholder = "__OPENCLAW_STATUS_INSPECTION_SCRIPT__";
    private const string CommandDispatchPlaceholder = "__OPENCLAW_COMMAND_DISPATCH_SCRIPT__";

    private static readonly Lazy<string> BridgeScriptTemplate = new(() => LoadEmbeddedResource(BridgeScriptResourceName));
    private static readonly Lazy<string> HostMessagingScript = new(() => LoadEmbeddedResource(HostMessagingResourceName));
    private static readonly Lazy<string> MutationFilterScript = new(() => LoadEmbeddedResource(MutationFilterResourceName));
    private static readonly Lazy<string> ModelResolverScript = new(() => LoadEmbeddedResource(ModelResolverResourceName));
    private static readonly Lazy<string> StatusInspectionScript = new(() => LoadEmbeddedResource(StatusInspectionResourceName));
    private static readonly Lazy<string> CommandDispatchScript = new(() => LoadEmbeddedResource(CommandDispatchResourceName));

    public static string Build()
    {
        var strings = new Dictionary<string, string>
        {
            ["bridgeGatewayUiLoaded"] = StringResources.BridgeGatewayUiLoaded,
            ["bridgePageLoading"] = StringResources.BridgePageLoading,
            ["bridgeTokenMissingSummary"] = StringResources.BridgeTokenMissingSummary,
            ["bridgeTokenMissingDetail"] = StringResources.BridgeTokenMissingDetail,
            ["bridgeTokenMismatchSummary"] = StringResources.BridgeTokenMismatchSummary,
            ["bridgeTokenMismatchDetail"] = StringResources.BridgeTokenMismatchDetail,
            ["bridgeDeviceTokenMismatchSummary"] = StringResources.BridgeDeviceTokenMismatchSummary,
            ["bridgeDeviceTokenMismatchDetail"] = StringResources.BridgeDeviceTokenMismatchDetail,
            ["bridgeOriginRejectedSummary"] = StringResources.BridgeOriginRejectedSummary,
            ["bridgeOriginRejectedDetail"] = StringResources.BridgeOriginRejectedDetail,
            ["bridgeTrustedProxyLoopbackSummary"] = StringResources.BridgeTrustedProxyLoopbackSummary,
            ["bridgeTrustedProxyLoopbackDetail"] = StringResources.BridgeTrustedProxyLoopbackDetail,
            ["bridgeMixedAuthSummary"] = StringResources.BridgeMixedAuthSummary,
            ["bridgeMixedAuthDetail"] = StringResources.BridgeMixedAuthDetail,
            ["bridgeTrustedProxyHeaderSummary"] = StringResources.BridgeTrustedProxyHeaderSummary,
            ["bridgeTrustedProxyHeaderDetail"] = StringResources.BridgeTrustedProxyHeaderDetail,
            ["bridgeTrustedProxyOriginSummary"] = StringResources.BridgeTrustedProxyOriginSummary,
            ["bridgeTrustedProxyOriginDetail"] = StringResources.BridgeTrustedProxyOriginDetail,
            ["bridgeRateLimitedSummary"] = StringResources.BridgeRateLimitedSummary,
            ["bridgeRateLimitedDetail"] = StringResources.BridgeRateLimitedDetail,
            ["bridgeInsecureHttpSummary"] = StringResources.BridgeInsecureHttpSummary,
            ["bridgeInsecureHttpDetail"] = StringResources.BridgeInsecureHttpDetail,
            ["bridgePairingSummary"] = StringResources.BridgePairingSummary,
            ["bridgePairingDetail"] = StringResources.BridgePairingDetail,
            ["bridgeAuthRequiredSummary"] = StringResources.BridgeAuthRequiredSummary,
            ["bridgeAuthRequiredDetail"] = StringResources.BridgeAuthRequiredDetail,
            ["bridgeGatewaySessionNotConnectedSummary"] = StringResources.BridgeGatewaySessionNotConnectedSummary,
            ["bridgeGatewaySessionNotConnectedDetail"] = StringResources.BridgeGatewaySessionNotConnectedDetail,
            ["bridgeConnectingSummary"] = StringResources.BridgeConnectingSummary,
            ["bridgeConnectingDetail"] = StringResources.BridgeConnectingDetail,
            ["bridgeConnectedSummary"] = StringResources.BridgeConnectedSummary,
        };

        var stringsJson = JsonSerializer.Serialize(strings);

        return BridgeScriptTemplate.Value
            .Replace(StringsPlaceholder, stringsJson, StringComparison.Ordinal)
            .Replace(HostMessagingPlaceholder, HostMessagingScript.Value, StringComparison.Ordinal)
            .Replace(MutationFilterPlaceholder, MutationFilterScript.Value, StringComparison.Ordinal)
            .Replace(ModelResolverPlaceholder, ModelResolverScript.Value, StringComparison.Ordinal)
            .Replace(StatusInspectionPlaceholder, StatusInspectionScript.Value, StringComparison.Ordinal)
            .Replace(CommandDispatchPlaceholder, CommandDispatchScript.Value, StringComparison.Ordinal);
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(HostedUiBridgeScript).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {resourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
