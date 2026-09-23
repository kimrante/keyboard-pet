using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Tests.Animation;

namespace KeyboardPet.Core.Tests.Rules;

public class KeyRuleControllerTests
{
    private const int VkEnter = 0x0D;
    private const int VkSpace = 0x20;
    private const int VkA = 0x41;

    private static KeyEvent Down(int vk) => new(vk, true, KeyModifiers.None, false);

    private static DisplayRequest Req(bool reset, int? frame = null) => new(reset, frame);

    private static (KeyRuleController Controller, FakeTimerFactory Timers, List<DisplayRequest> Events) Build(params KeyRule[] rules)
    {
        var timers = new FakeTimerFactory();
        var controller = new KeyRuleController(timers, new RuleMatcher(rules));
        var events = new List<DisplayRequest>();
        controller.DisplayChanged += events.Add;
        return (controller, timers, events);
    }

    [Fact]
    public void StartsOnAnimation()
    {
        var (controller, _, events) = Build();

        Assert.Null(controller.ActiveRule);
        Assert.Null(controller.ActiveFrameIndex);
        Assert.Empty(events);
    }

    [Fact]
    public void MatchedFrameRule_PinsFrame_AndStartsHoldTimer()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", HoldMs: 800, ResetIndex: true, FrameIndex: 1));

        var matched = controller.OnKeyDown(Down(VkEnter));

        Assert.NotNull(matched);
        Assert.Equal(1, controller.ActiveFrameIndex);
        Assert.Equal(new[] { Req(true, 1) }, events);
        Assert.True(controller.IsHolding);
        Assert.Equal(TimeSpan.FromMilliseconds(800), timers.Last.Interval);
    }

    [Fact]
    public void HoldExpiry_ReturnsToAnimation()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", HoldMs: 800, FrameIndex: 1));
        controller.OnKeyDown(Down(VkEnter));

        timers.Last.Fire();

        Assert.Null(controller.ActiveFrameIndex);
        Assert.Null(controller.ActiveRule);
        Assert.False(controller.IsHolding);
        Assert.Equal(new[] { Req(true, 1), Req(true) }, events);
    }

    [Fact]
    public void AnimationRule_WithResetIndex_RestartsAnimation_AndExpiryDoesNotRestartAgain()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", HoldMs: 800, ResetIndex: true));
        controller.OnKeyDown(Down(VkEnter));

        timers.Last.Fire();

        Assert.Equal(new[] { Req(true) }, events);
        Assert.Null(controller.ActiveRule);
    }

    [Fact]
    public void HoldZero_StaysUntilAnotherRuleMatches()
    {
        var (controller, timers, events) = Build(
            new KeyRule("Enter", HoldMs: 0, FrameIndex: 1),
            new KeyRule("Space", HoldMs: 0, FrameIndex: 2));
        controller.OnKeyDown(Down(VkEnter));

        Assert.False(controller.IsHolding);
        controller.OnKeyDown(Down(VkA));  // 미매칭: 유지
        Assert.Equal(1, controller.ActiveFrameIndex);

        controller.OnKeyDown(Down(VkSpace));
        Assert.Equal(2, controller.ActiveFrameIndex);
        Assert.Equal(new[] { Req(true, 1), Req(true, 2) }, events);
    }

    [Fact]
    public void UnmatchedKey_DoesNotChangeAnything()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", HoldMs: 800, FrameIndex: 1));
        controller.OnKeyDown(Down(VkEnter));
        var startCount = timers.Last.StartCount;

        var matched = controller.OnKeyDown(Down(VkA));

        Assert.Null(matched);
        Assert.Equal(1, controller.ActiveFrameIndex);
        Assert.Equal(startCount, timers.Last.StartCount);
        Assert.Single(events);
    }

    [Fact]
    public void SameRuleAgain_RestartsHoldTimer_AndReplaysWhenResetIndex()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", HoldMs: 800, ResetIndex: true));
        controller.OnKeyDown(Down(VkEnter));

        controller.OnKeyDown(Down(VkEnter));

        Assert.Equal(2, timers.Last.StartCount);
        Assert.Equal(new[] { Req(true), Req(true) }, events);
    }

    [Fact]
    public void AnimationRule_WithoutResetIndex_DoesNotInterruptLoop()
    {
        var (controller, timers, events) = Build(new KeyRule("*", HoldMs: 600, ResetIndex: false));
        controller.OnKeyDown(Down(VkA));

        controller.OnKeyDown(Down(VkA));
        controller.OnKeyDown(Down(VkSpace));
        timers.Last.Fire();

        Assert.Equal(3, timers.Last.StartCount);
        Assert.Empty(events);
    }

    [Fact]
    public void DifferentFrame_WithoutResetIndex_StillRaises()
    {
        var (controller, _, events) = Build(
            new KeyRule("Enter", HoldMs: 0, ResetIndex: false, FrameIndex: 1),
            new KeyRule("Space", HoldMs: 0, ResetIndex: false, FrameIndex: 3));

        controller.OnKeyDown(Down(VkEnter));
        controller.OnKeyDown(Down(VkEnter));   // 같은 대상: 이벤트 없음
        controller.OnKeyDown(Down(VkSpace));   // 다른 프레임: 이벤트

        Assert.Equal(new[] { Req(false, 1), Req(false, 3) }, events);
    }

    [Fact]
    public void AnimationRule_AfterPinnedFrame_UnpinsEvenWithoutResetIndex()
    {
        var (controller, _, events) = Build(
            new KeyRule("Enter", HoldMs: 0, FrameIndex: 1),
            new KeyRule("Space", HoldMs: 0, ResetIndex: false));

        controller.OnKeyDown(Down(VkEnter));
        controller.OnKeyDown(Down(VkSpace));

        Assert.Null(controller.ActiveFrameIndex);
        Assert.Equal(new[] { Req(true, 1), Req(false) }, events);
    }

    [Fact]
    public void ResetToDefault_StopsTimerAndRaisesOnce()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", HoldMs: 800, FrameIndex: 2));
        controller.OnKeyDown(Down(VkEnter));

        controller.ResetToDefault();
        controller.ResetToDefault();

        Assert.Null(controller.ActiveFrameIndex);
        Assert.False(timers.Last.IsRunning);
        Assert.Equal(new[] { Req(true, 2), Req(true) }, events);
    }

    [Fact]
    public void Dispose_DisposesTimer()
    {
        var (controller, timers, _) = Build();

        controller.Dispose();

        Assert.True(timers.Last.IsDisposed);
    }
}
