namespace KeyboardPet.Core.Abstractions;

/// <summary>
/// 프레임 스케줄러가 사용하는 타이머 추상화.
/// App 계층에서는 DispatcherTimer로, 테스트에서는 수동 틱 타이머로 구현한다.
/// </summary>
public interface IFrameTimer : IDisposable
{
    TimeSpan Interval { get; set; }
    bool IsRunning { get; }
    event Action? Tick;
    void Start();
    void Stop();
}

public interface IFrameTimerFactory
{
    IFrameTimer Create();
}
