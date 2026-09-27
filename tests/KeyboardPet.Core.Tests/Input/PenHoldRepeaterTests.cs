using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Input;
using KeyboardPet.Core.Tests.Animation;

namespace KeyboardPet.Core.Tests.Input;

public class PenHoldRepeaterTests
{
    private static (PenHoldRepeater Repeater, FakeTimerFactory Timers, Func<int> Strokes, Action<bool> SetDown) Build()
    {
        var timers = new FakeTimerFactory();
        var strokes = 0;
        var down = true;
        var repeater = new PenHoldRepeater(timers, () => down, () => strokes++);
        return (repeater, timers, () => strokes, value => down = value);
    }

    [Fact]
    public void PenDown_StrokesImmediately_ThenEveryTick()
    {
        var (repeater, timers, strokes, _) = Build();

        repeater.PenDown();
        Assert.Equal(1, strokes());
        Assert.True(repeater.IsHolding);

        timers.Last.Fire();
        timers.Last.Fire();
        Assert.Equal(3, strokes());
    }

    [Fact]
    public void PenUp_StopsRepeating()
    {
        var (repeater, timers, strokes, _) = Build();
        repeater.PenDown();

        repeater.PenUp();
        timers.Last.Fire();

        Assert.Equal(1, strokes());
        Assert.False(repeater.IsHolding);
    }

    [Fact]
    public void MissedPenUp_StopsWhenNoLongerDown()
    {
        var (repeater, timers, strokes, setDown) = Build();
        repeater.PenDown();
        setDown(false);

        timers.Last.Fire();

        Assert.Equal(1, strokes());
        Assert.False(repeater.IsHolding);
    }

    [Fact]
    public void Interval_IsClampedAndAppliedToTimer()
    {
        var (repeater, timers, _, _) = Build();

        repeater.Interval = TimeSpan.FromMilliseconds(5);
        Assert.Equal(TimeSpan.FromMilliseconds(PenHoldRepeater.MinIntervalMs), timers.Last.Interval);

        repeater.Interval = TimeSpan.FromMilliseconds(200);
        Assert.Equal(TimeSpan.FromMilliseconds(200), timers.Last.Interval);
    }

    [Fact]
    public void HoldingPen_AdvancesKeystrokeAnimation()
    {
        // 타수 기반 모드에서 펜을 대고만 있어도 프레임이 넘어간다.
        var timers = new FakeTimerFactory();
        var engine = new AnimationEngine(timers, new AnimationOptions { Mode = FrameMode.Keystroke, KeysPerFrame = 1, IdleReturnMs = 0 });
        engine.SetActiveSet(new FrameSet("cat", 4));
        engine.Start();
        var repeater = new PenHoldRepeater(timers, () => true, engine.OnKeystroke);

        repeater.PenDown();
        timers.Last.Fire();
        timers.Last.Fire();

        Assert.Equal(3, engine.FrameIndex);
    }

    [Fact]
    public void Dispose_DisposesTimer()
    {
        var (repeater, timers, _, _) = Build();

        repeater.Dispose();

        Assert.True(timers.Last.IsDisposed);
    }
}
