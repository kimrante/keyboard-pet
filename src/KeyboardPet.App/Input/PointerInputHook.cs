using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyboardPet.App.Services;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Input;

namespace KeyboardPet.App.Input;

/// <summary>
/// 마우스 클릭과 펜 접촉을 전역으로 받는다. 전용 스레드의 메시지 루프에서 두 가지를 돌린다.
/// - WH_MOUSE_LL 훅: 버튼 누름만 본다(이동·휠은 곧바로 넘김). 펜·터치가 만든 마우스 메시지는 dwExtraInfo 서명으로 구분한다.
/// - 디지타이저 Raw Input(메시지 전용 창, RIDEV_INPUTSINK): 펜 팁 스위치로 "닿아 있음"을 정확히 안다.
///   Windows는 펜을 가만히 누르면 "길게 눌러 오른쪽 클릭"을 판정하느라 마우스 메시지를 늦추거나 바꾸므로 마우스 훅만으로는 부족하다.
/// UI 스레드가 아닌 전용 스레드에 두는 이유: 저수준 마우스 훅은 콜백이 늦으면 시스템 전체의 커서가 버벅인다.
/// 콜백에서는 분류만 하고 이벤트는 Dispatcher로 넘긴다. 개인정보 원칙: 좌표·대상 창은 읽지도 전달하지도 않는다.
/// </summary>
public sealed class PointerInputHook : IPointerSource
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_QUIT = 0x0012;
    private const int WM_INPUT = 0x00FF;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    // 펜·터치가 만든 마우스 메시지의 dwExtraInfo 서명(MI_WP_SIGNATURE). 0x80 비트가 서면 터치, 아니면 펜.
    private const long SignatureMask = 0xFFFFFF00;
    private const long PenOrTouchSignature = 0xFF515700;
    private const long TouchBit = 0x80;

    // MSLLHOOKSTRUCT { POINT pt; DWORD mouseData; DWORD flags; DWORD time; ULONG_PTR dwExtraInfo; } (x64: 패딩 후 24)
    private static readonly int ExtraInfoOffset = IntPtr.Size == 8 ? 24 : 20;

    // HID 디지타이저 페이지: 펜(0x02)과 외장 태블릿 같은 일반 디지타이저(0x01). 팁 스위치(0x42), 지우개(0x45).
    private const ushort DigitizerPage = 0x0D;
    private const ushort UsageDigitizer = 0x01;
    private const ushort UsagePen = 0x02;
    private const ushort UsageTipSwitch = 0x42;
    private const ushort UsageEraser = 0x45;

    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_PREPARSEDDATA = 0x20000005;
    private const int RIM_TYPEHID = 2;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private const string WindowClassName = "KeyboardPet.PointerInput";

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherOperationCallback _raise;
    private readonly object _gate = new();
    private PenContactTracker _pen = new();   // _gate로 보호

    // 네이티브에 넘기는 델리게이트는 GC되지 않도록 필드로 둔다.
    private readonly HookProc _mouseProc;
    private readonly WndProc _wndProc;

    private Thread? _thread;
    private uint _threadId;
    private PointerSourceOptions _options;
    private volatile bool _running;
    private bool _callbackErrorLogged;

    // 입력 스레드 전용
    private IntPtr _mouseHook;
    private IntPtr _window;
    private bool _rawRegistered;
    private readonly Dictionary<IntPtr, IntPtr> _preparsed = new();
    private IntPtr _rawBuffer;
    private int _rawBufferSize;
    private readonly ushort[] _usages = new ushort[64];

    public PointerInputHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _mouseProc = MouseCallback;
        _wndProc = WindowProc;
        _raise = state =>
        {
            Input?.Invoke(this, (PointerInput)state!);
            return null;
        };
    }

    public event EventHandler<PointerInput>? Input;

    public bool IsRunning => _running;

    /// <summary>디지타이저 Raw Input 등록에 성공했는지(진단용).</summary>
    public bool IsRawInputRegistered { get; private set; }

    public bool IsPenDown
    {
        get
        {
            lock (_gate)
            {
                var down = _pen.IsDown(Environment.TickCount64);

                // 대체 경로(마우스 훅)로만 알고 있다면, 버튼이 실제로 떼어졌는지 한 번 더 확인한다(뗌 메시지를 놓친 경우).
                if (down && !_pen.RawSeen && !IsPressed(VK_LBUTTON) && !IsPressed(VK_RBUTTON))
                {
                    down = false;
                }

                return down;
            }
        }
    }

    public void Start(PointerSourceOptions options)
    {
        Stop();
        if (!options.Any)
        {
            return;
        }

        _options = options;
        Exception? startError = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready, e => startError = e))
        {
            IsBackground = true,
            Name = "KeyboardPet 포인터 입력",
        };
        _thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            startError ??= new TimeoutException("포인터 입력 스레드가 시작되지 않았습니다.");
        }

        if (startError is not null)
        {
            Stop();
            throw startError;
        }

        _running = true;
    }

    public void Stop()
    {
        _running = false;
        var thread = _thread;
        if (thread is null)
        {
            return;
        }

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
    }

    public void Dispose()
    {
        Stop();
        Input = null;
    }

    // ── 입력 스레드 ──

    private void Run(ManualResetEventSlim ready, Action<Exception> fail)
    {
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);   // 메시지 큐를 먼저 만들어 WM_QUIT이 사라지지 않게 한다

            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "마우스 훅 설치에 실패했습니다.");
            }

            if (_options.Pen)
            {
                // Raw Input을 못 받아도 마우스 훅의 펜 서명으로 대신하므로 실패는 기록만 한다.
                TryRegisterRawInput();
            }

            DiagnosticsLog.Trace($"포인터 입력 시작 (클릭={_options.Clicks}, 펜={_options.Pen}, 디지타이저 Raw Input={_rawRegistered})");
        }
        catch (Exception ex)
        {
            Cleanup();
            fail(ex);
            ready.Set();
            return;
        }

        ready.Set();

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            Cleanup();
            DiagnosticsLog.Trace("포인터 입력 중지");
        }
    }

    private void TryRegisterRawInput()
    {
        var instance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClassName,
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            DiagnosticsLog.Write($"포인터 입력 창 클래스 등록 실패 (오류 {Marshal.GetLastWin32Error()}), 펜은 마우스 메시지로 인식합니다.");
            return;
        }

        _window = CreateWindowEx(0, WindowClassName, string.Empty, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            DiagnosticsLog.Write($"포인터 입력 창 생성 실패 (오류 {Marshal.GetLastWin32Error()}), 펜은 마우스 메시지로 인식합니다.");
            return;
        }

        var devices = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = DigitizerPage, usUsage = UsagePen, dwFlags = RIDEV_INPUTSINK, hwndTarget = _window },
            new RAWINPUTDEVICE { usUsagePage = DigitizerPage, usUsage = UsageDigitizer, dwFlags = RIDEV_INPUTSINK, hwndTarget = _window },
        };

        _rawRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        IsRawInputRegistered = _rawRegistered;
        if (!_rawRegistered)
        {
            DiagnosticsLog.Write($"디지타이저 Raw Input 등록 실패 (오류 {Marshal.GetLastWin32Error()}), 펜은 마우스 메시지로 인식합니다.");
        }
    }

    private void Cleanup()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_rawRegistered)
        {
            var devices = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = DigitizerPage, usUsage = UsagePen, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
                new RAWINPUTDEVICE { usUsagePage = DigitizerPage, usUsage = UsageDigitizer, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
            };
            RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _rawRegistered = false;
            IsRawInputRegistered = false;
        }

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        UnregisterClass(WindowClassName, GetModuleHandle(null));

        foreach (var data in _preparsed.Values)
        {
            Marshal.FreeHGlobal(data);
        }

        _preparsed.Clear();
        if (_rawBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBuffer = IntPtr.Zero;
            _rawBufferSize = 0;
        }

        lock (_gate)
        {
            // 다음 시작 때 이전 접촉 상태가 남지 않도록 새로 만든다.
            _pen = new PenContactTracker();
        }
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 네이티브에서 호출되므로 예외가 새면 프로세스가 죽는다. 반드시 여기서 막는다.
        try
        {
            if (nCode >= 0)
            {
                OnMouseMessage((int)wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            if (!_callbackErrorLogged)
            {
                _callbackErrorLogged = true;
                DiagnosticsLog.Write("마우스 훅 콜백 예외 (이후 동일 오류는 기록하지 않음)", ex);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void OnMouseMessage(int message, IntPtr lParam)
    {
        bool down;
        switch (message)
        {
            case WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN:
                down = true;
                break;
            case WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP:
                down = false;
                break;
            default:
                return;   // 이동·휠: 가장 흔한 메시지이므로 아무것도 읽지 않고 넘긴다
        }

        var extra = Marshal.ReadIntPtr(lParam, ExtraInfoOffset).ToInt64();
        var isPenOrTouch = (extra & SignatureMask) == PenOrTouchSignature;
        var isPen = isPenOrTouch && (extra & TouchBit) == 0;
        var now = Environment.TickCount64;

        if (isPen && _options.Pen)
        {
            bool? change;
            lock (_gate)
            {
                change = _pen.OnHookPenButton(down, now);
            }

            RaisePenChange(change);
            return;
        }

        if (!isPen)
        {
            bool? change;
            lock (_gate)
            {
                change = _pen.OnHookMouseButton(now);
            }

            RaisePenChange(change);
        }

        if (down && _options.Clicks)
        {
            Raise(PointerInputKind.Click);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_INPUT)
        {
            try
            {
                OnRawInput(lParam);
            }
            catch (Exception ex)
            {
                if (!_callbackErrorLogged)
                {
                    _callbackErrorLogged = true;
                    DiagnosticsLog.Write("디지타이저 입력 처리 예외 (이후 동일 오류는 기록하지 않음)", ex);
                }
            }
        }

        // WM_INPUT도 DefWindowProc이 정리해야 한다.
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnRawInput(IntPtr handle)
    {
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        if (GetRawInputData(handle, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        if (size > _rawBufferSize)
        {
            if (_rawBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawBuffer);
            }

            _rawBufferSize = (int)Math.Max(size, 256);
            _rawBuffer = Marshal.AllocHGlobal(_rawBufferSize);
        }

        if (GetRawInputData(handle, RID_INPUT, _rawBuffer, ref size, headerSize) == unchecked((uint)-1))
        {
            return;
        }

        // RAWINPUTHEADER { DWORD dwType; DWORD dwSize; HANDLE hDevice; WPARAM wParam; } 뒤에 RAWHID { DWORD dwSizeHid; DWORD dwCount; BYTE bRawData[]; }
        if (Marshal.ReadInt32(_rawBuffer, 0) != RIM_TYPEHID)
        {
            return;
        }

        var device = Marshal.ReadIntPtr(_rawBuffer, 8);
        var reportSize = Marshal.ReadInt32(_rawBuffer, (int)headerSize);
        var count = Marshal.ReadInt32(_rawBuffer, (int)headerSize + 4);
        var reports = _rawBuffer + (int)headerSize + 8;
        var preparsed = GetPreparsedData(device);
        if (preparsed == IntPtr.Zero || reportSize <= 0 || count <= 0)
        {
            return;
        }

        bool? tip = null;
        for (var i = 0; i < count; i++)
        {
            var length = (uint)_usages.Length;
            var status = HidP_GetUsages(0 /* HidP_Input */, DigitizerPage, 0, _usages, ref length, preparsed, reports + i * reportSize, (uint)reportSize);
            if (status != HIDP_STATUS_SUCCESS)
            {
                continue;   // 이 장치의 다른 보고(예: 설정용 보고 ID)는 건너뛴다
            }

            var down = false;
            for (var u = 0; u < length; u++)
            {
                if (_usages[u] is UsageTipSwitch or UsageEraser)
                {
                    down = true;
                    break;
                }
            }

            tip = down;
        }

        if (tip is null)
        {
            return;
        }

        bool? change;
        lock (_gate)
        {
            change = _pen.OnRawReport(device, tip.Value, Environment.TickCount64);
        }

        RaisePenChange(change);
    }

    private IntPtr GetPreparsedData(IntPtr device)
    {
        if (_preparsed.TryGetValue(device, out var cached))
        {
            return cached;
        }

        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        var data = IntPtr.Zero;
        if (size > 0)
        {
            data = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, data, ref size) == unchecked((uint)-1))
            {
                Marshal.FreeHGlobal(data);
                data = IntPtr.Zero;
            }
        }

        _preparsed[device] = data;   // 실패도 기억해 보고마다 다시 묻지 않는다
        return data;
    }

    private void RaisePenChange(bool? change)
    {
        if (change is bool down)
        {
            Raise(down ? PointerInputKind.PenDown : PointerInputKind.PenUp);
        }
    }

    private void Raise(PointerInputKind kind) =>
        _dispatcher.BeginInvoke(DispatcherPriority.Input, _raise, new PointerInput(kind));

    private static bool IsPressed(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // ── Win32 ──

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [In, Out] ushort[] usageList,
        ref uint usageLength, IntPtr preparsedData, IntPtr report, uint reportLength);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
