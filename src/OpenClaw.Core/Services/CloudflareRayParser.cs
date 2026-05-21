// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

/// <summary>
/// Parses Cloudflare cf-ray header values to extract the PoP (Point of Presence) code.
/// Format: {hex-ray-id}-{POP} e.g. "8a1b2c3d4e5f6789-LAX"
/// </summary>
public static class CloudflareRayParser
{
    /// <summary>
    /// Extracts the 3-letter PoP code from a cf-ray header value.
    /// Returns null if the value is missing, malformed, or the PoP code is not exactly 3 letters.
    /// </summary>
    public static string? ParsePoP(string? cfRayValue)
    {
        if (string.IsNullOrWhiteSpace(cfRayValue))
        {
            return null;
        }

        var lastDash = cfRayValue.LastIndexOf('-');
        if (lastDash < 0 || lastDash >= cfRayValue.Length - 1)
        {
            return null;
        }

        var pop = cfRayValue[(lastDash + 1)..].Trim();
        if (pop.Length != 3)
        {
            return null;
        }

        // Validate all letters
        foreach (var ch in pop)
        {
            if (!char.IsLetter(ch))
            {
                return null;
            }
        }

        return pop.ToUpperInvariant();
    }
}
