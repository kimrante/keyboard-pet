using System.Windows.Threading;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Input;
using KeyboardPet.Core.Settings;
using Microsoft.Win32;

namespace KeyboardPet.App.Services;

/// <summary>
/// 마우스 클릭·펜 입력을 키 입력처럼 앱 상태(타수, 애니메이션)에 반영한다. 키 매핑 규칙에는 쓰이지 않는다.
/// 설정에서 둘 다 꺼져 있으면 훅을 설치하지 않는다(마우스를 움직일 때마다 콜백이 불리지 않도록).
/// 펜을 대고 있는 동안에는 <see cref="PenHoldRepeater"/>가 일정 간격마다 입력 한 번을 더해 프레임이 계속 넘어가게 한다.
/// </summary>
public sealed class PointerInputService : IDisposable
{
    private readonly IPointerSource _source;
    private readonly ShellViewModel _shell;
    private readonly AnimationService _animation;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly PenHoldRepeater _repeater;
    private PointerSourceOptions _applied;
    private bool _started;

    public PointerInputService(
        IPointerSource source,
        ShellViewModel shell,
        AnimationService animation,
        SettingsService settings,
        IFrameTimerFactory timers)
    {
        _source = source;
        _shell = shell;
        _animation = animation;
        _settings = settings;
        _repeater = new PenHoldRepeater(timers, () => _source.IsPenDown, _animation.OnInputStroke);
    }

    /// <summary>마지막으로 훅을 켜지 못한 이유. 정상이면 null.</summary>
    public string? LastError { get; private set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _source.Input += OnInput;
        _settings.Changed += OnSettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Apply(_settings.Current, force: true);
    }

    public void Dispose()
    {
        if (_started)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _settings.Changed -= OnSettingsChanged;
            _source.Input -= OnInput;
        }

        _repeater.Dispose();
        _source.Stop();
    }

    private void OnSettingsChanged(AppSettings old, AppSettings @new) => Apply(@new, force: false);

    private void Apply(AppSettings s, bool force)
    {
        _repeater.Interval = TimeSpan.FromMilliseconds(s.PenRepeatMs);

        var options = new PointerSourceOptions(s.CountMouseClicks, s.CountPenInput);
        if (!force && options == _applied)
        {
            return;
        }

        _applied = options;
        _repeater.PenUp();
        try
        {
            if (options.Any)
            {
                _source.Start(options);
            }
            else
            {
                _source.Stop();
            }

            LastError = null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            LastError = ex.Message;
            DiagnosticsLog.Write("마우스·펜 입력 감지 시작 실패", ex);
        }
    }

    private void OnInput(object? sender, PointerInput e)
    {
        switch (e.Kind)
        {
            case PointerInputKind.Click when _applied.Clicks:
                _shell.KeystrokeCount++;
                _animation.OnInputStroke();
                break;

            case PointerInputKind.PenDown when _applied.Pen:
                _shell.KeystrokeCount++;
                _repeater.PenDown();   // 누르는 순간 한 번, 대고 있는 동안 간격마다 한 번
                break;

            case PointerInputKind.PenUp:
                _repeater.PenUp();
                break;
        }
    }

    /// <summary>절전 복귀·잠금 해제 뒤에는 저수준 훅이 끊길 수 있어 다시 설치한다(키보드 훅과 같은 이유).</summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            ScheduleReinstall();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
        {
            ScheduleReinstall();
        }
    }

    private void ScheduleReinstall() =>
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_source.IsRunning)
            {
                Apply(_settings.Current, force: true);
            }
        });
}
