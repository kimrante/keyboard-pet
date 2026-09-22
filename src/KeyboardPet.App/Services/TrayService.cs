using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using H.NotifyIcon;
using KeyboardPet.App.ViewModels;

namespace KeyboardPet.App.Services;

/// <summary>
/// 시스템 트레이 아이콘과 컨텍스트 메뉴를 관리한다.
/// 트레이 생성은 실패해도 앱을 멈추지 않는다: 리소스 아이콘 → GDI 대체 아이콘 순으로 시도하고,
/// 모두 실패하면 트레이 없이 계속 실행한다(메뉴는 펫 창 우클릭으로 열 수 있다).
/// </summary>
public sealed class TrayService : IDisposable
{
    private static readonly Uri IconUri = new("pack://application:,,,/Assets/app.ico");

    private readonly ShellViewModel _shell;
    private TaskbarIcon? _icon;
    private Icon? _iconHandle;
    private bool _ownsIconHandle;

    public TrayService(ShellViewModel shell)
    {
        _shell = shell;
    }

    /// <summary>트레이 아이콘이 실제로 만들어졌는지.</summary>
    public bool IsCreated => _icon is not null;

    /// <summary>트레이 아이콘을 만든다. 실패하면 로그를 남기고 false를 반환한다(예외를 던지지 않음).</summary>
    public bool Show()
    {
        if (_icon is not null)
        {
            return true;
        }

        var appIcon = LoadAppIcon();
        if (appIcon is not null && TryCreate(appIcon, ownsHandle: false, "리소스 아이콘"))
        {
            return true;
        }

        var fallback = CreateFallbackIcon();
        if (TryCreate(fallback, ownsHandle: true, "대체 아이콘"))
        {
            return true;
        }

        DiagnosticsLog.Write("트레이 아이콘을 만들지 못해 트레이 없이 계속 실행합니다. 메뉴는 펫 창을 우클릭해 열 수 있습니다.");
        return false;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
        ReleaseIconHandle();
    }

    private bool TryCreate(Icon icon, bool ownsHandle, string description)
    {
        TaskbarIcon? taskbarIcon = null;
        try
        {
            DiagnosticsLog.Trace($"트레이: TaskbarIcon 생성 시도 ({description})");
            taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "Keyboard Pet",
                Icon = icon,
                ContextMenu = ContextMenuFactory.Create(_shell),
                DataContext = _shell,
                LeftClickCommand = _shell.OpenSettingsCommand,
            };

            // enablesEfficiencyMode=false:
            // 기본값(true)은 SetProcessInformation(ProcessPowerThrottling)을 호출하고 실패하면 예외를 던지는데,
            // Windows 10 일부 환경(1709 미만 빌드, 가상 머신, 정책)에서 이 호출이 실패해 시작 직후 앱이 죽었다.
            // 게다가 프로세스 우선순위를 Idle로 낮춰 애니메이션에도 불리하다.
            DiagnosticsLog.Trace("트레이: ForceCreate 호출");
            taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
            DiagnosticsLog.Trace("트레이: 생성 완료");

            _icon = taskbarIcon;
            _iconHandle = icon;
            _ownsIconHandle = ownsHandle;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ArgumentException or NotSupportedException or ExternalException)
        {
            DiagnosticsLog.Write($"트레이 아이콘 생성 실패 ({description})", ex);
            taskbarIcon?.Dispose();
            if (ownsHandle)
            {
                DestroyIcon(icon.Handle);
            }

            icon.Dispose();
            return false;
        }
    }

    /// <summary>앱 리소스의 app.ico에서 트레이 크기에 맞는 이미지를 고른다.</summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = Application.GetResourceStream(IconUri)?.Stream;
            return stream is null ? null : new Icon(stream, new System.Drawing.Size(32, 32));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or Win32Exception or NotSupportedException)
        {
            DiagnosticsLog.Write("app.ico 로드 실패", ex);
            return null;
        }
    }

    /// <summary>리소스를 읽지 못한 경우를 위한 예비 아이콘(GDI+로 그린 검은 사각형).</summary>
    private static Icon CreateFallbackIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(0x21, 0x25, 0x29));
            g.FillRectangle(fill, 2, 8, size - 4, size - 16);
        }

        // GetHicon으로 얻은 핸들은 Icon.FromHandle이 소유하지 않으므로 해제 시 DestroyIcon이 필요하다.
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void ReleaseIconHandle()
    {
        if (_iconHandle is null)
        {
            return;
        }

        if (_ownsIconHandle)
        {
            DestroyIcon(_iconHandle.Handle);
        }

        _iconHandle.Dispose();
        _iconHandle = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
