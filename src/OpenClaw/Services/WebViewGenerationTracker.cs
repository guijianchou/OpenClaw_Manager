// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class WebViewGenerationTracker
{
    private int _generation;

    public int Current => Volatile.Read(ref _generation);

    public int Next()
    {
        return Interlocked.Increment(ref _generation);
    }

    public bool IsCurrent(int generation)
    {
        return Current == generation;
    }
}
