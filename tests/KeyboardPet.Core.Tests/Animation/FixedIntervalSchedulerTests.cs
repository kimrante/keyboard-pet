using KeyboardPet.Core.Animation;

namespace KeyboardPet.Core.Tests.Animation;

public class FixedIntervalSchedulerTests
{
    [Fact]
    public void Ctor_SetsIntervalOnTimer()
    {
        var timers = new FakeTimerFactory();

        using var scheduler = new FixedIntervalScheduler(timers, TimeSpan.FromMilliseconds(250), () => { });

        Assert.Single(timers.Created);
        Assert.Equal(TimeSpan.FromMilliseconds(250), timers.Last.Interval);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void Tick_CallsAdvance_OnlyWhileRunning()
    {
        var timers = new FakeTimerFactory();
        var advanced = 0;
        using var scheduler = new FixedIntervalScheduler(timers, TimeSpan.FromMilliseconds(100), () => advanced++);

        timers.Last.Fire();
        Assert.Equal(0, advanced);

        scheduler.Start();
        timers.Last.Fire();
        timers.Last.Fire();
        Assert.Equal(2, advanced);

        scheduler.Stop();
        timers.Last.Fire();
        Assert.Equal(2, advanced);
    }

    [Fact]
    public void OnKeystroke_IsIgnored()
    {
        var timers = new FakeTimerFactory();
        var advanced = 0;
        using var scheduler = new FixedIntervalScheduler(timers, TimeSpan.FromMilliseconds(100), () => advanced++);
        scheduler.Start();

        scheduler.OnKeystroke();

        Assert.Equal(0, advanced);
    }

    [Fact]
    public void Dispose_DisposesTimer()
    {
        var timers = new FakeTimerFactory();
        var scheduler = new FixedIntervalScheduler(timers, TimeSpan.FromMilliseconds(100), () => { });

        scheduler.Dispose();

        Assert.True(timers.Last.IsDisposed);
    }
}
