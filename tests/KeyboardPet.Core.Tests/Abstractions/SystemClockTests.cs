using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Tests.Abstractions;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;

        var actual = clock.UtcNow;

        var after = DateTimeOffset.UtcNow;
        Assert.InRange(actual, before, after);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }
}
