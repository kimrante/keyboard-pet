namespace KeyboardPet.Core.Input;

/// <summary>
/// 펜 접촉 상태를 두 경로에서 모아 "닿음/떨어짐" 전환을 낸다. 스레드 안전하지 않으므로 호출자가 잠근다.
/// - 디지타이저 Raw Input(팁 스위치): 펜을 가만히 누르고 있어도 보고가 계속 와서 정확하다.
///   Windows의 "길게 눌러 오른쪽 클릭" 판정 때문에 마우스 메시지가 늦거나 바뀌어도 영향을 받지 않는다.
/// - 마우스 훅의 펜 서명 버튼 메시지: Raw Input을 받을 수 없는 환경을 위한 대체 경로.
/// 팁이 닿았다는 Raw 보고를 한 번이라도 받으면 그 뒤로는 Raw만 믿는다(두 경로가 한 번의 누름을 두 번 세지 않도록).
/// 팁이 닿지 않았다는 보고만 오는 동안은(파싱이 맞지 않는 장치 등) 대체 경로를 그대로 쓴다.
/// </summary>
public sealed class PenContactTracker
{
    /// <summary>팁이 닿은 채 이 시간 동안 보고가 없으면 떨어진 것으로 본다(장치 분리 등으로 뗌 보고를 놓친 경우).</summary>
    public static readonly TimeSpan DefaultRawStaleAfter = TimeSpan.FromSeconds(5);

    private readonly HashSet<nint> _rawDown = new();
    private readonly long _staleAfterMs;
    private long _lastRawReportMs;
    private bool _hookDown;
    private bool _reportedDown;

    public PenContactTracker(TimeSpan? rawStaleAfter = null)
    {
        _staleAfterMs = (long)(rawStaleAfter ?? DefaultRawStaleAfter).TotalMilliseconds;
    }

    /// <summary>Raw Input으로 팁 접촉을 한 번이라도 확인했는지. true면 마우스 훅의 펜 메시지는 무시한다.</summary>
    public bool RawSeen { get; private set; }

    /// <summary>지금 닿아 있는지.</summary>
    public bool IsDown(long nowMs) =>
        RawSeen
            ? _rawDown.Count > 0 && nowMs - _lastRawReportMs < _staleAfterMs
            : _hookDown;

    /// <summary>디지타이저 보고 한 건. 전환이 있으면 true(닿음)/false(떨어짐), 없으면 null.</summary>
    public bool? OnRawReport(nint device, bool tipDown, long nowMs)
    {
        if (!RawSeen && !tipDown)
        {
            return null;   // 아직 Raw를 믿기 전: 떨어져 있다는 보고는 대체 경로의 상태를 바꾸지 않는다
        }

        var before = _reportedDown && IsDown(nowMs);   // 보고가 끊겨 이미 떨어진 것으로 본 경우도 반영
        RawSeen = true;
        _lastRawReportMs = nowMs;
        if (tipDown)
        {
            _rawDown.Add(device);
        }
        else
        {
            _rawDown.Remove(device);
        }

        return Transition(before, IsDown(nowMs));
    }

    /// <summary>마우스 훅이 본 펜 서명 버튼 메시지(팁 = 왼쪽, 배럴 버튼 = 오른쪽).</summary>
    public bool? OnHookPenButton(bool down, long nowMs)
    {
        if (RawSeen)
        {
            return null;
        }

        var before = _reportedDown;
        _hookDown = down;
        return Transition(before, IsDown(nowMs));
    }

    /// <summary>펜이 아닌 마우스 버튼 메시지. 대체 경로에서 펜 뗌을 놓쳤다면 여기서 떨어진 것으로 정리한다.</summary>
    public bool? OnHookMouseButton(long nowMs)
    {
        if (RawSeen || !_hookDown)
        {
            return null;
        }

        var before = _reportedDown;
        _hookDown = false;
        return Transition(before, IsDown(nowMs));
    }

    private bool? Transition(bool before, bool after)
    {
        _reportedDown = after;
        return before == after ? null : after;
    }
}
