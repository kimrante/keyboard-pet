namespace KeyboardPet.Core.Animation;

public enum FrameMode
{
    /// <summary>고정 간격마다 다음 프레임</summary>
    Fixed,

    /// <summary>[Min, Max] 범위의 난수 간격마다 다음 프레임(프레임마다 재산출)</summary>
    Random,

    /// <summary>키 다운 N회마다 다음 프레임. 타이머 없음</summary>
    Keystroke,

    /// <summary>최근 타이핑 속도에 따라 간격이 느린 값~빠른 값 사이에서 자동 조절</summary>
    Adaptive,
}

/// <summary>
/// 프레임 전환 정책 설정. AppSettings의 일부로 저장된다.
/// </summary>
public sealed record AnimationOptions
{
    public const int MinIntervalMs = 16;
    public const int MaxIntervalMs = 10_000;
    public const double MinTargetKeysPerSecond = 0.5;
    public const double MaxTargetKeysPerSecond = 20.0;
    public const int MinWindowMs = 250;
    public const int MaxWindowMs = 10_000;

    /// <summary>기본값은 타이핑에 맞춰 프레임이 넘어가는 타수 기반이다.</summary>
    public FrameMode Mode { get; init; } = FrameMode.Keystroke;

    public int FixedIntervalMs { get; init; } = 200;

    public int RandomMinMs { get; init; } = 100;

    public int RandomMaxMs { get; init; } = 600;

    /// <summary>테스트 재현용. null이면 시스템 난수</summary>
    public int? RandomSeed { get; init; }

    public int KeysPerFrame { get; init; } = 1;

    /// <summary>
    /// 무입력 상태가 이 시간 이상 지속되면 0번 프레임으로 복귀. 0이면 비활성.
    /// 타수 기반 모드와 타이핑 속도 연동 모드가 함께 사용한다.
    /// </summary>
    public int IdleReturnMs { get; init; } = 2000;

    /// <summary>타이핑 속도 연동: 거의 안 칠 때의 간격</summary>
    public int AdaptiveSlowMs { get; init; } = 600;

    /// <summary>타이핑 속도 연동: 목표 속도 이상으로 칠 때의 간격</summary>
    public int AdaptiveFastMs { get; init; } = 80;

    /// <summary>타이핑 속도 연동: 이 속도(타/초)에 도달하면 가장 빠른 간격이 된다. 6타/초 ≈ 360타/분</summary>
    public double AdaptiveTargetKeysPerSecond { get; init; } = 6.0;

    /// <summary>타이핑 속도 연동: 속도를 측정하는 최근 구간 길이</summary>
    public int AdaptiveWindowMs { get; init; } = 2000;

    /// <summary>범위를 벗어난 값을 유효 범위로 보정한 복사본을 반환한다.</summary>
    public AnimationOptions Normalized()
    {
        var min = Math.Clamp(RandomMinMs, MinIntervalMs, MaxIntervalMs);
        var max = Math.Clamp(RandomMaxMs, MinIntervalMs, MaxIntervalMs);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        var slow = Math.Clamp(AdaptiveSlowMs, MinIntervalMs, MaxIntervalMs);
        var fast = Math.Clamp(AdaptiveFastMs, MinIntervalMs, MaxIntervalMs);
        if (fast > slow)
        {
            (slow, fast) = (fast, slow);
        }

        var target = double.IsFinite(AdaptiveTargetKeysPerSecond)
            ? Math.Clamp(AdaptiveTargetKeysPerSecond, MinTargetKeysPerSecond, MaxTargetKeysPerSecond)
            : 6.0;

        return this with
        {
            FixedIntervalMs = Math.Clamp(FixedIntervalMs, MinIntervalMs, MaxIntervalMs),
            RandomMinMs = min,
            RandomMaxMs = max,
            KeysPerFrame = Math.Max(1, KeysPerFrame),
            IdleReturnMs = Math.Max(0, IdleReturnMs),
            AdaptiveSlowMs = slow,
            AdaptiveFastMs = fast,
            AdaptiveTargetKeysPerSecond = target,
            AdaptiveWindowMs = Math.Clamp(AdaptiveWindowMs, MinWindowMs, MaxWindowMs),
        };
    }
}
