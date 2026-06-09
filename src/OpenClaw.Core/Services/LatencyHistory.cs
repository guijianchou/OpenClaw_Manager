// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public sealed class LatencyHistory
{
    private readonly int _capacity;
    private readonly Queue<long> _samples = new();

    public LatencyHistory(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
    }

    public void Record(ControlUiLatencySnapshot snapshot)
    {
        if (!snapshot.IsSuccess || snapshot.RoundtripTimeMs is not long roundtripTimeMs)
        {
            return;
        }

        _samples.Enqueue(roundtripTimeMs);
        while (_samples.Count > _capacity)
        {
            _samples.Dequeue();
        }
    }

    public void Clear()
    {
        _samples.Clear();
    }

    public LatencyHistorySummary CreateSummary()
    {
        if (_samples.Count == 0)
        {
            return LatencyHistorySummary.Empty;
        }

        var values = _samples.ToArray();
        Array.Sort(values);

        var latest = _samples.Last();
        var average = (long)Math.Round(_samples.Average(), MidpointRounding.AwayFromZero);
        var p95Index = Math.Clamp((int)Math.Ceiling(values.Length * 0.95d) - 1, 0, values.Length - 1);

        return new LatencyHistorySummary(
            _samples.Count,
            latest,
            values[0],
            average,
            values[p95Index],
            values[^1]);
    }
}

public readonly record struct LatencyHistorySummary(
    int SampleCount,
    long? LatestMs,
    long? MinMs,
    long? AverageMs,
    long? P95Ms,
    long? MaxMs)
{
    public static LatencyHistorySummary Empty => new(0, null, null, null, null, null);
}
