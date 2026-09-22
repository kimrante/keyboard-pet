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
        var differentFrame = new[] { new KeyRule(new[] { "Enter", "Space" }, "jump", 800, true, FrameIndex: 1) };

        Assert.True(AppSettings.RulesEqual(a, same));
        Assert.False(AppSettings.RulesEqual(a, differentKeys));
        Assert.False(AppSettings.RulesEqual(a, differentHold));
        Assert.False(AppSettings.RulesEqual(a, differentFrame));
        Assert.False(AppSettings.RulesEqual(a, Array.Empty<KeyRule>()));
    }

    [Fact]
    public void FrameSetsEqual_ComparesByValue_IncludingFrameList()
    {
        var a = new[] { new FrameSetSettings("cat", @"C:\a", new[] { "1.png", "2.png" }) };
        var same = new[] { new FrameSetSettings("cat", @"C:\a", new[] { "1.png", "2.png" }) };
        var reordered = new[] { new FrameSetSettings("cat", @"C:\a", new[] { "2.png", "1.png" }) };
        var noList = new[] { new FrameSetSettings("cat", @"C:\a") };
        var otherFolder = new[] { new FrameSetSettings("cat", @"C:\b", new[] { "1.png", "2.png" }) };

        Assert.True(AppSettings.FrameSetsEqual(a, same));
        Assert.False(AppSettings.FrameSetsEqual(a, reordered));
        Assert.False(AppSettings.FrameSetsEqual(a, noList));
        Assert.False(AppSettings.FrameSetsEqual(a, otherFolder));
        Assert.True(AppSettings.FrameSetsEqual(noList, new[] { new FrameSetSettings("cat", @"C:\a") }));
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
