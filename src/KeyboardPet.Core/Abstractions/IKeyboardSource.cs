namespace KeyboardPet.Core.Abstractions;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

/// <summary>
/// 키 이벤트 한 건. 개인정보 원칙에 따라 어디에도 저장·기록하지 않는다.
/// </summary>
/// <param name="VirtualKey">Windows 가상 키 코드(VK_*)</param>
/// <param name="IsDown">true = 키 다운, false = 키 업</param>
/// <param name="Modifiers">이벤트 시점의 조합키 상태</param>
/// <param name="IsAutoRepeat">키를 길게 눌러 발생한 반복 이벤트인지</param>
public readonly record struct KeyEvent(
    int VirtualKey,
    bool IsDown,
    KeyModifiers Modifiers,
    bool IsAutoRepeat);

/// <summary>
/// 전역 키 이벤트 소스. App 계층에서 WH_KEYBOARD_LL 훅으로 구현한다.
/// </summary>
public interface IKeyboardSource : IDisposable
{
    event EventHandler<KeyEvent>? KeyEvent;
    bool IsRunning { get; }
    void Start();
    void Stop();
}
