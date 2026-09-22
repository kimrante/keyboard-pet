using KeyboardPet.Core.Animation;

namespace KeyboardPet.Core.Tests.Animation;

public class FrameSetTests
{
    [Fact]
    public void WithoutLoopFrames_AllFramesCycle()
    {
        var set = new FrameSet("s", 3);

        Assert.Equal(3, set.LoopCount);
        Assert.Equal(0, set.FirstLoopIndex);
        Assert.Equal(1, set.NextLoopIndex(0));
        Assert.Equal(0, set.NextLoopIndex(2));
        Assert.Equal(0, set.NextLoopIndex(99));
        Assert.True(set.IsInLoop(2));
        Assert.False(set.IsInLoop(3));
    }

    [Fact]
    public void SingleOrEmptySet_NeverAdvances()
    {
        Assert.Equal(0, new FrameSet("one", 1).NextLoopIndex(0));
        Assert.Equal(0, FrameSet.Empty.NextLoopIndex(0));
    }

    [Fact]
    public void LoopFrames_CycleOnlyThroughListedFrames()
    {
        var set = new FrameSet("s", 4, LoopFrames: new[] { 0, 1, 2 });

        Assert.Equal(3, set.LoopCount);
        Assert.Equal(new[] { 1, 2, 0 }, new[] { set.NextLoopIndex(0), set.NextLoopIndex(1), set.NextLoopIndex(2) });
        Assert.False(set.IsInLoop(3));
        Assert.True(set.IsInLoop(1));
    }

    [Fact]
    public void LoopFrames_FromOutsideLoop_GoesToFirstLoopFrame()
    {
        var set = new FrameSet("s", 4, LoopFrames: new[] { 2, 1 });

        Assert.Equal(2, set.FirstLoopIndex);
        Assert.Equal(2, set.NextLoopIndex(3));
        Assert.Equal(1, set.NextLoopIndex(2));
        Assert.Equal(2, set.NextLoopIndex(1));
    }

    [Fact]
    public void EmptyLoop_NeverAdvances_AndFirstIsZero()
    {
        var set = new FrameSet("s", 4, LoopFrames: Array.Empty<int>());

        Assert.Equal(0, set.LoopCount);
        Assert.Equal(0, set.FirstLoopIndex);
        Assert.Equal(3, set.NextLoopIndex(3));
    }
}
