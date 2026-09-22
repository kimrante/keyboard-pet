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

    public void Show()
    {
        if (_icon is not null)
        {
            return;
        }

        _iconHandle = LoadAppIcon() ?? CreateFallbackIcon();
        _icon = new TaskbarIcon
        {
            ToolTipText = "Keyboard Pet",
            Icon = _iconHandle,
            ContextMenu = ContextMenuFactory.Create(_shell),
            DataContext = _shell,
            LeftClickCommand = _shell.OpenSettingsCommand,
        };
        _icon.ForceCreate();
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;

        if (_iconHandle is not null)
        {
            if (_ownsIconHandle)
            {
                DestroyIcon(_iconHandle.Handle);
            }

            _iconHandle.Dispose();
            _iconHandle = null;
        }
    }

    /// <summary>앱 리소스의 app.ico에서 트레이 크기에 맞는 이미지를 고른다.</summary>
    private Icon? LoadAppIcon()
    {
        try
        {
            using var stream = Application.GetResourceStream(IconUri)?.Stream;
            if (stream is null)
            {
                return null;
            }

            _ownsIconHandle = false;
            return new Icon(stream, new System.Drawing.Size(32, 32));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>리소스를 읽지 못한 경우를 위한 예비 아이콘(GDI+로 그림).</summary>
    private Icon CreateFallbackIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(0xFF, 0xC8, 0x50));
            g.FillEllipse(fill, 1, 1, size - 2, size - 2);
        }

        // GetHicon으로 얻은 핸들은 Icon.FromHandle이 소유하지 않으므로 Dispose 시 DestroyIcon으로 해제한다.
        _ownsIconHandle = true;
        return Icon.FromHandle(bitmap.GetHicon());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
