using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KeyboardPet.App.Services;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Effects;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.Windows;

public partial class PetWindow : Window
{
    private const double EdgeMargin = 24;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private readonly ShellViewModel _shell;
    private readonly SettingsService _settings;
    private readonly EffectService _effects;
    private Thickness _effectMargin;
    private (double Width, double Height) _frameSize;

    /// <summary>
    /// true인 동안은 크기가 바뀔 때마다 작업 영역 우하단에 자동 정렬한다.
    /// 저장된 위치가 있거나 사용자가 드래그로 옮기면 false가 된다.
    /// </summary>
    private bool _autoAnchor = true;

    public PetWindow(ShellViewModel shell, SettingsService settings, EffectService effects)
    {
        InitializeComponent();
        _shell = shell;
        _settings = settings;
        _effects = effects;
        DataContext = shell;
        ContextMenu = ContextMenuFactory.Create(shell);

        // 저장된 위치는 이미지의 왼쪽 위다(효과 여백 제외). 여백이 없으면 창 위치와 같다.
        _effectMargin = shell.EffectMargin;
        var saved = settings.Current.Window;
        if (saved.X is double x && saved.Y is double y && IsOnScreen(x, y))
        {
            Left = x - _effectMargin.Left;
            Top = y - _effectMargin.Top;
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
        ContentRendered += (_, _) => DiagnosticsLog.Trace($"펫 창 렌더링 완료: 위치 ({Left:0},{Top:0}) 크기 {ActualWidth:0}x{ActualHeight:0}, 작업 영역 {SystemParameters.WorkArea}");
        _shell.PropertyChanged += OnShellPropertyChanged;
        _settings.Changed += OnSettingsChanged;
        _effects.TransformChanged += ApplyEffect;
        ApplyEffect(_effects.Current);

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
        else if (e.PropertyName == nameof(ShellViewModel.EffectMargin))
        {
            // 여백이 바뀌어도 이미지가 화면에서 제자리에 있도록 창을 반대로 옮긴다(자동 정렬 중이면 SizeChanged가 맞춘다).
            var margin = _shell.EffectMargin;
            if (!_autoAnchor)
            {
                Left -= margin.Left - _effectMargin.Left;
                Top -= margin.Top - _effectMargin.Top;
            }

            _effectMargin = margin;
        }
        else if (e.PropertyName is nameof(ShellViewModel.FrameWidth) or nameof(ShellViewModel.FrameHeight))
        {
            // 이동량은 이미지 크기에 비례하므로 크기가 실제로 바뀐 프레임에서만 다시 적용한다(프레임마다 두 번 통지됨).
            var size = (_shell.FrameWidth, _shell.FrameHeight);
            if (size != _frameSize)
            {
                _frameSize = size;
                ApplyEffect(_effects.Current);
            }
        }
    }

    /// <summary>효과 변형을 이미지에 적용한다. 이동은 이미지 크기에 대한 비율이므로 표시 크기를 곱한다.</summary>
    private void ApplyEffect(EffectTransform t)
    {
        _frameSize = (_shell.FrameWidth, _shell.FrameHeight);
        EffectScale.ScaleX = t.ScaleX;
        EffectScale.ScaleY = t.ScaleY;
        EffectRotate.Angle = t.Angle;
        EffectTranslate.X = t.OffsetX * _shell.FrameWidth;
        EffectTranslate.Y = t.OffsetY * _shell.FrameHeight;
        FrameImage.Opacity = t.Opacity;
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

        var before = (Left, Top);
        try
        {
            // 빠른 클릭-해제 뒤에 핸들러가 돌면 버튼이 이미 떼어져 있어 DragMove가 예외를 던진다.
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var (left, top) = (Left, Top);
        if (left == before.Left && top == before.Top)
        {
            return; // 움직이지 않은 클릭은 설정을 건드리지 않는다.
        }

        _autoAnchor = false;
        _settings.Update(s => s with { Window = s.Window with { X = left + _effectMargin.Left, Y = top + _effectMargin.Top } });
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
