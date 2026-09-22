using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Input;
using Microsoft.Win32;

namespace KeyboardPet.App.Input;

/// <summary>
/// WH_KEYBOARD_LL 전역 훅으로 키 이벤트를 수신한다.
/// 훅 콜백은 훅을 설치한 스레드(UI 스레드)의 메시지 루프에서 호출되며,
/// Windows가 느린 훅을 자동 제거하므로 콜백 안에서는 최소 작업만 하고
/// 실제 이벤트 전파는 Dispatcher 큐로 넘긴다.
/// 절전 복귀·세션 잠금 해제 뒤에는 훅이 끊길 수 있어 자동으로 다시 설치한다.
/// 개인정보 원칙: 키 코드는 이벤트 인자로만 전달하고 어디에도 기록하지 않는다.
/// </summary>
public sealed class LowLevelKeyboardHook : IKeyboardSource
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly Dispatcher _dispatcher;
    private readonly AutoRepeatDetector _repeatDetector = new();

    // GC가 델리게이트를 수거하지 않도록 필드로 보관한다.
    private readonly HookProc _hookProc;
    private IntPtr _hookHandle;
    private bool _systemEventsSubscribed;

    public LowLevelKeyboardHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _hookProc = HookCallback;
    }

    public event EventHandler<KeyEvent>? KeyEvent;

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("훅은 메시지 루프를 가진 UI 스레드에서 설치해야 합니다.");
        }

        if (IsRunning)
        {
            return;
        }

        _repeatDetector.Reset();
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "키보드 훅 설치에 실패했습니다.");
        }

        if (!_systemEventsSubscribed)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _systemEventsSubscribed = true;
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_systemEventsSubscribed)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _systemEventsSubscribed = false;
        }

        Stop();
        KeyEvent = null;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            ScheduleReinstall("절전 복귀");
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
        {
            ScheduleReinstall(e.Reason.ToString());
        }
    }

    /// <summary>SystemEvents는 별도 스레드에서 올 수 있으므로 UI 스레드로 넘겨서 훅을 다시 건다.</summary>
    private void ScheduleReinstall(string reason)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!IsRunning)
            {
                return;
            }

            try
            {
                Stop();
                Start();
                Debug.WriteLine($"[KeyboardPet] 키보드 훅 재설치 ({reason})");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Debug.WriteLine($"[KeyboardPet] 키보드 훅 재설치 실패 ({reason}): {ex.Message}");
            }
        });
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vk = (int)data.vkCode;

            bool? isDown = message switch
            {
                WM_KEYDOWN or WM_SYSKEYDOWN => true,
                WM_KEYUP or WM_SYSKEYUP => false,
                _ => null,
            };

            if (isDown is not null)
            {
                bool isRepeat;
                if (isDown.Value)
                {
                    isRepeat = _repeatDetector.OnKeyDown(vk, data.time);
                }
                else
                {
                    _repeatDetector.OnKeyUp(vk);
                    isRepeat = false;
                }

                var keyEvent = new KeyEvent(vk, isDown.Value, ReadModifiers(), isRepeat);
                _dispatcher.BeginInvoke(DispatcherPriority.Input, () => KeyEvent?.Invoke(this, keyEvent));
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static KeyModifiers ReadModifiers()
    {
        var mods = KeyModifiers.None;
        if (IsPressed(VK_CONTROL)) mods |= KeyModifiers.Control;
        if (IsPressed(VK_SHIFT)) mods |= KeyModifiers.Shift;
        if (IsPressed(VK_MENU)) mods |= KeyModifiers.Alt;
        if (IsPressed(VK_LWIN) || IsPressed(VK_RWIN)) mods |= KeyModifiers.Win;
        return mods;
    }

    private static bool IsPressed(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
