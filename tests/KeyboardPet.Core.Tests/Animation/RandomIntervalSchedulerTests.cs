using KeyboardPet.Core.Animation;

namespace KeyboardPet.Core.Tests.Animation;

public class RandomIntervalSchedulerTests
{
    private static readonly TimeSpan Min = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Max = TimeSpan.FromMilliseconds(600);

    [Fact]
    public void Start_PicksIntervalWithinRange()
    {
        var timers = new FakeTimerFactory();
        using var scheduler = new RandomIntervalScheduler(timers, Min, Max, () => { }, new Random(42));

        scheduler.Start();

        Assert.InRange(timers.Last.Interval, Min, Max);
        Assert.True(scheduler.IsRunning);
    }

    [Fact]
    public void EveryTick_AdvancesAndRerollsIntervalWithinRange()
    {
        var timers = new FakeTimerFactory();
        var advanced = 0;
        using var scheduler = new RandomIntervalScheduler(timers, Min, Max, () => advanced++, new Random(7));
        scheduler.Start();

        var intervals = new List<TimeSpan>();
        for (var i = 0; i < 50; i++)
        {
            timers.Last.Fire();
            intervals.Add(timers.Last.Interval);
        }

        Assert.Equal(50, advanced);
        Assert.All(intervals, t => Assert.InRange(t, Min, Max));
        Assert.True(intervals.Distinct().Count() > 1, "간격이 매 프레임 재산출되어야 한다.");
    }

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new FakeTimerFactory();
        var b = new FakeTimerFactory();
        using var sa = new RandomIntervalScheduler(a, Min, Max, () => { }, new Random(123));
        using var sb = new RandomIntervalScheduler(b, Min, Max, () => { }, new Random(123));
        sa.Start();
        sb.Start();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(a.Last.Interval, b.Last.Interval);
            a.Last.Fire();
            b.Last.Fire();
        }
    }

    [Fact]
    public void SwappedMinMax_AreCorrected()
    {
        var timers = new FakeTimerFactory();
        using var scheduler = new RandomIntervalScheduler(timers, Max, Min, () => { }, new Random(1));

        scheduler.Start();

        Assert.InRange(timers.Last.Interval, Min, Max);
    }
}
