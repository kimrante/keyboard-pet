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

    private static ActiveSetRequest Req(string set, bool reset, int? frame = null) => new(set, reset, frame);

    private static (KeyRuleController Controller, FakeTimerFactory Timers, List<ActiveSetRequest> Events) Build(params KeyRule[] rules)
    {
        var timers = new FakeTimerFactory();
        var controller = new KeyRuleController(timers, new RuleMatcher(rules), "idle");
        var events = new List<ActiveSetRequest>();
        controller.ActiveSetChanged += events.Add;
        return (controller, timers, events);
    }

    [Fact]
    public void StartsOnDefaultSet()
    {
        var (controller, _, events) = Build();

        Assert.Equal("idle", controller.ActiveFrameSet);
        Assert.Null(controller.ActiveRule);
        Assert.Null(controller.ActiveFrameIndex);
        Assert.Empty(events);
    }

    [Fact]
    public void MatchedRule_ActivatesSet_AndStartsHoldTimer()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "jump", HoldMs: 800, ResetIndex: true));

        var matched = controller.OnKeyDown(Down(VkEnter));

        Assert.NotNull(matched);
        Assert.Equal("jump", controller.ActiveFrameSet);
        Assert.Equal(new[] { Req("jump", true) }, events);
        Assert.True(controller.IsHolding);
        Assert.Equal(TimeSpan.FromMilliseconds(800), timers.Last.Interval);
    }

    [Fact]
    public void HoldExpiry_ReturnsToDefault()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "jump", HoldMs: 800));
        controller.OnKeyDown(Down(VkEnter));

        timers.Last.Fire();

        Assert.Equal("idle", controller.ActiveFrameSet);
        Assert.Null(controller.ActiveRule);
        Assert.False(controller.IsHolding);
        Assert.Equal(Req("idle", true), events[^1]);
    }

    [Fact]
    public void HoldZero_StaysUntilAnotherRuleMatches()
    {
        var (controller, timers, events) = Build(
            new KeyRule("Enter", "jump", HoldMs: 0),
            new KeyRule("Space", "blink", HoldMs: 0));
        controller.OnKeyDown(Down(VkEnter));

        Assert.False(controller.IsHolding);
        controller.OnKeyDown(Down(VkA));  // 미매칭: 유지
        Assert.Equal("jump", controller.ActiveFrameSet);

        controller.OnKeyDown(Down(VkSpace));
        Assert.Equal("blink", controller.ActiveFrameSet);
        Assert.Equal(new[] { Req("jump", true), Req("blink", true) }, events);
    }

    [Fact]
    public void UnmatchedKey_DoesNotChangeAnything()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "jump", HoldMs: 800));
        controller.OnKeyDown(Down(VkEnter));
        var startCount = timers.Last.StartCount;

        var matched = controller.OnKeyDown(Down(VkA));

        Assert.Null(matched);
        Assert.Equal("jump", controller.ActiveFrameSet);
        Assert.Equal(startCount, timers.Last.StartCount);
        Assert.Single(events);
    }

    [Fact]
    public void SameRuleAgain_RestartsHoldTimer_AndReplaysWhenResetIndex()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "jump", HoldMs: 800, ResetIndex: true));
        controller.OnKeyDown(Down(VkEnter));

        controller.OnKeyDown(Down(VkEnter));

        Assert.Equal(2, timers.Last.StartCount);
        Assert.Equal(new[] { Req("jump", true), Req("jump", true) }, events);
    }

    [Fact]
    public void SameRuleAgain_WithoutResetIndex_DoesNotRaiseAgain()
    {
        var (controller, timers, events) = Build(new KeyRule("*", "typing", HoldMs: 600, ResetIndex: false));
        controller.OnKeyDown(Down(VkA));

        controller.OnKeyDown(Down(VkA));
        controller.OnKeyDown(Down(VkSpace));

        Assert.Equal(3, timers.Last.StartCount);
        Assert.Equal(new[] { Req("typing", false) }, events);
    }

    [Fact]
    public void SingleFrameRule_RequestsPinnedFrame_AndReturnsToDefaultAfterHold()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "idle", HoldMs: 500, ResetIndex: true, FrameIndex: 2));

        controller.OnKeyDown(Down(VkEnter));

        Assert.Equal(2, controller.ActiveFrameIndex);
        Assert.Equal(new[] { Req("idle", true, 2) }, events);

        timers.Last.Fire();

        Assert.Null(controller.ActiveFrameIndex);
        Assert.Equal(Req("idle", true), events[^1]);
    }

    [Fact]
    public void DifferentFrameOfSameSet_WithoutResetIndex_StillRaises()
    {
        var (controller, _, events) = Build(
            new KeyRule("Enter", "idle", HoldMs: 0, ResetIndex: false, FrameIndex: 1),
            new KeyRule("Space", "idle", HoldMs: 0, ResetIndex: false, FrameIndex: 3));

        controller.OnKeyDown(Down(VkEnter));
        controller.OnKeyDown(Down(VkEnter));   // 같은 대상: 이벤트 없음
        controller.OnKeyDown(Down(VkSpace));   // 다른 프레임: 이벤트

        Assert.Equal(new[] { Req("idle", false, 1), Req("idle", false, 3) }, events);
    }

    [Fact]
    public void ResetToDefault_FromPinnedFrameOnDefaultSet_Raises()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "idle", HoldMs: 0, FrameIndex: 2));
        controller.OnKeyDown(Down(VkEnter));

        controller.ResetToDefault();

        Assert.Null(controller.ActiveFrameIndex);
        Assert.Equal(new[] { Req("idle", true, 2), Req("idle", true) }, events);
    }

    [Fact]
    public void ResetToDefault_StopsTimerAndRaisesOnce()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "jump", HoldMs: 800));
        controller.OnKeyDown(Down(VkEnter));

        controller.ResetToDefault();
        controller.ResetToDefault();

        Assert.Equal("idle", controller.ActiveFrameSet);
        Assert.False(timers.Last.IsRunning);
        Assert.Equal(new[] { Req("jump", true), Req("idle", true) }, events);
    }

    [Fact]
    public void Dispose_DisposesTimer()
    {
        var (controller, timers, _) = Build();

        controller.Dispose();

        Assert.True(timers.Last.IsDisposed);
    }
}
