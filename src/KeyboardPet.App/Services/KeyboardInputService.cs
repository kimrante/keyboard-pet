using System.Diagnostics;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.App.Services;

/// <summary>
/// 키 이벤트 소스를 구동하고, 키 다운 이벤트를 앱 상태(타수, 규칙 매칭, 애니메이션)로 반영한다.
/// </summary>
public sealed class KeyboardInputService : IDisposable
{
    private readonly IKeyboardSource _source;
    private readonly ShellViewModel _shell;
    private readonly AnimationService _animation;
    private readonly SettingsService _settings;

    public KeyboardInputService(
        IKeyboardSource source,
        ShellViewModel shell,
        AnimationService animation,
        SettingsService settings)
    {
        _source = source;
        _shell = shell;
        _animation = animation;
        _settings = settings;
    }

    public void Start()
    {
        _source.KeyEvent += OnKeyEvent;
        _source.Start();
    }

    public void Dispose()
    {
        _source.KeyEvent -= OnKeyEvent;
        _source.Stop();
    }

    private void OnKeyEvent(object? sender, KeyEvent e)
    {
        if (!e.IsDown)
        {
            return;
        }

        if (e.IsAutoRepeat && !_settings.Current.CountAutoRepeat)
        {
            return;
        }

        _shell.KeystrokeCount++;
        _animation.OnKeyDown(e);

        // 개인정보 원칙: 어떤 키인지는 출력하지 않는다.
        Debug.WriteLine($"[KeyboardPet] keystrokes={_shell.KeystrokeCount}");
    }
}
