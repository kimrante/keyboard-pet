using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Rules;

namespace KeyboardPet.Core.Tests.Rules;

public class KeySpecTests
{
    private static KeyEvent Down(int vk, KeyModifiers mods = KeyModifiers.None) => new(vk, true, mods, false);

    [Theory]
    [InlineData("Enter", 0x0D, KeyModifiers.None)]
    [InlineData("enter", 0x0D, KeyModifiers.None)]
    [InlineData("a", 0x41, KeyModifiers.None)]
    [InlineData("Ctrl+S", 0x53, KeyModifiers.Control)]
    [InlineData("ctrl + shift + a", 0x41, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Alt+F4", 0x73, KeyModifiers.Alt)]
    [InlineData("Win+D", 0x44, KeyModifiers.Win)]
    [InlineData("0x41", 0x41, KeyModifiers.None)]
    [InlineData("65", 0x41, KeyModifiers.None)]
    [InlineData("한영", 0x15, KeyModifiers.None)]
    [InlineData("NumPad5", 0x65, KeyModifiers.None)]
    public void Parse_ValidSpecs(string text, int vk, KeyModifiers mods)
    {
        var spec = KeySpec.Parse(text);

        Assert.Equal(vk, spec.VirtualKey);
        Assert.Equal(mods, spec.Modifiers);
        Assert.False(spec.IsWildcard);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAKey")]
    [InlineData("Ctrl+")]
    [InlineData("Foo+A")]
    [InlineData("0x1FF")]
    public void TryParse_InvalidSpecs_ReturnsFalse(string text)
    {
        Assert.False(KeySpec.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeySpec.Parse(text));
    }

    [Fact]
    public void Parse_Wildcard()
    {
        var spec = KeySpec.Parse("*");

        Assert.True(spec.IsWildcard);
        Assert.True(spec.Matches(Down(0x41)));
        Assert.True(spec.Matches(Down(0x0D, KeyModifiers.Shift)));
    }

    [Fact]
    public void Wildcard_DoesNotMatchPureModifierKeys()
    {
        var spec = KeySpec.Wildcard;

        Assert.False(spec.Matches(Down(0x10)));  // Shift
        Assert.False(spec.Matches(Down(0xA2)));  // LControl
        Assert.False(spec.Matches(Down(0x5B)));  // LWin
    }

    [Fact]
    public void SpecWithoutModifiers_MatchesAnyModifierState()
    {
        var spec = KeySpec.Parse("A");

        Assert.True(spec.Matches(Down(0x41)));
        Assert.True(spec.Matches(Down(0x41, KeyModifiers.Shift)));
        Assert.True(spec.Matches(Down(0x41, KeyModifiers.Control)));
        Assert.False(spec.Matches(Down(0x42)));
    }

    [Fact]
    public void SpecWithModifiers_RequiresExactModifierState()
    {
        var spec = KeySpec.Parse("Ctrl+S");

        Assert.True(spec.Matches(Down(0x53, KeyModifiers.Control)));
        Assert.False(spec.Matches(Down(0x53)));
        Assert.False(spec.Matches(Down(0x53, KeyModifiers.Control | KeyModifiers.Shift)));
    }

    [Theory]
    [InlineData("ctrl+shift+a", "Ctrl+Shift+A")]
    [InlineData("enter", "Enter")]
    [InlineData("*", "*")]
    [InlineData("alt+*", "Alt+*")]
    [InlineData("0xFE", "0xFE")]
    public void ToString_IsCanonical(string input, string expected)
    {
        Assert.Equal(expected, KeySpec.Parse(input).ToString());
    }
}
