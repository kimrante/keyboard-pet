using KeyboardPet.Core.Effects;

namespace KeyboardPet.Core.Tests.Effects;

public class FrameEffectTests
{
    private const int Period = 1000;

    private static EffectTransform At(FrameEffectKind kind, int strength, double ms) =>
        new FrameEffect(kind, strength, Period).Evaluate(ms);

    [Theory]
    [InlineData(FrameEffectKind.BobVertical)]
    [InlineData(FrameEffectKind.ShakeHorizontal)]
    [InlineData(FrameEffectKind.Shrink)]
    [InlineData(FrameEffectKind.Grow)]
    [InlineData(FrameEffectKind.Bounce)]
    [InlineData(FrameEffectKind.Tilt)]
    [InlineData(FrameEffectKind.Squash)]
    [InlineData(FrameEffectKind.Blink)]
    public void EveryEffect_StartsAtRest_AndZeroStrengthNeverMoves(FrameEffectKind kind)
    {
        AssertIdentity(At(kind, 100, 0));
        AssertIdentity(At(kind, 0, 250));
        AssertIdentity(At(kind, 0, 500));
        Assert.False(At(kind, 100, 250).IsIdentity && At(kind, 100, 500).IsIdentity);   // 강도가 있으면 움직인다
    }

    [Fact]
    public void Bob_And_Shake_PeakAtQuarterPeriod_ScaledByStrength()
    {
        Assert.Equal(-FrameEffect.Coefficients.Bob, At(FrameEffectKind.BobVertical, 100, 250).OffsetY, 6);
        Assert.Equal(-FrameEffect.Coefficients.Bob / 2, At(FrameEffectKind.BobVertical, 50, 250).OffsetY, 6);
        Assert.Equal(FrameEffect.Coefficients.Shake, At(FrameEffectKind.ShakeHorizontal, 100, 250).OffsetX, 6);
        Assert.Equal(-FrameEffect.Coefficients.Shake, At(FrameEffectKind.ShakeHorizontal, 100, 750).OffsetX, 6);
    }

    [Fact]
    public void Shrink_Grow_Blink_AreDeepestAtHalfPeriod()
    {
        Assert.Equal(1 - FrameEffect.Coefficients.Shrink, At(FrameEffectKind.Shrink, 100, 500).ScaleX, 6);
        Assert.Equal(1 + FrameEffect.Coefficients.Grow, At(FrameEffectKind.Grow, 100, 500).ScaleY, 6);
        Assert.Equal(1 - FrameEffect.Coefficients.Blink, At(FrameEffectKind.Blink, 100, 500).Opacity, 6);
    }

    [Fact]
    public void Bounce_IsAlwaysUpward_TopAtHalfPeriod_AndRepeats()
    {
        Assert.Equal(-FrameEffect.Coefficients.Bounce, At(FrameEffectKind.Bounce, 100, 500).OffsetY, 6);
        Assert.Equal(-FrameEffect.Coefficients.Bounce, At(FrameEffectKind.Bounce, 100, 1500).OffsetY, 6);
        for (var ms = 0; ms < 2000; ms += 37)
        {
            Assert.True(At(FrameEffectKind.Bounce, 100, ms).OffsetY <= 0);
        }
    }

    [Fact]
    public void Tilt_And_Squash_SwingBothWays()
    {
        Assert.Equal(FrameEffect.Coefficients.TiltDegrees, At(FrameEffectKind.Tilt, 100, 250).Angle, 6);
        Assert.Equal(-FrameEffect.Coefficients.TiltDegrees, At(FrameEffectKind.Tilt, 100, 750).Angle, 6);
        var squash = At(FrameEffectKind.Squash, 100, 250);
        Assert.True(squash.ScaleX > 1 && squash.ScaleY < 1);
    }

    [Fact]
    public void Faster_Period_MovesSooner()
    {
        var fast = new FrameEffect(FrameEffectKind.ShakeHorizontal, 100, 200).Evaluate(50);
        Assert.Equal(FrameEffect.Coefficients.Shake, fast.OffsetX, 6);
    }

