using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Animation;

/// <summary>
/// 프레임을 넘길 때마다 [Min, Max] 범위에서 다음 간격을 새로 뽑는다.
/// </summary>
public sealed class RandomIntervalScheduler : IFrameScheduler
{
    private readonly IFrameTimer _timer;
    private readonly Action _advance;
    private readonly Random _random;
    private readonly double _minMs;
    private readonly double _maxMs;

    public RandomIntervalScheduler(
        IFrameTimerFactory timers,
        TimeSpan min,
        TimeSpan max,
        Action advance,
        Random? random = null)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        _minMs = min.TotalMilliseconds;
        _maxMs = max.TotalMilliseconds;
        _advance = advance;
        _random = random ?? Random.Shared;

        _timer = timers.Create();
        _timer.Interval = NextInterval();
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsRunning;

    public TimeSpan CurrentInterval => _timer.Interval;

    public void Start()
    {
        _timer.Interval = NextInterval();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void OnKeystroke()
    {
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }

    private void OnTick()
    {
        _advance();
        _timer.Interval = NextInterval();
    }

    private TimeSpan NextInterval()
    {
        var ms = _minMs + _random.NextDouble() * (_maxMs - _minMs);
        return TimeSpan.FromMilliseconds(ms);
    }
}
