using System.Diagnostics;
using System.Windows.Media;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Effects;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.Services;

/// <summary>효과 계산에 쓰는 시계(ms). 스크린샷 모드는 <see cref="Override"/>로 특정 순간을 고정한다.</summary>
public sealed class EffectClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _offset;

    /// <summary>값이 있으면 실제 시계 대신 이 시각(ms)을 쓴다.</summary>
    public double? Override { get; set; }

    public double NowMs => Override ?? _stopwatch.Elapsed.TotalMilliseconds + _offset;

    /// <summary>고정을 풀되 시각이 뒤로 가지 않도록, 고정했던 시각에서 이어서 흐르게 한다.</summary>
    public void Release()
    {
        if (Override is double frozen)
        {
            _offset = frozen - _stopwatch.Elapsed.TotalMilliseconds;
            Override = null;
        }
    }
}

/// <summary>
/// 사용 중인 세트의 프레임 효과와 활성 키 규칙의 효과를 합성해 펫 이미지 변형(<see cref="TransformChanged"/>)을 낸다.
/// 지금 프레임에서 움직일 효과가 있을 때만 화면 갱신(CompositionTarget.Rendering)에 붙어 돌고, 없으면 멈춰서 CPU를 쓰지 않는다.
/// </summary>
public sealed class EffectService : IDisposable
{
    private readonly AnimationService _animation;
    private readonly SettingsService _settings;
    private readonly ShellViewModel _shell;
    private readonly EffectClock _clock;
    private readonly EffectMixer _mixer = new();
    private bool _running;
    private bool _suspended;

    public EffectService(AnimationService animation, SettingsService settings, ShellViewModel shell, EffectClock clock)
    {
        _animation = animation;
        _settings = settings;
        _shell = shell;
        _clock = clock;
    }

    /// <summary>새 변형. 항상 UI 스레드에서 발생한다.</summary>
    public event Action<EffectTransform>? TransformChanged;

    public EffectTransform Current { get; private set; } = EffectTransform.Identity;

    public void Initialize()
    {
        Configure(_settings.Current);
        _settings.Changed += OnSettingsChanged;
        _animation.RuleActivated += OnRuleActivated;
        _animation.FrameChanged += OnFrameChanged;
        EnsureRunning();
    }

    /// <summary>펫 창이 숨겨졌거나 세션이 잠긴 동안 계산을 멈춘다. 풀리면 필요할 때 다시 돈다.</summary>
    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended)
        {
            return;
        }

        _suspended = suspended;
        if (suspended)
        {
            Stop();
        }
        else
        {
            EnsureRunning();
        }
    }

    /// <summary>지금 시각으로 한 번 계산해 반영한다.</summary>
    public void Tick()
    {
        var frame = _animation.CurrentFrameIndex;
        var ruleEffects = _animation.ActiveRule?.Effects;
        Publish(_mixer.Sample(_clock.NowMs, frame, ruleEffects));

        if (_mixer.IsIdle(frame, ruleEffects))
        {
            Stop();
            Publish(EffectTransform.Identity);
        }
    }

    public void Dispose()
    {
        Stop();
        _settings.Changed -= OnSettingsChanged;
        _animation.RuleActivated -= OnRuleActivated;
        _animation.FrameChanged -= OnFrameChanged;
    }

    private void OnSettingsChanged(AppSettings old, AppSettings @new)
    {
        var effectsChanged = !FrameEffect.ListsEqual(old.EffectiveEffects, @new.EffectiveEffects);
        if (effectsChanged || !AppSettings.RulesEqual(old.EffectiveRules, @new.EffectiveRules))
        {
            // 규칙만 바뀌었으면 여백만 다시 잡는다(세트 효과의 진행 중인 움직임을 끊지 않도록).
            Configure(@new, reconfigureMixer: effectsChanged);
            EnsureRunning();
        }
    }

    private void OnRuleActivated(KeyRule rule)
    {
        _mixer.RestartRule();
        EnsureRunning();
    }

    private void OnFrameChanged(int frameIndex) => EnsureRunning();

    private void Configure(AppSettings s, bool reconfigureMixer = true)
    {
        if (reconfigureMixer)
        {
            _mixer.Configure(s.EffectiveEffects);
        }

        // 펫 창이 효과를 잘라내지 않도록, 세트 효과와 규칙 효과가 한꺼번에 움직일 때의 여백을 둔다.
        var all = s.EffectiveEffects.Concat(s.EffectiveRules.SelectMany(r => r.Effects ?? Array.Empty<FrameEffect>()));
        _shell.EffectPadding = EffectPadding.For(all);
    }

    private void EnsureRunning()
    {
        if (_running || _suspended || _mixer.IsIdle(_animation.CurrentFrameIndex, _animation.ActiveRule?.Effects))
        {
            return;
        }

        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (_running)
        {
            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e) => Tick();

    private void Publish(EffectTransform transform)
    {
        if (transform == Current)
        {
            return;
        }

        Current = transform;
        TransformChanged?.Invoke(transform);
    }
}
