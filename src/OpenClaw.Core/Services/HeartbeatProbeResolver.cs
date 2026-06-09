// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public static class HeartbeatProbeResolver
{
    public static HeartbeatProbeResult Resolve(
        HeartbeatProbeResult? hostedSessionResult,
        HeartbeatProbeResult transportResult)
    {
        if (hostedSessionResult is null)
        {
            return transportResult;
        }

        if (hostedSessionResult.Status is HeartbeatProbeStatus.Healthy or HeartbeatProbeStatus.SessionBlocked)
        {
            return hostedSessionResult;
        }

        return transportResult.Status switch
        {
            HeartbeatProbeStatus.Healthy => hostedSessionResult with
            {
                Message = CombineMessages(hostedSessionResult.Message, transportResult.Message),
            },
            HeartbeatProbeStatus.SessionBlocked => transportResult with
            {
                Message = CombineMessages(hostedSessionResult.Message, transportResult.Message),
            },
            HeartbeatProbeStatus.Failure when hostedSessionResult.Status == HeartbeatProbeStatus.Connecting =>
                hostedSessionResult with
                {
                    Message = CombineMessages(hostedSessionResult.Message, transportResult.Message),
                },
            _ => hostedSessionResult with
            {
                Message = CombineMessages(hostedSessionResult.Message, transportResult.Message),
            },
        };
    }

    private static string CombineMessages(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return $"{first} {second}";
    }
}
