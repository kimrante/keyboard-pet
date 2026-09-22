using KeyboardPet.Core.Input;

namespace KeyboardPet.Core.Tests.Input;

public class AutoRepeatDetectorTests
{
    private const int VkA = 0x41;
    private const int VkB = 0x42;

    [Fact]
    public void FirstKeyDown_IsNotRepeat()
    {
        var detector = new AutoRepeatDetector();

        Assert.False(detector.OnKeyDown(VkA, 1000));
    }

    [Fact]
    public void KeyDown_WithoutKeyUp_WithinWindow_IsRepeat()
    {
        var detector = new AutoRepeatDetector();
        detector.OnKeyDown(VkA, 1000);

        Assert.True(detector.OnKeyDown(VkA, 1030));
        Assert.True(detector.OnKeyDown(VkA, 1060));
    }

    [Fact]
    public void KeyDown_AfterKeyUp_IsNotRepeat()
    {
        var detector = new AutoRepeatDetector();
        detector.OnKeyDown(VkA, 1000);
        detector.OnKeyUp(VkA);

        Assert.False(detector.OnKeyDown(VkA, 1030));
    }

    [Fact]
    public void KeyDown_AfterMissedKeyUp_BeyondWindow_IsNotRepeat()
    {
        var detector = new AutoRepeatDetector(TimeSpan.FromMilliseconds(500));
        detector.OnKeyDown(VkA, 1000);

        Assert.False(detector.OnKeyDown(VkA, 1600));
    }

    [Fact]
    public void DifferentKeys_AreTrackedIndependently()
    {
        var detector = new AutoRepeatDetector();
        detector.OnKeyDown(VkA, 1000);

        Assert.False(detector.OnKeyDown(VkB, 1010));
        Assert.True(detector.OnKeyDown(VkA, 1020));
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var detector = new AutoRepeatDetector();
        detector.OnKeyDown(VkA, 1000);
        detector.Reset();

        Assert.False(detector.OnKeyDown(VkA, 1010));
    }
}
