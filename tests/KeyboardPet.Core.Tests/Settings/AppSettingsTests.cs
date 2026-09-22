using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void RulesEqual_ComparesByValueIncludingKeys()
    {
        var a = new[] { new KeyRule(new[] { "Enter", "Space" }, "jump", 800, true) };
        var same = new[] { new KeyRule(new[] { "Enter", "Space" }, "jump", 800, true) };
        var differentKeys = new[] { new KeyRule(new[] { "Enter" }, "jump", 800, true) };
        var differentHold = new[] { new KeyRule(new[] { "Enter", "Space" }, "jump", 900, true) };

        Assert.True(AppSettings.RulesEqual(a, same));
        Assert.False(AppSettings.RulesEqual(a, differentKeys));
        Assert.False(AppSettings.RulesEqual(a, differentHold));
        Assert.False(AppSettings.RulesEqual(a, Array.Empty<KeyRule>()));
    }

    [Fact]
    public void FrameSetsEqual_ComparesByValue()
    {
        var a = new[] { new FrameSetSettings("cat", @"C:\a") };
        var same = new[] { new FrameSetSettings("cat", @"C:\a") };
        var other = new[] { new FrameSetSettings("cat", @"C:\b") };

        Assert.True(AppSettings.FrameSetsEqual(a, same));
        Assert.False(AppSettings.FrameSetsEqual(a, other));
    }

    [Fact]
    public void Default_HasBuiltInRules()
    {
        var s = AppSettings.Default;

        Assert.Equal("jump", s.Rules[0].FrameSet);
        Assert.Equal("typing", s.Rules[1].FrameSet);
        Assert.Equal(AppSettings.BuiltInDefaultSet, s.DefaultFrameSet);
    }
}
