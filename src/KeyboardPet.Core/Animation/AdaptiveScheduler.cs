using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Animation;

/// <summary>
/// 최근 <c>window</c> 동안의 타수로 타이핑 속도(타/초)를 구해, 프레임 간격을
/// 느린 간격(속도 0)과 빠른 간격(목표 속도 이상) 사이에서 선형 보간한다.
/// 입력이 <c>idleReturn</c> 이상 없으면 첫 프레임으로 돌아가 멈추고, 다음 키 입력에 다시 움직인다.
/// idleReturn이 0이면 입력이 없어도 느린 간격으로 계속 순환한다.
/// </summary>
public sealed class AdaptiveScheduler : IFrameScheduler
{
    private readonly IFrameTimer _timer;
    private readonly IClock _clock;
    private readonly Action _advance;
    private readonly Action _returnToIdle;
    private readonly double _slowMs;
    private readonly double _fastMs;
    private readonly double _targetKeysPerSecond;
    private readonly long _windowMs;
    private readonly long _idleReturnMs;
    private readonly Queue<long> _keystrokes = new();
    private long _lastKeystrokeMs;
    private bool _isIdle;

    public AdaptiveScheduler(
        IFrameTimerFactory timers,
        IClock clock,
        TimeSpan slow,
        TimeSpan fast,
        double targetKeysPerSecond,
        TimeSpan window,
        TimeSpan idleReturn,
        Action advance,
        Action returnToIdle)
    {
        if (fast > slow)
        {
            (slow, fast) = (fast, slow);
        }

        _clock = clock;
        _slowMs = slow.TotalMilliseconds;
        _fastMs = fast.TotalMilliseconds;
        _targetKeysPerSecond = Math.Max(0.01, targetKeysPerSecond);
        _windowMs = Math.Max(1, (long)window.TotalMilliseconds);
        _idleReturnMs = Math.Max(0, (long)idleReturn.TotalMilliseconds);
        _advance = advance;
        _returnToIdle = returnToIdle;

        _timer = timers.Create();
        _timer.Interval = slow;
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsRunning;

    public bool IsIdle => _isIdle;

    public TimeSpan CurrentInterval => _timer.Interval;

    /// <summary>최근 측정 구간의 타이핑 속도(타/초).</summary>
    public double CurrentKeysPerSecond => KeysPerSecond(NowMs());

    public void Start()
    {
        var now = NowMs();
        _keystrokes.Clear();
        _lastKeystrokeMs = now - _idleReturnMs;
        // 복귀 시간이 설정되어 있으면 첫 키 입력 전까지는 정지 상태로 시작하고 복귀 프레임을 보여준다.
        _isIdle = _idleReturnMs > 0;
        _timer.Interval = TimeSpan.FromMilliseconds(_slowMs);
        _timer.Start();

        if (_isIdle)
        {
            _returnToIdle();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _keystrokes.Clear();
    }

    public void OnKeystroke()
    {
        if (!IsRunning)
        {
            return;
        }

        var now = NowMs();
        _keystrokes.Enqueue(now);
        _lastKeystrokeMs = now;
        var interval = ComputeInterval(now);

        if (_isIdle)
        {
            // 정지 상태에서 깨어날 때는 타이머 주기를 새 간격으로 즉시 다시 시작한다.
            _isIdle = false;
            _timer.Stop();
            _timer.Interval = interval;
            _timer.Start();
        }
        else
        {
            _timer.Interval = interval;
        }
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }

    private void OnTick()
    {
        var now = NowMs();

        if (_idleReturnMs > 0 && now - _lastKeystrokeMs >= _idleReturnMs)
        {
            if (!_isIdle)
            {
                _isIdle = true;
                _keystrokes.Clear();
                _timer.Interval = TimeSpan.FromMilliseconds(_slowMs);
                _returnToIdle();
            }

            return;
        }

        _advance();
        _timer.Interval = ComputeInterval(now);
    }

    private TimeSpan ComputeInterval(long now)
    {
        var t = Math.Clamp(KeysPerSecond(now) / _targetKeysPerSecond, 0.0, 1.0);
        return TimeSpan.FromMilliseconds(_slowMs + (_fastMs - _slowMs) * t);
    }

    private double KeysPerSecond(long now)
    {
        while (_keystrokes.Count > 0 && now - _keystrokes.Peek() > _windowMs)
        {
            _keystrokes.Dequeue();
        }

        return _keystrokes.Count * 1000.0 / _windowMs;
    }

    private long NowMs() => _clock.UtcNow.ToUnixTimeMilliseconds();
}
