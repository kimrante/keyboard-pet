using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Tests.Animation;

/// <summary>테스트에서 수동으로 틱을 발생시키는 타이머.</summary>
public sealed class FakeTimer : IFrameTimer
{
    public TimeSpan Interval { get; set; }

    public bool IsRunning { get; private set; }

    public bool IsDisposed { get; private set; }

    public int StartCount { get; private set; }

    public event Action? Tick;

    public void Start()
    {
        IsRunning = true;
        StartCount++;
    }

    public void Stop() => IsRunning = false;

    /// <summary>실행 중일 때만 Tick을 발생시킨다(실제 타이머와 동일).</summary>
    public void Fire()
    {
        if (IsRunning)
        {
            Tick?.Invoke();
        }
    }

    public void Dispose()
    {
        IsRunning = false;
        IsDisposed = true;
    }
}

public sealed class FakeTimerFactory : IFrameTimerFactory
{
    public List<FakeTimer> Created { get; } = new();

    public FakeTimer Last => Created[^1];

    public IFrameTimer Create()
    {
        var timer = new FakeTimer();
        Created.Add(timer);
        return timer;
    }
}
