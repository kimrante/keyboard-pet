using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Animation;

public sealed class FixedIntervalScheduler : IFrameScheduler
{
    private readonly IFrameTimer _timer;
    private readonly Action _advance;

    public FixedIntervalScheduler(IFrameTimerFactory timers, TimeSpan interval, Action advance)
    {
        _advance = advance;
        _timer = timers.Create();
        _timer.Interval = interval;
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsRunning;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void OnKeystroke()
    {
        // 타이머 기반: 키 입력은 프레임 전환에 영향 없음
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }

    private void OnTick() => _advance();
}
