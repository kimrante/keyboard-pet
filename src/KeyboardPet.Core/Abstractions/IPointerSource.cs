namespace KeyboardPet.Core.Abstractions;

/// <summary>포인터 입력 한 건의 종류.</summary>
public enum PointerInputKind
{
    /// <summary>마우스 버튼을 눌렀다(터치 탭, 펜 옵션이 꺼져 있을 때의 펜 탭 포함).</summary>
    Click,

    /// <summary>펜이 화면(태블릿)에 닿았다.</summary>
    PenDown,

    /// <summary>펜이 떨어졌다.</summary>
    PenUp,
}

/// <summary>
/// 포인터 입력 한 건. 개인정보 원칙에 따라 위치·대상 창은 담지 않고 종류만 전달한다.
/// </summary>
public readonly record struct PointerInput(PointerInputKind Kind);

/// <summary>어떤 포인터 입력을 받을지.</summary>
/// <param name="Clicks">마우스 클릭(터치 탭 포함)</param>
/// <param name="Pen">펜 접촉(누름/뗌)</param>
public readonly record struct PointerSourceOptions(bool Clicks, bool Pen)
{
    public bool Any => Clicks || Pen;
}

/// <summary>
/// 전역 포인터 입력 소스. App 계층에서 저수준 마우스 훅과 디지타이저 Raw Input으로 구현한다.
/// </summary>
public interface IPointerSource : IDisposable
{
    /// <summary>입력 이벤트. UI 스레드에서 발생한다.</summary>
    event EventHandler<PointerInput>? Input;

    bool IsRunning { get; }

    /// <summary>지금 펜이 닿아 있는지(소스가 아는 최신 상태). 누르고 있는 동안의 반복을 멈출지 판단하는 데 쓴다.</summary>
    bool IsPenDown { get; }

    /// <summary>주어진 옵션으로 시작한다. 이미 실행 중이면 새 옵션으로 다시 시작한다.</summary>
    void Start(PointerSourceOptions options);

    void Stop();
}
