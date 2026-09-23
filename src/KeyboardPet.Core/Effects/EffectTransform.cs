namespace KeyboardPet.Core.Effects;

/// <summary>
/// 한 시점에 펫 이미지에 적용할 변형. 크기와 기울기는 이미지 바닥 가운데를 기준으로 한다.
/// </summary>
/// <param name="OffsetX">가로 이동(이미지 너비에 대한 비율, 오른쪽이 +)</param>
/// <param name="OffsetY">세로 이동(이미지 높이에 대한 비율, 아래쪽이 +)</param>
/// <param name="ScaleX">가로 배율</param>
/// <param name="ScaleY">세로 배율</param>
/// <param name="Angle">기울기(도, 시계 방향이 +)</param>
/// <param name="Opacity">불투명도 0~1</param>
public readonly record struct EffectTransform(
    double OffsetX,
    double OffsetY,
    double ScaleX,
    double ScaleY,
    double Angle,
    double Opacity)
{
    public static EffectTransform Identity { get; } = new(0, 0, 1, 1, 0, 1);

    public bool IsIdentity => this == Identity;

    /// <summary>두 효과를 합성한다: 이동·기울기는 더하고, 배율·불투명도는 곱한다.</summary>
    public EffectTransform Combine(EffectTransform other) => new(
        OffsetX + other.OffsetX,
        OffsetY + other.OffsetY,
        ScaleX * other.ScaleX,
        ScaleY * other.ScaleY,
        Angle + other.Angle,
        Math.Clamp(Opacity * other.Opacity, 0, 1));
}

/// <summary>효과가 이미지 밖으로 움직일 수 있는 여백(이미지 긴 변에 대한 비율). 펫 창이 효과를 잘라내지 않도록 쓴다.</summary>
public readonly record struct EffectPadding(double Side, double Top, double Bottom)
{
    public static EffectPadding None { get; } = new(0, 0, 0);

    /// <summary>효과 목록이 동시에 최대로 움직일 때의 여백. 효과는 합성되므로 각 효과의 범위를 모두 더해 넉넉하게 잡는다.</summary>
    public static EffectPadding For(IEnumerable<FrameEffect> effects) =>
        effects.Aggregate(None, (sum, e) => sum + e.MaxExtent());

    public static EffectPadding operator +(EffectPadding a, EffectPadding b) => new(a.Side + b.Side, a.Top + b.Top, a.Bottom + b.Bottom);
}
