using KeyboardPet.Core.Animation;

namespace KeyboardPet.Core.Tests.Animation;

public class AnimationEngineTests
{
    private static FrameSet Set(int count) => new("test", count);

    [Fact]
    public void Advance_WrapsAroundToFirstFrame()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        engine.SetActiveSet(Set(3));
        var seen = new List<int>();
        engine.FrameChanged += seen.Add;

        engine.Advance();
        engine.Advance();
        engine.Advance();

        Assert.Equal(new[] { 1, 2, 0 }, seen);
        Assert.Equal(0, engine.FrameIndex);
    }

    [Fact]
    public void Advance_WithSingleFrame_DoesNothing()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        engine.SetActiveSet(Set(1));
        var changed = 0;
        engine.FrameChanged += _ => changed++;

        engine.Advance();

        Assert.Equal(0, changed);
        Assert.Equal(0, engine.FrameIndex);
    }

    [Fact]
    public void Advance_WithEmptySet_DoesNothing()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        var changed = 0;
        engine.FrameChanged += _ => changed++;

        engine.Advance();

        Assert.Equal(0, changed);
    }

    [Fact]
    public void SetActiveSet_RaisesFrameChangedWithIndexZero()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        var seen = new List<int>();
        engine.FrameChanged += seen.Add;

        engine.SetActiveSet(Set(4));

        Assert.Equal(new[] { 0 }, seen);
        Assert.Equal(4, engine.ActiveSet.FrameCount);
    }

    [Fact]
    public void SetActiveSet_ResetIndexTrue_GoesToZero()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        engine.SetActiveSet(Set(5));
        engine.Advance();
        engine.Advance();

        engine.SetActiveSet(Set(5), resetIndex: true);

        Assert.Equal(0, engine.FrameIndex);
    }

    [Fact]
    public void SetActiveSet_ResetIndexFalse_KeepsIndexWhenInRange()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        engine.SetActiveSet(Set(5));
        engine.Advance();
        engine.Advance();

        engine.SetActiveSet(Set(5), resetIndex: false);

        Assert.Equal(2, engine.FrameIndex);
    }

    [Fact]
    public void SetActiveSet_ResetIndexFalse_ClampsToZeroWhenOutOfRange()
    {
        var engine = new AnimationEngine(new FakeTimerFactory());
        engine.SetActiveSet(Set(5));
        engine.Advance();
        engine.Advance();
        engine.Advance();

        engine.SetActiveSet(Set(2), resetIndex: false);

        Assert.Equal(0, engine.FrameIndex);
    }

    [Fact]
    public void FixedMode_TimerTick_AdvancesFrame()
    {
        var timers = new FakeTimerFactory();
        var engine = new AnimationEngine(timers, new AnimationOptions { Mode = FrameMode.Fixed, FixedIntervalMs = 200 });
        engine.SetActiveSet(Set(3));
        engine.Start();

        Assert.Equal(TimeSpan.FromMilliseconds(200), timers.Last.Interval);
        Assert.True(timers.Last.IsRunning);

        timers.Last.Fire();
        Assert.Equal(1, engine.FrameIndex);
    }

    [Fact]
    public void Options_AreNormalized()
    {
        var engine = new AnimationEngine(new FakeTimerFactory(), new AnimationOptions
        {
            FixedIntervalMs = 1,
            RandomMinMs = 900,
            RandomMaxMs = 100,
            KeysPerFrame = 0,
            IdleReturnMs = -5,
        });

        Assert.Equal(AnimationOptions.MinIntervalMs, engine.Options.FixedIntervalMs);
        Assert.Equal(100, engine.Options.RandomMinMs);
        Assert.Equal(900, engine.Options.RandomMaxMs);
        Assert.Equal(1, engine.Options.KeysPerFrame);
        Assert.Equal(0, engine.Options.IdleReturnMs);
    }

    [Fact]
    public void ApplyOptions_WhileRunning_SwapsSchedulerAndKeepsRunning()
    {
        var timers = new FakeTimerFactory();
        var engine = new AnimationEngine(timers, new AnimationOptions { Mode = FrameMode.Fixed });
        engine.SetActiveSet(Set(3));
        engine.Start();
        var fixedTimer = timers.Last;

        engine.ApplyOptions(new AnimationOptions { Mode = FrameMode.Keystroke, KeysPerFrame = 1, IdleReturnMs = 0 });

        Assert.True(fixedTimer.IsDisposed);
        Assert.True(engine.IsRunning);

        fixedTimer.Fire();
        Assert.Equal(0, engine.FrameIndex);

        engine.OnKeystroke();
        Assert.Equal(1, engine.FrameIndex);
    }

    [Fact]
    public void ApplyOptions_WhileStopped_DoesNotStartScheduler()
    {
        var timers = new FakeTimerFactory();
        var engine = new AnimationEngine(timers, new AnimationOptions { Mode = FrameMode.Fixed });

        engine.ApplyOptions(new AnimationOptions { Mode = FrameMode.Random });

        Assert.False(engine.IsRunning);
        Assert.False(timers.Last.IsRunning);
    }

    [Fact]
    public void Stop_StopsTimer_And_TickNoLongerAdvances()
    {
        var timers = new FakeTimerFactory();
        var engine = new AnimationEngine(timers);
        engine.SetActiveSet(Set(3));
        engine.Start();
        engine.Stop();

        timers.Last.Fire();

        Assert.False(timers.Last.IsRunning);
        Assert.Equal(0, engine.FrameIndex);
    }

    [Fact]
    public void Dispose_DisposesScheduler()
    {
        var timers = new FakeTimerFactory();
        var engine = new AnimationEngine(timers);
        engine.Start();

        engine.Dispose();

        Assert.True(timers.Last.IsDisposed);
        Assert.False(engine.IsRunning);
    }
}
