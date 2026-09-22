using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Animation;

/// <summary>
/// 키 다운 N회마다 한 프레임 전진한다. 일정 시간 입력이 없으면 첫 프레임으로 복귀시킨다.
/// </summary>
public sealed class KeystrokeScheduler : IFrameScheduler
{
    private readonly IFrameTimer? _idleTimer;
    private readonly int _keysPerFrame;
    private readonly Action _advance;
    private readonly Action _returnToIdle;
    private int _pending;

    public KeystrokeScheduler(
        IFrameTimerFactory timers,
        int keysPerFrame,
        TimeSpan idleReturn,
        Action advance,
        Action returnToIdle)
    {
        _keysPerFrame = Math.Max(1, keysPerFrame);
        _advance = advance;
        _returnToIdle = returnToIdle;

        if (idleReturn > TimeSpan.Zero)
        {
            _idleTimer = timers.Create();
            _idleTimer.Interval = idleReturn;
            _idleTimer.Tick += OnIdle;
        }
    }

    public bool IsRunning { get; private set; }

    public int PendingKeystrokes => _pending;

    public void Start() => IsRunning = true;

    public void Stop()
    {
        IsRunning = false;
        _idleTimer?.Stop();
        _pending = 0;
    }

    public void OnKeystroke()
    {
        if (!IsRunning)
        {
            return;
        }

        _pending++;
        if (_pending >= _keysPerFrame)
        {
            _pending = 0;
            _advance();
        }

        // 단발성 타이머처럼 동작: 입력마다 재시작하고, 만료되면 스스로 멈춘다.
        if (_idleTimer is not null)
        {
            _idleTimer.Stop();
            _idleTimer.Start();
        }
    }

    public void Dispose()
    {
        if (_idleTimer is not null)
        {
            _idleTimer.Tick -= OnIdle;
            _idleTimer.Dispose();
        }
    }

    private void OnIdle()
    {
        _idleTimer?.Stop();
        _pending = 0;
        _returnToIdle();
    }
}
