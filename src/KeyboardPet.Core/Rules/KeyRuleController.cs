using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Rules;

/// <summary>
/// 규칙 매칭 결과에 따라 현재 세트에서 "루프 애니메이션을 재생할지, 어떤 프레임 한 장을 보여줄지"를 관리한다.
/// HoldMs가 있는 규칙은 타이머 만료 시 루프 애니메이션으로 복귀하고, 같은 규칙이 다시 매칭되면 타이머를 재시작한다.
/// </summary>
public sealed class KeyRuleController : IDisposable
{
    private readonly RuleMatcher _matcher;
    private readonly IFrameTimer _holdTimer;

    public KeyRuleController(IFrameTimerFactory timers, RuleMatcher matcher)
    {
        _matcher = matcher;

        _holdTimer = timers.Create();
        _holdTimer.Tick += OnHoldExpired;
    }

    /// <summary>단일 프레임 규칙이 활성일 때 그 프레임 번호. 애니메이션 상태면 null.</summary>
    public int? ActiveFrameIndex { get; private set; }

    public KeyRule? ActiveRule { get; private set; }

    public bool IsHolding => _holdTimer.IsRunning;

    /// <summary>규칙이 활성화될 때마다(같은 규칙 재입력 포함) 1씩 늘어난다. 규칙 효과를 처음부터 재생하는 기준.</summary>
    public long ActivationCount { get; private set; }

    /// <summary>보여줄 대상이 바뀌어야 할 때 발생.</summary>
    public event Action<DisplayRequest>? DisplayChanged;

    /// <summary>규칙이 매칭되어 활성화될 때마다 발생(표시 대상이 그대로여도).</summary>
    public event Action<KeyRule>? RuleActivated;

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
        if (ActiveFrameIndex is not null)
        {
            ActiveFrameIndex = null;
            DisplayChanged?.Invoke(new DisplayRequest(true));
        }
    }

    public void Dispose()
    {
        _holdTimer.Tick -= OnHoldExpired;
        _holdTimer.Dispose();
        DisplayChanged = null;
        RuleActivated = null;
    }

    private void Activate(KeyRule rule)
    {
        var targetChanged = ActiveFrameIndex != rule.FrameIndex;
        ActiveRule = rule;
        ActiveFrameIndex = rule.FrameIndex;
        ActivationCount++;

        // 같은 대상이 유지되는 경우에도 ResetIndex=true면 애니메이션을 처음부터 다시 재생한다.
        if (targetChanged || rule.ResetIndex)
        {
            DisplayChanged?.Invoke(new DisplayRequest(rule.ResetIndex, rule.FrameIndex));
        }

        _holdTimer.Stop();
        if (rule.HoldMs > 0)
        {
            _holdTimer.Interval = TimeSpan.FromMilliseconds(rule.HoldMs);
            _holdTimer.Start();
        }

        RuleActivated?.Invoke(rule);
    }

    private void OnHoldExpired()
    {
        _holdTimer.Stop();
        var wasPinned = ActiveFrameIndex is not null;
        ActiveRule = null;
        ActiveFrameIndex = null;

        // 애니메이션 규칙이 끝났을 때는 이미 루프를 재생 중이므로 흐름을 끊지 않는다.
        if (wasPinned)
        {
            DisplayChanged?.Invoke(new DisplayRequest(true));
        }
    }
}
