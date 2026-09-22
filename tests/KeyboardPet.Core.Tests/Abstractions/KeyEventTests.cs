using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Tests.Abstractions;

public class KeyEventTests
{
    [Fact]
    public void KeyEvent_IsValueEqual()
    {
        var a = new KeyEvent(0x41, IsDown: true, KeyModifiers.Control | KeyModifiers.Shift, IsAutoRepeat: false);
        var b = new KeyEvent(0x41, IsDown: true, KeyModifiers.Control | KeyModifiers.Shift, IsAutoRepeat: false);

        Assert.Equal(a, b);
        Assert.True(a.Modifiers.HasFlag(KeyModifiers.Control));
        Assert.False(a.Modifiers.HasFlag(KeyModifiers.Alt));
    }
}