    [Fact]
    public void Combine_AddsMovesAndAngles_MultipliesScalesAndOpacity()
    {
        var a = new EffectTransform(0.1, -0.2, 1.5, 0.5, 10, 0.5);
        var b = new EffectTransform(0.05, 0.1, 2, 2, -4, 0.5);

        var c = a.Combine(b);

        Assert.Equal(0.15, c.OffsetX, 6);
        Assert.Equal(-0.1, c.OffsetY, 6);
        Assert.Equal(3, c.ScaleX, 6);
        Assert.Equal(1, c.ScaleY, 6);
        Assert.Equal(6, c.Angle, 6);
        Assert.Equal(0.25, c.Opacity, 6);
        Assert.Equal(a, a.Combine(EffectTransform.Identity));
    }

    [Fact]
    public void Normalized_ClampsValues_AndCleansFrames()
    {
        var e = new FrameEffect(FrameEffectKind.Tilt, 250, 5, new[] { 3, -1, 1, 3 }).Normalized();

        Assert.Equal(FrameEffect.MaxStrength, e.Strength);
        Assert.Equal(FrameEffect.MinPeriodMs, e.PeriodMs);
        Assert.Equal(new[] { 1, 3 }, e.Frames);
        Assert.Equal(FrameEffectKind.BobVertical, new FrameEffect((FrameEffectKind)99).Normalized().Kind);
    }

    [Fact]
    public void AppliesTo_AllFramesWhenNull_OtherwiseListed()
    {
        Assert.True(new FrameEffect(FrameEffectKind.Blink).AppliesTo(7));
        var some = new FrameEffect(FrameEffectKind.Blink, Frames: new[] { 1, 2 });
        Assert.True(some.AppliesTo(2));
        Assert.False(some.AppliesTo(0));
    }

    [Fact]
    public void ListsEqual_ComparesByValue_IncludingFrames()
    {
        var a = new[] { new FrameEffect(FrameEffectKind.Bounce, 60, 500, new[] { 1 }) };
        Assert.True(FrameEffect.ListsEqual(a, new[] { new FrameEffect(FrameEffectKind.Bounce, 60, 500, new[] { 1 }) }));
        Assert.False(FrameEffect.ListsEqual(a, new[] { new FrameEffect(FrameEffectKind.Bounce, 60, 500) }));
        Assert.False(FrameEffect.ListsEqual(a, new[] { new FrameEffect(FrameEffectKind.Bounce, 61, 500, new[] { 1 }) }));
        Assert.True(FrameEffect.ListsEqual(null, null));
        Assert.False(FrameEffect.ListsEqual(a, null));
    }

    [Fact]
    public void Padding_GrowsWithStrength_AndAddsAcrossKinds()
    {
        Assert.Equal(EffectPadding.None, EffectPadding.For(Array.Empty<FrameEffect>()));
        Assert.Equal(EffectPadding.None, EffectPadding.For(new[] { new FrameEffect(FrameEffectKind.Blink, 100) }));

        var bounce = EffectPadding.For(new[] { new FrameEffect(FrameEffectKind.Bounce, 100) });
        Assert.Equal(FrameEffect.Coefficients.Bounce, bounce.Top, 6);
        Assert.Equal(0, bounce.Side);

        var combined = EffectPadding.For(new[] { new FrameEffect(FrameEffectKind.Bounce, 100), new FrameEffect(FrameEffectKind.BobVertical, 100) });
        Assert.Equal(FrameEffect.Coefficients.Bounce + FrameEffect.Coefficients.Bob, combined.Top, 6);
        Assert.True(EffectPadding.For(new[] { new FrameEffect(FrameEffectKind.Tilt, 100) }).Side > 0.4);
    }

    private static void AssertIdentity(EffectTransform t)
    {
        Assert.Equal(0, t.OffsetX, 9);
        Assert.Equal(0, t.OffsetY, 9);
        Assert.Equal(1, t.ScaleX, 9);
        Assert.Equal(1, t.ScaleY, 9);
        Assert.Equal(0, t.Angle, 9);
        Assert.Equal(1, t.Opacity, 9);
    }
}
