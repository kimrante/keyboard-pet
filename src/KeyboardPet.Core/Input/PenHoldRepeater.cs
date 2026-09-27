using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Input;

/// <summary>
/// 펜을 누르고 있는 동안 일정 간격마다 입력 한 번(<c>stroke</c>)을 낸다. 누르는 순간 한 번, 그 뒤 <see cref="Interval"/>마다 한 번.
/// 타수 기반·타이핑 속도 연동 모드에서 펜을 대고 있기만 해도 프레임이 계속 넘어가게 하기 위한 것이다.
/// 뗌 이벤트를 놓쳐도 계속 돌지 않도록 틱마다 <c>isStillDown</c>으로 확인한다. UI 스레드 단일 소유를 전제로 한다.
/// </summary>
public sealed class PenHoldRepeater : IDisposable
{
    public const int MinIntervalMs = 30;
    public const int MaxIntervalMs = 1000;
    public const int DefaultIntervalMs = 150;

    private readonly IFrameTimer _timer;
    private readonly Func<bool> _isStillDown;
    private readonly Action _stroke;
    private TimeSpan _interval = TimeSpan.FromMilliseconds(DefaultIntervalMs);

    public PenHoldRepeater(IFrameTimerFactory timers, Func<bool> isStillDown, Action stroke)
    {
        _isStillDown = isStillDown;
        _stroke = stroke;
        _timer = timers.Create();
        _timer.Interval = _interval;
        _timer.Tick += OnTick;
    }

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            var ms = Math.Clamp(value.TotalMilliseconds, MinIntervalMs, MaxIntervalMs);
            _interval = TimeSpan.FromMilliseconds(ms);
            _timer.Interval = _interval;
        }
    }

    public bool IsHolding => _timer.IsRunning;

    public void PenDown()
    {
        _stroke();
        _timer.Stop();
        _timer.Start();
    }

    public void PenUp() => _timer.Stop();

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }

    private void OnTick()
    {
        if (!_isStillDown())
        {
            _timer.Stop();
            return;
        }

        _stroke();
    }
}
