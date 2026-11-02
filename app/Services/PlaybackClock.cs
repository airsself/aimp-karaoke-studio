using System.Diagnostics;

namespace KaraokeStudio.Services;
/// <summary>Monotonic clock interpolated between AIMP's ~10Hz position events.</summary>
public sealed class PlaybackClock
{
    private long _anchorTicks=Stopwatch.GetTimestamp();
    private double _anchorMs;
    public bool Playing { get; private set; }
    public int PositionMs => (int)Math.Max(0, Math.Round(Predict()));
    private double Predict() => _anchorMs + (Playing ? Stopwatch.GetElapsedTime(_anchorTicks).TotalMilliseconds : 0);
    public void SetPosition(int ms)
    {
        double observed=Predict();
        // Small jitter: apply partial correction, large seek: snap immediately.
        _anchorMs=Math.Abs(ms-observed)<180 ? observed+(ms-observed)*0.3 : ms;
        _anchorTicks=Stopwatch.GetTimestamp();
    }
    public void Seek(int ms)
    {
        _anchorMs=Math.Max(0,ms);
        _anchorTicks=Stopwatch.GetTimestamp();
    }
    public void SetPlaying(bool playing)
    {
        _anchorMs=Predict();
        _anchorTicks=Stopwatch.GetTimestamp();
        Playing=playing;
    }
}
