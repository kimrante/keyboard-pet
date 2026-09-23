namespace KeyboardPet.Core.Effects;

/// <summary>
/// 현재 프레임과 활성 키 규칙에 맞춰 효과들을 합성한다.
/// - 세트 효과: 적용 프레임이 보이기 시작한 순간부터 시간이 흐른다(프레임이 바뀌어 빠지면 초기화).
///   모든 프레임에 적용되는 효과는 끊기지 않고 이어진다.
/// - 규칙 효과: 키를 누른 순간(규칙이 활성화될 때마다)부터 시간이 흐르고, 규칙이 끝나면 멈춘다.
/// </summary>
public sealed class EffectMixer
{
    private IReadOnlyList<FrameEffect> _setEffects = Array.Empty<FrameEffect>();
    private double?[] _startedAt = Array.Empty<double?>();
    private long _ruleActivation = -1;
    private double _ruleStartedAt;

    public IReadOnlyList<FrameEffect> SetEffects => _setEffects;

    public void Configure(IReadOnlyList<FrameEffect> setEffects)
    {
        _setEffects = setEffects;
        _startedAt = new double?[setEffects.Count];
    }

    /// <summary>지금 움직일 효과가 하나도 없는지(세트 효과 없음, 활성 규칙에 효과 없음).</summary>
    public bool IsIdle(IReadOnlyList<FrameEffect>? activeRuleEffects) =>
        _setEffects.Count == 0 && activeRuleEffects is not { Count: > 0 };

    /// <param name="nowMs">단조 증가하는 현재 시각(ms)</param>
    /// <param name="frameIndex">지금 보이는 프레임</param>
    /// <param name="activeRuleEffects">활성 규칙의 효과(없으면 null)</param>
    /// <param name="ruleActivation">규칙이 활성화될 때마다 바뀌는 번호. 같은 키를 다시 누르면 효과가 처음부터 재생된다</param>
    public EffectTransform Sample(double nowMs, int frameIndex, IReadOnlyList<FrameEffect>? activeRuleEffects, long ruleActivation)
    {
        var result = EffectTransform.Identity;

        for (var i = 0; i < _setEffects.Count; i++)
        {
            var effect = _setEffects[i];
            if (!effect.AppliesTo(frameIndex))
            {
                _startedAt[i] = null;
                continue;
            }

            _startedAt[i] ??= nowMs;
            result = result.Combine(effect.Evaluate(nowMs - _startedAt[i]!.Value));
        }

        if (activeRuleEffects is { Count: > 0 })
        {
            if (ruleActivation != _ruleActivation)
            {
                _ruleActivation = ruleActivation;
                _ruleStartedAt = nowMs;
            }

            foreach (var effect in activeRuleEffects)
            {
                result = result.Combine(effect.Evaluate(nowMs - _ruleStartedAt));
            }
        }
        else
        {
            _ruleActivation = -1;
        }

        return result;
    }
}
