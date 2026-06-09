// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;

namespace OpenClaw.Services;

internal sealed partial class WebViewStatusInspector
{
    private const string ControlUiStatusMessageKind = "openclaw-control-ui-status";
    private const int MaxControlUiStatusPayloadLength = 64 * 1024;

    private static ControlUiProbeSnapshot ParseControlUiSnapshot(string json, bool allowStringEnvelope = false)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxControlUiStatusPayloadLength)
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String && allowStringEnvelope)
            {
                var nested = root.GetString();
                if (string.IsNullOrWhiteSpace(nested) || nested.Length > MaxControlUiStatusPayloadLength)
                {
                    return ControlUiProbeSnapshot.Unknown;
                }

                using var nestedDocument = JsonDocument.Parse(nested);
                return ParseControlUiSnapshot(nestedDocument.RootElement);
            }

            return ParseControlUiSnapshot(root);
        }
        catch (JsonException)
        {
            return ControlUiProbeSnapshot.Unknown;
        }
    }

    private static ControlUiProbeSnapshot ParseControlUiSnapshot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        var kind = GetString(root, "kind");
        if (!string.Equals(kind, ControlUiStatusMessageKind, StringComparison.Ordinal))
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        var phase = ParsePhase(GetString(root, "phase"));
        var summary = GetString(root, "summary");
        var detail = GetString(root, "detail");
        var url = GetString(root, "url");
        var shellDetected = root.TryGetProperty("shellDetected", out var shellProperty) &&
            shellProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            shellProperty.GetBoolean();
        var isBusy = root.TryGetProperty("isBusy", out var busyProperty) &&
            busyProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            busyProperty.GetBoolean();
        var inputFocused = root.TryGetProperty("inputFocused", out var inputFocusedProperty) &&
            inputFocusedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            inputFocusedProperty.GetBoolean();
        var focusedInputHasText = root.TryGetProperty("focusedInputHasText", out var focusedInputHasTextProperty) &&
            focusedInputHasTextProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            focusedInputHasTextProperty.GetBoolean();
        var isBusyStale = root.TryGetProperty("isBusyStale", out var staleProperty) &&
            staleProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            staleProperty.GetBoolean();
        var busyStaleSeconds = root.TryGetProperty("busyStaleSeconds", out var staleSecondsProperty) &&
            staleSecondsProperty.ValueKind == JsonValueKind.Number &&
            staleSecondsProperty.TryGetInt32(out var parsedBusyStaleSeconds)
                ? parsedBusyStaleSeconds
                : 0;
        var workState = GetString(root, "workState");
        var currentModel = GetString(root, "currentModel");
        var currentModelSource = GetString(root, "currentModelSource");
        var activitySignature = GetString(root, "activitySignature");

        return new ControlUiProbeSnapshot(phase, summary, detail, url, shellDetected, isBusy, inputFocused, workState, currentModel)
        {
            FocusedInputHasText = focusedInputHasText,
            IsBusyStale = isBusyStale,
            BusyStaleSeconds = busyStaleSeconds,
            ActivitySignature = activitySignature,
            ModelSource = currentModelSource,
        };
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static ControlUiPhase ParsePhase(string value)
    {
        return value switch
        {
            "loading" => ControlUiPhase.Loading,
            "page_loaded" => ControlUiPhase.PageLoaded,
            "gateway_connecting" => ControlUiPhase.GatewayConnecting,
            "connected" => ControlUiPhase.Connected,
            "auth_required" => ControlUiPhase.AuthRequired,
            "pairing_required" => ControlUiPhase.PairingRequired,
            "origin_rejected" => ControlUiPhase.OriginRejected,
            "gateway_error" => ControlUiPhase.GatewayError,
            "unavailable" => ControlUiPhase.Unavailable,
            _ => ControlUiPhase.Unknown,
        };
    }
}
