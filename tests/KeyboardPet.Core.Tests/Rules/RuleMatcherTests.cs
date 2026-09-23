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
            new KeyRule("Ctrl+S", FrameIndex: 0),
            new KeyRule("S", FrameIndex: 1),
            new KeyRule("*", FrameIndex: 2),
        });

        Assert.Equal(0, matcher.Match(Down(VkS, KeyModifiers.Control))?.FrameIndex);
        Assert.Equal(1, matcher.Match(Down(VkS))?.FrameIndex);
        Assert.Equal(1, matcher.Match(Down(VkS, KeyModifiers.Shift))?.FrameIndex);
        Assert.Equal(2, matcher.Match(Down(VkEnter))?.FrameIndex);
    }

    [Fact]
    public void RuleOrderMatters_BroadRuleAboveSpecificSwallows()
    {
        var matcher = new RuleMatcher(new[]
        {
            new KeyRule("S", FrameIndex: 1),
            new KeyRule("Ctrl+S", FrameIndex: 0),
        });

        Assert.Equal(1, matcher.Match(Down(VkS, KeyModifiers.Control))?.FrameIndex);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var matcher = new RuleMatcher(new[] { new KeyRule("Enter", FrameIndex: 0) });

        Assert.Null(matcher.Match(Down(VkS)));
    }

    [Fact]
    public void RuleWithMultipleKeys_MatchesAny()
    {
        var matcher = new RuleMatcher(new[]
        {
            new KeyRule(new[] { "A", "S", "D", "F" }, FrameIndex: 0),
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
            new KeyRule(new[] { "Bogus", "Enter" }, FrameIndex: 5),
            new KeyRule("AlsoBogus", FrameIndex: 6),
        });

        Assert.Single(matcher.Rules);
        Assert.Equal(5, matcher.Match(Down(VkEnter))?.FrameIndex);
        Assert.Equal(2, matcher.Errors.Count);
        Assert.Contains(matcher.Errors, e => e.Contains("Bogus"));
        Assert.Contains(matcher.Errors, e => e.Contains("AlsoBogus"));
    }

    [Fact]
    public void EmptyRules_NeverMatch()
    {
        var matcher = new RuleMatcher(Array.Empty<KeyRule>());

        Assert.Null(matcher.Match(Down(VkEnter)));
        Assert.Empty(matcher.Errors);
    }
}
