using KeyboardPet.Core.Animation;

namespace KeyboardPet.Core.Tests.Animation;

public class AdaptiveSchedulerTests
{
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan IdleReturn = TimeSpan.FromMilliseconds(2000);
    private const double TargetKps = 5.0;

    private sealed class Harness
    {
        public FakeTimerFactory Timers { get; } = new();
        public FakeClock Clock { get; } = new();
        public int Advanced;
        public int Returned;
        public AdaptiveScheduler Scheduler { get; }

        public Harness(TimeSpan? idleReturn = null, TimeSpan? window = null)
        {
            Scheduler = new AdaptiveScheduler(
                Timers, Clock, Slow, Fast, TargetKps, window ?? Window, idleReturn ?? IdleReturn,
                () => Advanced++, () => Returned++);
        }

        public FakeTimer Timer => Timers.Last;

        /// <summary>시간을 흘리고 타이머 틱을 발생시킨다.</summary>
        public void Tick(int elapsedMs)
        {
            Clock.Advance(elapsedMs);
            Timer.Fire();
        }

        public void Type(int count, int gapMs)
        {
            for (var i = 0; i < count; i++)
            {
                Clock.Advance(gapMs);
                Scheduler.OnKeystroke();
            }
        }
    }

    [Fact]
    public void Start_UsesSlowInterval_AndBeginsIdleWhenIdleReturnIsSet()
    {
        var h = new Harness();

        h.Scheduler.Start();

        Assert.Equal(Slow, h.Timer.Interval);
        Assert.True(h.Scheduler.IsRunning);
        Assert.True(h.Scheduler.IsIdle);
    }

    [Fact]
    public void TickWhileIdle_DoesNotAdvance()
    {
        var h = new Harness();
        h.Scheduler.Start();

        h.Tick(600);
        h.Tick(600);

        Assert.Equal(0, h.Advanced);
        Assert.Equal(1, h.Returned);   // Start 시 복귀 프레임으로 한 번, 이후 틱에서는 반복하지 않음
    }

    [Fact]
    public void Keystroke_WakesUp_AndTickAdvances()
    {
        var h = new Harness();
        h.Scheduler.Start();
        var startsBefore = h.Timer.StartCount;

        h.Type(1, 100);

        Assert.False(h.Scheduler.IsIdle);
        Assert.Equal(startsBefore + 1, h.Timer.StartCount);

        h.Tick(300);
        Assert.Equal(1, h.Advanced);
    }

    [Fact]
    public void FastTyping_ReachesFastInterval()
    {
        var h = new Harness();
        h.Scheduler.Start();

        // 2초 창 안에 12타 = 6타/초 > 목표 5타/초
        h.Type(12, 100);

        Assert.Equal(Fast, h.Timer.Interval);
        Assert.InRange(h.Scheduler.CurrentKeysPerSecond, 5.9, 6.1);
    }

    [Fact]
    public void ModerateTyping_InterpolatesBetweenSlowAndFast()
    {
        var h = new Harness();
        h.Scheduler.Start();

        // 2초 창 안에 5타 = 2.5타/초 = 목표의 절반 → 간격은 slow와 fast의 중간
        h.Type(5, 100);

        var expected = (Slow.TotalMilliseconds + Fast.TotalMilliseconds) / 2;
        Assert.InRange(h.Timer.Interval.TotalMilliseconds, expected - 1, expected + 1);
    }

    [Fact]
    public void OldKeystrokes_FallOutOfWindow()
    {
        // 측정 구간 1초, 복귀 3초: 구간 밖으로 빠졌지만 아직 복귀 전인 상태를 만들 수 있다.
        var h = new Harness(idleReturn: TimeSpan.FromMilliseconds(3000), window: TimeSpan.FromMilliseconds(1000));
        h.Scheduler.Start();
        h.Type(10, 50);   // 10타가 0.5초 안에 → 10타/초 > 목표
        Assert.Equal(Fast, h.Timer.Interval);

        h.Tick(1600);     // 마지막 키로부터 1.6초: 구간(1초) 밖, 복귀(3초) 전

        Assert.False(h.Scheduler.IsIdle);
        Assert.Equal(1, h.Advanced);
        Assert.Equal(Slow, h.Timer.Interval);
        Assert.Equal(0.0, h.Scheduler.CurrentKeysPerSecond);
    }

    [Fact]
    public void NoInputForIdleReturn_ReturnsToIdleOnce_AndStopsAdvancing()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Type(3, 100);
        h.Tick(200);
        Assert.Equal(1, h.Advanced);

        h.Tick(2100);   // 마지막 키로부터 2.3초 경과 → 복귀
        h.Tick(600);
        h.Tick(600);

        Assert.Equal(2, h.Returned);   // Start 시 1회 + 만료 시 1회, 이후 반복 없음
        Assert.Equal(1, h.Advanced);
        Assert.True(h.Scheduler.IsIdle);
        Assert.Equal(Slow, h.Timer.Interval);
    }

    [Fact]
    public void IdleReturnZero_KeepsCyclingAtSlowIntervalWithoutInput()
    {
        var h = new Harness(TimeSpan.Zero);
        h.Scheduler.Start();

        Assert.False(h.Scheduler.IsIdle);
        h.Tick(600);
        h.Tick(600);

        Assert.Equal(2, h.Advanced);
        Assert.Equal(0, h.Returned);
        Assert.Equal(Slow, h.Timer.Interval);
    }

    [Fact]
    public void KeystrokesAreIgnoredWhenNotRunning()
    {
        var h = new Harness();

        h.Scheduler.OnKeystroke();

        Assert.False(h.Scheduler.IsRunning);
        Assert.Equal(0.0, h.Scheduler.CurrentKeysPerSecond);
    }

    [Fact]
    public void Stop_StopsTimer_AndClearsHistory()
    {
        var h = new Harness();
        h.Scheduler.Start();
        h.Type(5, 100);

        h.Scheduler.Stop();

        Assert.False(h.Timer.IsRunning);
        Assert.Equal(0.0, h.Scheduler.CurrentKeysPerSecond);
    }

    [Fact]
    public void SwappedSlowFast_AreCorrected()
    {
        var timers = new FakeTimerFactory();
        using var scheduler = new AdaptiveScheduler(
            timers, new FakeClock(), Fast, Slow, TargetKps, Window, IdleReturn, () => { }, () => { });

        scheduler.Start();

        Assert.Equal(Slow, timers.Last.Interval);
    }

    [Fact]
    public void Dispose_DisposesTimer()
    {
        var h = new Harness();

        h.Scheduler.Dispose();

        Assert.True(h.Timer.IsDisposed);
    }
}
