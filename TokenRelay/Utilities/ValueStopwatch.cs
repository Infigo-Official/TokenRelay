using System.Diagnostics;

namespace TokenRelay.Utilities;

/// <summary>
/// A lightweight, allocation-free stopwatch using a struct instead of class.
/// Useful for high-throughput scenarios where frequent timing is needed.
/// Based on the pattern used in ASP.NET Core internals.
/// </summary>
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Gets a value indicating whether the stopwatch is active (has been started).
    /// </summary>
    public bool IsActive => _startTimestamp != 0;

    /// <summary>
    /// Starts a new ValueStopwatch.
    /// </summary>
    /// <returns>A new ValueStopwatch instance that has been started.</returns>
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    /// <summary>
    /// Gets the elapsed time since the stopwatch was started.
    /// </summary>
    /// <returns>The elapsed TimeSpan.</returns>
    public TimeSpan GetElapsedTime()
    {
        // Start timestamp can't be zero in an initialized ValueStopwatch.
        // It would have to be literally the first thing executed when the machine boots.
        if (!IsActive)
        {
            throw new InvalidOperationException("ValueStopwatch has not been started. Call StartNew() first.");
        }

        var end = Stopwatch.GetTimestamp();
        var timestampDelta = end - _startTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }

    /// <summary>
    /// Gets the elapsed time in milliseconds since the stopwatch was started.
    /// </summary>
    /// <returns>The elapsed time in milliseconds.</returns>
    public long GetElapsedMilliseconds()
    {
        return (long)GetElapsedTime().TotalMilliseconds;
    }
}
