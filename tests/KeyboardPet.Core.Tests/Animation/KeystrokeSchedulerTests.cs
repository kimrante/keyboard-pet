using KeyboardPet.Core.Animation;

namespace KeyboardPet.Core.Tests.Animation;

public class KeystrokeSchedulerTests
{
    [Fact]
    public void AdvancesEveryNKeystrokes()
    {
        var timers = new FakeTimerFactory();
        var advanced = 0;
        using var scheduler = new KeystrokeScheduler(timers, keysPerFrame: 3, TimeSpan.Zero, () => advanced++, () => { });
        scheduler.Start();

        scheduler.OnKeystroke();
        scheduler.OnKeystroke();
        Assert.Equal(0, advanced);

        scheduler.OnKeystroke();
        Assert.Equal(1, advanced);

        scheduler.OnKeystroke();
        scheduler.OnKeystroke();
        scheduler.OnKeystroke();
        Assert.Equal(2, advanced);
    }

    [Fact]
    public void KeystrokesAreIgnoredWhenNotRunning()
    {
        var timers = new FakeTimerFactory();
        var advanced = 0;
        using var scheduler = new KeystrokeScheduler(timers, 1, TimeSpan.Zero, () => advanced++, () => { });

        scheduler.OnKeystroke();

        Assert.Equal(0, advanced);
    }

    [Fact]
    public void IdleReturnZero_CreatesNoTimer()
    {
        var timers = new FakeTimerFactory();
        using var scheduler = new KeystrokeScheduler(timers, 1, TimeSpan.Zero, () => { }, () => { });
        scheduler.Start();

        scheduler.OnKeystroke();

        Assert.Empty(timers.Created);
    }

    [Fact]
    public void IdleTimer_RestartsOnEachKeystroke_AndReturnsToIdleOnExpiry()
    {
        var timers = new FakeTimerFactory();
        var returned = 0;
        using var scheduler = new KeystrokeScheduler(timers, 2, TimeSpan.FromSeconds(2), () => { }, () => returned++);
        scheduler.Start();

        scheduler.OnKeystroke();
        Assert.Equal(TimeSpan.FromSeconds(2), timers.Last.Interval);
        Assert.True(timers.Last.IsRunning);
        Assert.Equal(1, timers.Last.StartCount);

        scheduler.OnKeystroke();
        Assert.Equal(2, timers.Last.StartCount);

        timers.Last.Fire();
        Assert.Equal(1, returned);
        Assert.False(timers.Last.IsRunning);
        Assert.Equal(0, scheduler.PendingKeystrokes);
    }

    [Fact]
    public void IdleExpiry_ResetsPendingCount()
    {
        var timers = new FakeTimerFactory();
        var advanced = 0;
        using var scheduler = new KeystrokeScheduler(timers, 3, TimeSpan.FromSeconds(1), () => advanced++, () => { });
        scheduler.Start();

        scheduler.OnKeystroke();
        scheduler.OnKeystroke();
        timers.Last.Fire();

        scheduler.OnKeystroke();
        Assert.Equal(0, advanced);
        Assert.Equal(1, scheduler.PendingKeystrokes);
    }

    [Fact]
    public void Stop_StopsIdleTimer_AndClearsPending()
    {
        var timers = new FakeTimerFactory();
        using var scheduler = new KeystrokeScheduler(timers, 3, TimeSpan.FromSeconds(1), () => { }, () => { });
        scheduler.Start();
        scheduler.OnKeystroke();

        scheduler.Stop();

        Assert.False(timers.Last.IsRunning);
        Assert.Equal(0, scheduler.PendingKeystrokes);
        Assert.False(scheduler.IsRunning);
    }
}
