using KeyboardPet.Core.Input;

namespace KeyboardPet.Core.Tests.Input;

public class PenContactTrackerTests
{
    private static readonly nint Pen1 = 1;
    private static readonly nint Pen2 = 2;

    [Fact]
    public void RawTip_DownThenUp_RaisesTransitions()
    {
        var t = new PenContactTracker();

        Assert.True(t.OnRawReport(Pen1, tipDown: true, nowMs: 0));
        Assert.Null(t.OnRawReport(Pen1, tipDown: true, nowMs: 10));   // 누르고 있는 동안 계속 오는 보고
        Assert.True(t.IsDown(20));
        Assert.False(t.OnRawReport(Pen1, tipDown: false, nowMs: 30));
        Assert.False(t.IsDown(40));
    }

    [Fact]
    public void RawUpReports_BeforeAnyContact_DoNotDisableFallback()
    {
        var t = new PenContactTracker();

        Assert.Null(t.OnRawReport(Pen1, tipDown: false, nowMs: 0));   // 호버 중 보고
        Assert.False(t.RawSeen);
        Assert.True(t.OnHookPenButton(down: true, nowMs: 5));
        Assert.False(t.OnHookPenButton(down: false, nowMs: 6));
    }

    [Fact]
    public void AfterRawContact_HookPenMessagesAreIgnored()
    {
        var t = new PenContactTracker();
        t.OnRawReport(Pen1, true, 0);

        Assert.Null(t.OnHookPenButton(down: false, nowMs: 1));   // 길게 누르기 판정으로 바뀐 마우스 메시지 등
        Assert.True(t.IsDown(2));
        Assert.Null(t.OnHookMouseButton(3));
        Assert.True(t.IsDown(4));
    }

    [Fact]
    public void HookFirst_ThenRaw_DoesNotDoubleCount()
    {
        var t = new PenContactTracker();

        Assert.True(t.OnHookPenButton(down: true, nowMs: 0));
        Assert.Null(t.OnRawReport(Pen1, true, 1));    // 같은 누름: 전환 없음
        Assert.False(t.OnRawReport(Pen1, false, 50));
    }

    [Fact]
    public void Fallback_MissedPenUp_IsClearedByOtherMouseButton()
    {
        var t = new PenContactTracker();
        t.OnHookPenButton(down: true, nowMs: 0);

        Assert.False(t.OnHookMouseButton(10));
        Assert.False(t.IsDown(11));
        Assert.Null(t.OnHookMouseButton(12));
    }

    [Fact]
    public void RawStale_CountsAsUp_AndNextContactRaisesDownAgain()
    {
        var t = new PenContactTracker(TimeSpan.FromMilliseconds(100));
        t.OnRawReport(Pen1, true, 0);

        Assert.False(t.IsDown(150));                    // 뗌 보고를 놓친 채 보고가 끊김
        Assert.True(t.OnRawReport(Pen1, true, 200));    // 다시 닿음으로 알려 반복을 재개하게 한다
    }

    [Fact]
    public void MultipleDevices_UpOnlyWhenAllLifted()
    {
        var t = new PenContactTracker();
        t.OnRawReport(Pen1, true, 0);

        Assert.Null(t.OnRawReport(Pen2, true, 1));
        Assert.Null(t.OnRawReport(Pen1, false, 2));
        Assert.False(t.OnRawReport(Pen2, false, 3));
    }
}
