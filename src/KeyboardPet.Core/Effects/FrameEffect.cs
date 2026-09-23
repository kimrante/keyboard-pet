using System.Text.Json.Serialization;

namespace KeyboardPet.Core.Effects;

/// <summary>펫 이미지에 입히는 움직임 효과의 종류.</summary>
public enum FrameEffectKind
{
    /// <summary>위아래로 흔들리기.</summary>
    BobVertical,

    /// <summary>좌우로 흔들리기.</summary>
    ShakeHorizontal,

    /// <summary>작아졌다가 돌아오기(바닥 기준).</summary>
    Shrink,

    /// <summary>커졌다가 돌아오기(바닥 기준).</summary>
    Grow,

    /// <summary>바닥에서 튀어오르기.</summary>
    Bounce,

    /// <summary>바닥을 축으로 좌우로 기울기.</summary>
    Tilt,

    /// <summary>가로·세로가 번갈아 늘었다 줄기(말랑말랑).</summary>
    Squash,

    /// <summary>투명해졌다가 돌아오기.</summary>
    Blink,
}

/// <summary>
/// 움직임 효과 하나. 세트 프로필(프레임별 효과)과 키 매핑 규칙(키를 누른 동안의 효과)에 저장된다.
/// 여러 효과를 함께 두면 합성된다(<see cref="EffectTransform.Combine"/>).
/// </summary>
/// <param name="Kind">효과 종류</param>
/// <param name="Strength">강도 0~100(%)</param>
/// <param name="PeriodMs">한 번 움직이는 데 걸리는 시간(ms). 작을수록 빠르다</param>
/// <param name="Frames">적용할 프레임 번호(0부터). null이면 모든 프레임. 키 매핑 규칙의 효과에서는 쓰지 않는다</param>
[method: JsonConstructor]
public sealed record FrameEffect(
    FrameEffectKind Kind,
    int Strength = FrameEffect.DefaultStrength,
    int PeriodMs = FrameEffect.DefaultPeriodMs,
    IReadOnlyList<int>? Frames = null)
{
    public const int MinStrength = 0;
    public const int MaxStrength = 100;
    public const int DefaultStrength = 50;
    public const int MinPeriodMs = 100;
    public const int MaxPeriodMs = 5000;
    public const int DefaultPeriodMs = 800;

    public bool AppliesTo(int frameIndex) => Frames is null || Frames.Contains(frameIndex);

    /// <summary>강도가 0이면 움직이지 않으므로 계산할 필요가 없다.</summary>
    [JsonIgnore]
    public bool IsVisible => Strength > MinStrength;

    /// <summary>범위 밖 값과 중복·음수 프레임 번호를 보정한 복사본.</summary>
    public FrameEffect Normalized() => this with
    {
        Kind = Enum.IsDefined(Kind) ? Kind : FrameEffectKind.BobVertical,
        Strength = Math.Clamp(Strength, MinStrength, MaxStrength),
        PeriodMs = Math.Clamp(PeriodMs, MinPeriodMs, MaxPeriodMs),
        Frames = Frames?.Where(f => f >= 0).Distinct().Order().ToList(),
    };

    /// <summary>효과가 시작된 뒤 <paramref name="elapsedMs"/>가 지난 시점의 변형.</summary>
    public EffectTransform Evaluate(double elapsedMs)
    {
        var s = Math.Clamp(Strength, MinStrength, MaxStrength) / 100.0;
        var period = Math.Clamp(PeriodMs, MinPeriodMs, MaxPeriodMs);
        var phase = Math.Max(0, elapsedMs) / period;

        // 종류마다 하나만 쓰므로 필요한 것만 계산한다(매 프레임 호출).
        double Wave() => Math.Sin(2 * Math.PI * phase);               // -1..1, 0에서 시작
        double Pulse() => 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase);  // 0..1..0, 0에서 시작

        return Kind switch
        {
            FrameEffectKind.BobVertical => EffectTransform.Identity with { OffsetY = -Coefficients.Bob * s * Wave() },
            FrameEffectKind.ShakeHorizontal => EffectTransform.Identity with { OffsetX = Coefficients.Shake * s * Wave() },
            FrameEffectKind.Shrink => Uniform(1 - Coefficients.Shrink * s * Pulse()),
            FrameEffectKind.Grow => Uniform(1 + Coefficients.Grow * s * Pulse()),
            FrameEffectKind.Bounce => EffectTransform.Identity with { OffsetY = -Coefficients.Bounce * s * Hop(phase) },
            FrameEffectKind.Tilt => EffectTransform.Identity with { Angle = Coefficients.TiltDegrees * s * Wave() },
            FrameEffectKind.Squash => Squash(Coefficients.Squash * s * Wave()),
            FrameEffectKind.Blink => EffectTransform.Identity with { Opacity = 1 - Coefficients.Blink * s * Pulse() },
            _ => EffectTransform.Identity,
        };
    }

    /// <summary>
    /// 이 효과가 이미지 밖으로 나갈 수 있는 최대 범위(이미지 긴 변에 대한 비율). <see cref="Evaluate"/>와 같은 계수로 계산한다.
    /// 배율·기울기는 바닥 가운데 기준이므로 위쪽과 옆으로만 번진다.
    /// </summary>
    public EffectPadding MaxExtent()
    {
        var s = Math.Clamp(Strength, MinStrength, MaxStrength) / 100.0;
        return Kind switch
        {
            FrameEffectKind.BobVertical => new EffectPadding(0, Coefficients.Bob * s, Coefficients.Bob * s),
            FrameEffectKind.ShakeHorizontal => new EffectPadding(Coefficients.Shake * s, 0, 0),
            FrameEffectKind.Grow => new EffectPadding(Coefficients.Grow * s / 2, Coefficients.Grow * s, 0),
            FrameEffectKind.Bounce => new EffectPadding(0, Coefficients.Bounce * s, 0),
            FrameEffectKind.Tilt => TiltExtent(Coefficients.TiltDegrees * s),
            FrameEffectKind.Squash => new EffectPadding(Coefficients.Squash * s / 2, Coefficients.Squash * s, 0),
            _ => EffectPadding.None,   // Shrink, Blink: 이미지 안에서만 변한다
        };
    }

    /// <summary>
    /// 바닥 가운데를 축으로 <paramref name="degrees"/>만큼 기울였을 때 이미지가 밖으로 나가는 범위의 상한(긴 변 L 기준).
    /// 너비 W, 높이 H(둘 다 ≤ L)일 때 위쪽 모서리는 옆으로 H·sinθ + ½W(cosθ−1) ≤ L·sinθ, 위로 ½W·sinθ − H(1−cosθ) ≤ ½L·sinθ,
    /// 아래쪽 모서리는 아래로 ½W·sinθ ≤ ½L·sinθ 움직이므로, 가로세로 비율과 무관하게 이 값이면 잘리지 않는다.
    /// </summary>
    private static EffectPadding TiltExtent(double degrees)
    {
        var sin = Math.Sin(degrees * Math.PI / 180);
        return new EffectPadding(sin, 0.5 * sin, 0.5 * sin);
    }

    /// <summary>효과 목록을 값으로 비교한다(Frames 목록 포함).</summary>
    public static bool ListsEqual(IReadOnlyList<FrameEffect>? a, IReadOnlyList<FrameEffect>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Kind != y.Kind || x.Strength != y.Strength || x.PeriodMs != y.PeriodMs) return false;
            if (x.Frames is null != y.Frames is null) return false;
            if (x.Frames is not null && !x.Frames.SequenceEqual(y.Frames!)) return false;
        }

        return true;
    }

    /// <summary>포물선 한 번 뛰기: 주기마다 0 → 1(꼭대기) → 0.</summary>
    private static double Hop(double phase)
    {
        var q = phase - Math.Floor(phase);
        return 4 * q * (1 - q);
    }

    private static EffectTransform Uniform(double scale) => EffectTransform.Identity with { ScaleX = scale, ScaleY = scale };

    private static EffectTransform Squash(double amount) => EffectTransform.Identity with { ScaleX = 1 + amount, ScaleY = 1 - amount };

    /// <summary>강도 100%일 때의 최대 변화량. 이동은 이미지 크기에 대한 비율, 기울기는 도(°).</summary>
    public static class Coefficients
    {
        public const double Bob = 0.12;
        public const double Shake = 0.12;
        public const double Shrink = 0.5;
        public const double Grow = 0.35;
        public const double Bounce = 0.45;
        public const double TiltDegrees = 25;
        public const double Squash = 0.25;
        public const double Blink = 0.9;
    }
}
