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

    private static (KeyRuleController Controller, FakeTimerFactory Timers, List<(string Set, bool Reset)> Events) Build(params KeyRule[] rules)
    {
        var timers = new FakeTimerFactory();
        var controller = new KeyRuleController(timers, new RuleMatcher(rules), "idle");
        var events = new List<(string, bool)>();
        controller.ActiveSetChanged += (set, reset) => events.Add((set, reset));
        return (controller, timers, events);
    }

    [Fact]
    public void StartsOnDefaultSet()
    {
        var (controller, _, events) = Build();

        Assert.Equal("idle", controller.ActiveFrameSet);
        Assert.Null(controller.ActiveRule);
        Assert.Empty(events);
    }

    [Fact]
    public void MatchedRule_ActivatesSet_AndStartsHoldTimer()
    {
        var (controller, timers, events) = Build(new KeyRule("Enter", "jump", HoldMs: 800, ResetIndex: true));

        var matched = controller.OnKeyDown(Down(VkEnter));

        Assert.NotNull(matched);
        Assert.Equal("jump", controller.ActiveFrameSet);
        Assert.Equal(new[] { ("jump", true) }, events);
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
        Assert.Equal(("idle", true), events[^1]);
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
        Assert.Equal(new[] { ("jump", true), ("blink", true) }, events);
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
        Assert.Equal(new[] { ("jump", true), ("jump", true) }, events);
    }

    [Fact]
    public void SameRuleAgain_WithoutResetIndex_DoesNotRaiseAgain()
    {
        var (controller, timers, events) = Build(new KeyRule("*", "typing", HoldMs: 600, ResetIndex: false));
        controller.OnKeyDown(Down(VkA));

        controller.OnKeyDown(Down(VkA));
        controller.OnKeyDown(Down(VkSpace));

        Assert.Equal(3, timers.Last.StartCount);
        Assert.Equal(new[] { ("typing", false) }, events);
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
        Assert.Equal(new[] { ("jump", true), ("idle", true) }, events);
    }

    [Fact]
    public void Dispose_DisposesTimer()
    {
        var (controller, timers, _) = Build();

        controller.Dispose();

        Assert.True(timers.Last.IsDisposed);
    }
}
