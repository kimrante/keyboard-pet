namespace KeyboardPet.Core.Effects;

/// <summary>
/// 현재 프레임과 활성 키 규칙에 맞춰 효과들을 합성한다.
/// - 세트 효과: 적용 프레임이 보이기 시작한 순간부터 시간이 흐른다(프레임이 바뀌어 빠지면 초기화).
///   모든 프레임에 적용되는 효과는 끊기지 않고 이어진다.
/// - 규칙 효과: 키를 누른 순간(<see cref="RestartRule"/>)부터 시간이 흐르고, 규칙이 끝나면 멈춘다.
/// </summary>
public sealed class EffectMixer
{
    private IReadOnlyList<FrameEffect> _setEffects = Array.Empty<FrameEffect>();
    private double?[] _startedAt = Array.Empty<double?>();
    private double? _ruleStartedAt;

    /// <summary>세트 효과를 바꾼다. 강도 0인 효과는 움직이지 않으므로 뺀다.</summary>
    public void Configure(IReadOnlyList<FrameEffect> setEffects)
    {
        _setEffects = setEffects.Where(e => e.IsVisible).ToList();
        _startedAt = new double?[_setEffects.Count];
    }

    /// <summary>규칙이 (다시) 활성화되었다: 규칙 효과를 다음 샘플부터 처음부터 재생한다.</summary>
    public void RestartRule() => _ruleStartedAt = null;

    /// <summary>지금 보이는 프레임에서 움직일 효과가 하나도 없는지(세트 효과 해당 없음, 활성 규칙에 효과 없음).</summary>
    public bool IsIdle(int frameIndex, IReadOnlyList<FrameEffect>? activeRuleEffects)
    {
        if (activeRuleEffects is not null)
        {
            for (var i = 0; i < activeRuleEffects.Count; i++)
            {
                if (activeRuleEffects[i].IsVisible)
                {
                    return false;
                }
            }
        }

        for (var i = 0; i < _setEffects.Count; i++)
        {
            if (_setEffects[i].AppliesTo(frameIndex))
            {
                return false;
            }
        }

        return true;
    }

    /// <param name="nowMs">단조 증가하는 현재 시각(ms)</param>
    /// <param name="frameIndex">지금 보이는 프레임</param>
    /// <param name="activeRuleEffects">활성 규칙의 효과(없으면 null)</param>
    public EffectTransform Sample(double nowMs, int frameIndex, IReadOnlyList<FrameEffect>? activeRuleEffects)
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
            _ruleStartedAt ??= nowMs;
            for (var i = 0; i < activeRuleEffects.Count; i++)
            {
                result = result.Combine(activeRuleEffects[i].Evaluate(nowMs - _ruleStartedAt.Value));
            }
        }
        else
        {
            _ruleStartedAt = null;
        }

        return result;
    }
}
