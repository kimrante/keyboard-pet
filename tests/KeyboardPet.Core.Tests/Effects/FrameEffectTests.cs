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

    [Theory]
    [InlineData(FrameEffectKind.BobVertical)]
    [InlineData(FrameEffectKind.ShakeHorizontal)]
    [InlineData(FrameEffectKind.Shrink)]
    [InlineData(FrameEffectKind.Grow)]
    [InlineData(FrameEffectKind.Bounce)]
    [InlineData(FrameEffectKind.Tilt)]
    [InlineData(FrameEffectKind.Squash)]
    [InlineData(FrameEffectKind.Blink)]
    public void MaxExtent_CoversEverySampledPosition(FrameEffectKind kind)
    {
        // 정사각형 이미지 기준으로 한 주기를 촘촘히 돌며, 실제 움직임이 여백 안에 들어오는지 본다.
        var effect = new FrameEffect(kind, 100, 1000);
        var extent = effect.MaxExtent();
        for (var ms = 0; ms <= 1000; ms += 10)
        {
            var t = effect.Evaluate(ms);
            var (left, top, right, bottom) = TransformedBounds(t);
            Assert.True(-left <= extent.Side + 1e-9, $"{kind} @{ms}: left {-left} > {extent.Side}");
            Assert.True(right - 1 <= extent.Side + 1e-9, $"{kind} @{ms}: right {right - 1} > {extent.Side}");
            Assert.True(-top <= extent.Top + 1e-9, $"{kind} @{ms}: top {-top} > {extent.Top}");
            Assert.True(bottom - 1 <= extent.Bottom + 1e-9, $"{kind} @{ms}: bottom {bottom - 1} > {extent.Bottom}");
        }
    }

    /// <summary>단위 정사각형(0,0)-(1,1)을 바닥 가운데(0.5,1) 기준으로 변형했을 때의 외접 사각형.</summary>
    private static (double Left, double Top, double Right, double Bottom) TransformedBounds(EffectTransform t)
    {
        var rad = t.Angle * Math.PI / 180;
        var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var (x, y) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) })
        {
            var dx = (x - 0.5) * t.ScaleX;
            var dy = (y - 1) * t.ScaleY;
            var px = 0.5 + dx * cos - dy * sin + t.OffsetX;
            var py = 1 + dx * sin + dy * cos + t.OffsetY;
            left = Math.Min(left, px); right = Math.Max(right, px);
            top = Math.Min(top, py); bottom = Math.Max(bottom, py);
        }

        return (left, top, right, bottom);
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
        var tilt = EffectPadding.For(new[] { new FrameEffect(FrameEffectKind.Tilt, 100) });
        Assert.True(tilt.Side > 0.3 && tilt.Top > 0 && tilt.Bottom > 0);   // 기울면 옆·위·아래로 모두 조금씩 나간다
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
