using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Infrastructure.Observability;

/// <summary>Thread-safe count/sum/min/max accumulator backing a <see cref="DurationSummary"/>.</summary>
internal sealed class DurationAccumulator
{
    private readonly object _gate = new();
    private long _count;
    private double _sum;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    public void Record(double value)
    {
        lock (_gate)
        {
            _count++;
            _sum += value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;
        }
    }

    public DurationSummary? ToSummary()
    {
        lock (_gate)
            return _count == 0 ? null : new DurationSummary(_count, _min, _max, _sum / _count);
    }
}
