using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Rules;

namespace KeyboardPet.Core.Tests.Rules;

public class RuleMatcherTests
{
    private const int VkS = 0x53;
    private const int VkEnter = 0x0D;

    private static KeyEvent Down(int vk, KeyModifiers mods = KeyModifiers.None) => new(vk, true, mods, false);

    [Fact]
    public void FirstMatchingRuleWins_InOrder()
    {
        var matcher = new RuleMatcher(new[]
        {
            new KeyRule("Ctrl+S", "save"),
            new KeyRule("S", "letter"),
            new KeyRule("*", "any"),
        });

        Assert.Equal("save", matcher.Match(Down(VkS, KeyModifiers.Control))?.FrameSet);
        Assert.Equal("letter", matcher.Match(Down(VkS))?.FrameSet);
        Assert.Equal("letter", matcher.Match(Down(VkS, KeyModifiers.Shift))?.FrameSet);
        Assert.Equal("any", matcher.Match(Down(VkEnter))?.FrameSet);
    }

    [Fact]
    public void RuleOrderMatters_BroadRuleAboveSpecificSwallows()
    {
        var matcher = new RuleMatcher(new[]
        {
            new KeyRule("S", "letter"),
            new KeyRule("Ctrl+S", "save"),
        });

        Assert.Equal("letter", matcher.Match(Down(VkS, KeyModifiers.Control))?.FrameSet);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var matcher = new RuleMatcher(new[] { new KeyRule("Enter", "jump") });

        Assert.Null(matcher.Match(Down(VkS)));
    }

    [Fact]
    public void RuleWithMultipleKeys_MatchesAny()
    {
        var matcher = new RuleMatcher(new[]
        {
            new KeyRule(new[] { "A", "S", "D", "F" }, "left"),
        });

        Assert.NotNull(matcher.Match(Down(0x41)));
        Assert.NotNull(matcher.Match(Down(0x46)));
        Assert.Null(matcher.Match(Down(0x47)));
    }

    [Fact]
    public void InvalidKeyNames_AreSkippedWithErrors()
    {
        var matcher = new RuleMatcher(new[]
        {
            new KeyRule(new[] { "Bogus", "Enter" }, "partial"),
            new KeyRule("AlsoBogus", "dropped"),
        });

        Assert.Single(matcher.Rules);
        Assert.Equal("partial", matcher.Match(Down(VkEnter))?.FrameSet);
        Assert.Equal(2, matcher.Errors.Count);
        Assert.Contains(matcher.Errors, e => e.Contains("Bogus"));
        Assert.Contains(matcher.Errors, e => e.Contains("dropped"));
    }

    [Fact]
    public void EmptyRules_NeverMatch()
    {
        var matcher = new RuleMatcher(Array.Empty<KeyRule>());

        Assert.Null(matcher.Match(Down(VkEnter)));
        Assert.Empty(matcher.Errors);
    }
}
