using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Tests.Animation;

/// <summary>테스트에서 수동으로 시간을 전진시키는 시계.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset? start = null)
    {
        UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(int milliseconds) => UtcNow = UtcNow.AddMilliseconds(milliseconds);
}
