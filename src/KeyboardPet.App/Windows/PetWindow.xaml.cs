using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KeyboardPet.App.Services;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.Windows;

public partial class PetWindow : Window
{
    private const double EdgeMargin = 24;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private readonly ShellViewModel _shell;
    private readonly SettingsService _settings;

    /// <summary>
    /// true인 동안은 크기가 바뀔 때마다 작업 영역 우하단에 자동 정렬한다.
    /// 저장된 위치가 있거나 사용자가 드래그로 옮기면 false가 된다.
    /// </summary>
    private bool _autoAnchor = true;

    public PetWindow(ShellViewModel shell, SettingsService settings)
    {
        InitializeComponent();
        _shell = shell;
        _settings = settings;
        DataContext = shell;
        ContextMenu = ContextMenuFactory.Create(shell);

        var saved = settings.Current.Window;
        if (saved.X is double x && saved.Y is double y && IsOnScreen(x, y))
        {
            Left = x;
            Top = y;
            _autoAnchor = false;
        }
        else
        {
            // 바인딩은 DataContext 설정 직후가 아니라 Dispatcher의 DataBind 단계에서 반영되므로
            // Loaded 시점의 ActualWidth/Height는 빈 콘텐츠 기준이다. 위치는 SizeChanged에서 맞추되,
            // 첫 프레임의 깜빡임을 줄이기 위해 뷰모델이 아는 프레임 크기로 초기 위치를 먼저 추정한다.
            AnchorToBottomRight(shell.FrameWidth + 16, shell.FrameHeight + 48);
        }

        SizeChanged += OnSizeChanged;
        SourceInitialized += (_, _) => ApplyClickThrough(_shell.ClickThrough);
        _shell.PropertyChanged += OnShellPropertyChanged;
        _settings.Changed += OnSettingsChanged;

        // 전체화면 앱이나 다른 Topmost 창이 위로 올라오면 WPF의 Topmost만으로는 밀릴 수 있어 주기적으로 재적용한다.
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        _topmostTimer.Start();
    }

    private readonly DispatcherTimer _topmostTimer;

    private void ReassertTopmost()
    {
        if (!_shell.IsTopmost || !IsVisible)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_autoAnchor)
        {
            AnchorToBottomRight(e.NewSize.Width, e.NewSize.Height);
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ClickThrough))
        {
            ApplyClickThrough(_shell.ClickThrough);
        }
    }

    private void OnSettingsChanged(AppSettings old, AppSettings @new)
    {
        // 설정 창의 "위치 초기화": 저장된 좌표가 사라지면 다시 우하단 자동 정렬로 돌아간다.
        if (old.Window.X is not null && @new.Window.X is null)
        {
            _autoAnchor = true;
            AnchorToBottomRight(ActualWidth, ActualHeight);
        }
    }

    private void AnchorToBottomRight(double width, double height)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - width - EdgeMargin;
        Top = area.Bottom - height - EdgeMargin;
    }

    private static bool IsOnScreen(double x, double y)
    {
        // 저장된 위치가 현재 모니터 구성 밖(분리된 모니터 등)이면 무시한다.
        const double slack = 32;
        var left = SystemParameters.VirtualScreenLeft - slack;
        var top = SystemParameters.VirtualScreenTop - slack;
        var right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - slack;
        var bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - slack;
        return x >= left && x <= right && y >= top && y <= bottom;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        _autoAnchor = false;
        DragMove();

        var (left, top) = (Left, Top);
        _settings.Update(s => s with { Window = s.Window with { X = left, Y = top } });
    }

    private void ApplyClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle = enabled ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    // x64 전용 빌드이므로 *Ptr 변형만 사용한다.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
