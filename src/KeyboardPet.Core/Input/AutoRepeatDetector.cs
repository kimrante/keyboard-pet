namespace KeyboardPet.Core.Input;

/// <summary>
/// 저수준 키보드 훅은 키 반복(Auto-repeat) 여부를 알려주지 않으므로,
/// 키 다운/업 순서와 시간 간격으로 반복 여부를 추정한다.
/// 키 업을 놓친 경우(관리자 권한 창으로 포커스 이동 등)를 대비해
/// 마지막 키 다운으로부터 <see cref="RepeatWindow"/> 이상 지났으면 새 입력으로 본다.
/// </summary>
public sealed class AutoRepeatDetector
{
    private readonly Dictionary<int, long> _lastDownTimeMs = new();

    public AutoRepeatDetector(TimeSpan? repeatWindow = null)
    {
        RepeatWindow = repeatWindow ?? TimeSpan.FromMilliseconds(1000);
    }

    public TimeSpan RepeatWindow { get; }

    /// <summary>
    /// 키 다운 이벤트를 기록하고, 반복 입력이면 true를 반환한다.
    /// 훅의 타임스탬프는 부팅 후 밀리초(uint)라 약 49.7일마다 0으로 돌아가므로,
    /// 이전 값보다 작은 타임스탬프는 새 입력으로 본다.
    /// </summary>
    public bool OnKeyDown(int virtualKey, long timestampMs)
    {
        var isRepeat = _lastDownTimeMs.TryGetValue(virtualKey, out var last)
                       && timestampMs >= last
                       && timestampMs - last < RepeatWindow.TotalMilliseconds;
        _lastDownTimeMs[virtualKey] = timestampMs;
        return isRepeat;
    }

    public void OnKeyUp(int virtualKey) => _lastDownTimeMs.Remove(virtualKey);

    public void Reset() => _lastDownTimeMs.Clear();
}
