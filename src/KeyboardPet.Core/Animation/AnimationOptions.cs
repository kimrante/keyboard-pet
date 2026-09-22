namespace KeyboardPet.Core.Animation;

public enum FrameMode
{
    /// <summary>고정 간격마다 다음 프레임</summary>
    Fixed,

    /// <summary>[Min, Max] 범위의 난수 간격마다 다음 프레임(프레임마다 재산출)</summary>
    Random,

    /// <summary>키 다운 N회마다 다음 프레임. 타이머 없음</summary>
    Keystroke,
}

/// <summary>
/// 프레임 전환 정책 설정. M4에서 AppSettings의 일부로 저장된다.
/// </summary>
public sealed record AnimationOptions
{
    public const int MinIntervalMs = 16;
    public const int MaxIntervalMs = 10_000;

    public FrameMode Mode { get; init; } = FrameMode.Fixed;

    public int FixedIntervalMs { get; init; } = 200;

    public int RandomMinMs { get; init; } = 100;

    public int RandomMaxMs { get; init; } = 600;

    /// <summary>테스트 재현용. null이면 시스템 난수</summary>
    public int? RandomSeed { get; init; }

    public int KeysPerFrame { get; init; } = 1;

    /// <summary>무입력 상태가 이 시간 이상 지속되면 0번 프레임으로 복귀. 0이면 비활성</summary>
    public int IdleReturnMs { get; init; } = 2000;

    /// <summary>범위를 벗어난 값을 유효 범위로 보정한 복사본을 반환한다.</summary>
    public AnimationOptions Normalized()
    {
        var min = Math.Clamp(RandomMinMs, MinIntervalMs, MaxIntervalMs);
        var max = Math.Clamp(RandomMaxMs, MinIntervalMs, MaxIntervalMs);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        return this with
        {
            FixedIntervalMs = Math.Clamp(FixedIntervalMs, MinIntervalMs, MaxIntervalMs),
            RandomMinMs = min,
            RandomMaxMs = max,
            KeysPerFrame = Math.Max(1, KeysPerFrame),
            IdleReturnMs = Math.Max(0, IdleReturnMs),
        };
    }
}
