namespace KeyboardPet.Core.Abstractions;

/// <summary>
/// 현재 시각을 제공한다. 테스트에서는 가짜 구현으로 시간을 제어한다.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
