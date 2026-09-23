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

    /// <summary>
    /// 효과 목록이 동시에 최대로 움직일 때의 여백. 효과는 합성되므로(같은 종류도) 강도를 모두 더해 넉넉하게 잡는다.
    /// </summary>
    public static EffectPadding For(IEnumerable<FrameEffect> effects)
    {
        double side = 0, top = 0, bottom = 0;
        foreach (var group in effects.GroupBy(e => e.Kind))
        {
            var s = group.Sum(e => Math.Clamp(e.Strength, FrameEffect.MinStrength, FrameEffect.MaxStrength)) / 100.0;
            switch (group.Key)
            {
                case FrameEffectKind.BobVertical:
                    top += FrameEffect.Coefficients.Bob * s;
                    bottom += FrameEffect.Coefficients.Bob * s;
                    break;
                case FrameEffectKind.ShakeHorizontal:
                    side += FrameEffect.Coefficients.Shake * s;
                    break;
                case FrameEffectKind.Grow:
                    top += FrameEffect.Coefficients.Grow * s;
                    side += FrameEffect.Coefficients.Grow * s / 2;
                    break;
                case FrameEffectKind.Bounce:
                    top += FrameEffect.Coefficients.Bounce * s;
                    break;
                case FrameEffectKind.Tilt:
                    // 바닥 가운데를 축으로 기울면 위쪽 모서리가 옆으로 크게 움직인다(정사각형 기준 약 0.4).
                    side += Math.Sin(FrameEffect.Coefficients.TiltDegrees * s * Math.PI / 180) * 1.05;
                    break;
                case FrameEffectKind.Squash:
                    side += FrameEffect.Coefficients.Squash * s / 2;
                    top += FrameEffect.Coefficients.Squash * s;
                    break;
            }
        }

        return new EffectPadding(side, top, bottom);
    }
}
