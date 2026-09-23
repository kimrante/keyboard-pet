using KeyboardPet.Core.Effects;

namespace KeyboardPet.Core.Tests.Effects;

public class EffectMixerTests
{
    private static readonly FrameEffect ShakeAll = new(FrameEffectKind.ShakeHorizontal, 100, 1000);
    private static readonly FrameEffect TiltOnFrame1 = new(FrameEffectKind.Tilt, 100, 1000, new[] { 1 });
    private static readonly FrameEffect[] BounceRule = { new(FrameEffectKind.Bounce, 100, 1000) };

    [Fact]
    public void NoEffects_IsIdle_AndIdentity()
    {
        var mixer = new EffectMixer();

        Assert.True(mixer.IsIdle(null));
        Assert.True(mixer.Sample(123, 0, null, 0).IsIdentity);
        Assert.False(mixer.IsIdle(BounceRule));
    }

    [Fact]
    public void AllFramesEffect_RunsContinuously_AcrossFrameChanges()
    {
        var mixer = new EffectMixer();
        mixer.Configure(new[] { ShakeAll });

        mixer.Sample(1000, 0, null, 0);                       // 시작
        var quarter = mixer.Sample(1250, 1, null, 0);         // 프레임이 바뀌어도 이어짐

        Assert.Equal(FrameEffect.Coefficients.Shake, quarter.OffsetX, 6);
    }

    [Fact]
    public void FrameSpecificEffect_OnlyOnThatFrame_AndRestartsWhenFrameReappears()
    {
        var mixer = new EffectMixer();
        mixer.Configure(new[] { TiltOnFrame1 });

        Assert.Equal(0, mixer.Sample(0, 0, null, 0).Angle);
        Assert.Equal(0, mixer.Sample(100, 1, null, 0).Angle, 6);          // 프레임 1이 보인 순간 = 효과 시작
        Assert.Equal(FrameEffect.Coefficients.TiltDegrees, mixer.Sample(350, 1, null, 0).Angle, 6);
        Assert.Equal(0, mixer.Sample(400, 0, null, 0).Angle);             // 다른 프레임에서는 멈춤
        Assert.Equal(0, mixer.Sample(900, 1, null, 0).Angle, 6);          // 다시 보이면 처음부터
    }

    [Fact]
    public void RuleEffects_StartAtActivation_AndRestartOnNewActivation()
    {
        var mixer = new EffectMixer();

        Assert.Equal(0, mixer.Sample(1000, 0, BounceRule, 1).OffsetY, 6);
        Assert.Equal(-FrameEffect.Coefficients.Bounce, mixer.Sample(1500, 0, BounceRule, 1).OffsetY, 6);
        Assert.Equal(0, mixer.Sample(1600, 0, BounceRule, 2).OffsetY, 6);    // 같은 키 재입력 → 처음부터
        Assert.True(mixer.Sample(1700, 0, null, 2).IsIdentity);              // 규칙이 끝나면 멈춤
    }

    [Fact]
    public void SetAndRuleEffects_AreCombined()
    {
        var mixer = new EffectMixer();
        mixer.Configure(new[] { ShakeAll, TiltOnFrame1 });

        mixer.Sample(0, 1, BounceRule, 5);
        var t = mixer.Sample(250, 1, BounceRule, 5);

        Assert.Equal(FrameEffect.Coefficients.Shake, t.OffsetX, 6);
        Assert.Equal(FrameEffect.Coefficients.TiltDegrees, t.Angle, 6);
        Assert.True(t.OffsetY < 0);
    }
}
