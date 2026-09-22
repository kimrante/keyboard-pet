using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Rules;

/// <summary>
/// 규칙 매칭 결과에 따라 "지금 어떤 세트(또는 어떤 프레임 한 장)를 보여줘야 하는지"를 관리한다.
/// HoldMs가 있는 규칙은 타이머 만료 시 기본 세트로 복귀하고, 같은 규칙이 다시 매칭되면 타이머를 재시작한다.
/// </summary>
public sealed class KeyRuleController : IDisposable
{
    private readonly RuleMatcher _matcher;
    private readonly IFrameTimer _holdTimer;

    public KeyRuleController(IFrameTimerFactory timers, RuleMatcher matcher, string defaultFrameSet)
    {
        _matcher = matcher;
        DefaultFrameSet = defaultFrameSet;
        ActiveFrameSet = defaultFrameSet;

        _holdTimer = timers.Create();
        _holdTimer.Tick += OnHoldExpired;
    }

    public string DefaultFrameSet { get; }

    public string ActiveFrameSet { get; private set; }

    /// <summary>단일 프레임 규칙이 활성일 때 그 프레임 번호. 애니메이션 상태면 null.</summary>
    public int? ActiveFrameIndex { get; private set; }

    public KeyRule? ActiveRule { get; private set; }

    public bool IsHolding => _holdTimer.IsRunning;

    /// <summary>보여줄 대상이 바뀌어야 할 때 발생.</summary>
    public event Action<ActiveSetRequest>? ActiveSetChanged;

    /// <summary>키 다운(반복 제외) 이벤트를 넘긴다. 매칭된 규칙을 반환한다(없으면 null).</summary>
    public KeyRule? OnKeyDown(KeyEvent e)
    {
        var rule = _matcher.Match(e);
        if (rule is null)
        {
            return null;
        }

        Activate(rule);
        return rule;
    }

    public void ResetToDefault()
    {
        _holdTimer.Stop();
        ActiveRule = null;
        if (ActiveFrameSet != DefaultFrameSet || ActiveFrameIndex is not null)
        {
            ActiveFrameSet = DefaultFrameSet;
            ActiveFrameIndex = null;
            ActiveSetChanged?.Invoke(new ActiveSetRequest(DefaultFrameSet, true));
        }
    }

    public void Dispose()
    {
        _holdTimer.Tick -= OnHoldExpired;
        _holdTimer.Dispose();
        ActiveSetChanged = null;
    }

    private void Activate(KeyRule rule)
    {
        var targetChanged = ActiveFrameSet != rule.FrameSet || ActiveFrameIndex != rule.FrameIndex;
        ActiveRule = rule;
        ActiveFrameSet = rule.FrameSet;
        ActiveFrameIndex = rule.FrameIndex;

        // 같은 대상이 유지되는 경우에도 ResetIndex=true면 애니메이션을 처음부터 다시 재생한다.
        if (targetChanged || rule.ResetIndex)
        {
            ActiveSetChanged?.Invoke(new ActiveSetRequest(rule.FrameSet, rule.ResetIndex, rule.FrameIndex));
        }

        _holdTimer.Stop();
        if (rule.HoldMs > 0)
        {
            _holdTimer.Interval = TimeSpan.FromMilliseconds(rule.HoldMs);
            _holdTimer.Start();
        }
    }

    private void OnHoldExpired()
    {
        _holdTimer.Stop();
        ActiveRule = null;
        ActiveFrameSet = DefaultFrameSet;
        ActiveFrameIndex = null;
        ActiveSetChanged?.Invoke(new ActiveSetRequest(DefaultFrameSet, true));
    }
}
