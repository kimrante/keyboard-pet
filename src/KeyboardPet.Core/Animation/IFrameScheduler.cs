namespace KeyboardPet.Core.Animation;

/// <summary>
/// "언제 다음 프레임으로 넘어갈지"를 결정하는 정책.
/// 프레임 인덱스 자체는 <see cref="AnimationEngine"/>이 관리하고,
/// 스케줄러는 생성 시 받은 콜백을 호출해 전진을 요청한다.
/// </summary>
public interface IFrameScheduler : IDisposable
{
    bool IsRunning { get; }

    void Start();

    void Stop();

    /// <summary>키 다운(반복 제외) 1회. 타이머 기반 스케줄러는 무시한다.</summary>
    void OnKeystroke();
}
