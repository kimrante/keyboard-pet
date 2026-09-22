using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Rules;

namespace KeyboardPet.Core.Settings;

/// <summary>PetWindow 표시 관련 설정.</summary>
public sealed record WindowSettings
{
    public const double MinScale = 0.25;
    public const double MaxScale = 4.0;
    public const double MinOpacity = 0.1;
    public const double MaxOpacity = 1.0;

    /// <summary>저장된 창 위치(DIP). null이면 작업 영역 우하단에 자동 배치.</summary>
    public double? X { get; init; }

    public double? Y { get; init; }

    public double Scale { get; init; } = 1.0;

    public double Opacity { get; init; } = 1.0;

    /// <summary>true면 마우스 클릭이 창을 통과한다(우클릭 메뉴는 트레이에서만 가능).</summary>
    public bool ClickThrough { get; init; }

    public bool ShowCounter { get; init; } = true;

    public WindowSettings Normalized() => this with
    {
        X = X is { } x && double.IsFinite(x) ? x : null,
        Y = Y is { } y && double.IsFinite(y) ? y : null,
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, MinScale, MaxScale) : 1.0,
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, MaxOpacity) : 1.0,
    };
}

/// <summary>
/// 사용자 이미지 세트: 이름과 이미지가 들어 있는 폴더.
/// <paramref name="Frames"/>가 null이면 폴더의 모든 이미지 파일을 파일명 순서로 쓰고,
/// 값이 있으면 그 파일들만 그 순서대로 쓴다(설정 창에서 순서 편집·제외한 결과).
/// </summary>
public sealed record FrameSetSettings(string Name, string Folder, IReadOnlyList<string>? Frames = null)
{
    public bool HasCustomFrames => Frames is not null;

    public bool Equals(FrameSetSettings? other) =>
        other is not null
        && Name == other.Name
        && Folder == other.Folder
        && (Frames is null ? other.Frames is null : other.Frames is not null && Frames.SequenceEqual(other.Frames));

    public override int GetHashCode() => HashCode.Combine(Name, Folder, Frames?.Count ?? -1);
}

/// <summary>
/// 앱 전체 설정. 불변 레코드이며 변경은 with 식으로 새 인스턴스를 만든다.
/// JSON(settings.json)에 그대로 직렬화된다.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public const string BuiltInDefaultSet = "idle";

    public static IReadOnlyList<KeyRule> DefaultRules { get; } = new[]
    {
        new KeyRule("Enter", "jump", HoldMs: 800, ResetIndex: true),
        new KeyRule("*", "typing", HoldMs: 600, ResetIndex: false),
    };

    public int Version { get; init; } = CurrentVersion;

    public bool IsTopmost { get; init; } = true;

    public WindowSettings Window { get; init; } = new();

    public AnimationOptions Animation { get; init; } = new();

    /// <summary>키를 길게 눌러 발생하는 반복 입력도 타수·규칙에 반영할지.</summary>
    public bool CountAutoRepeat { get; init; }

    public bool StartWithWindows { get; init; }

    public IReadOnlyList<FrameSetSettings> FrameSets { get; init; } = Array.Empty<FrameSetSettings>();

    public string DefaultFrameSet { get; init; } = BuiltInDefaultSet;

    public IReadOnlyList<KeyRule> Rules { get; init; } = DefaultRules;

    public static AppSettings Default => new();

    /// <summary>역직렬화 결과의 null·범위 밖 값을 보정한 복사본.</summary>
    public AppSettings Normalized() => this with
    {
        Version = CurrentVersion,
        Window = (Window ?? new WindowSettings()).Normalized(),
        Animation = (Animation ?? new AnimationOptions()).Normalized(),
        DefaultFrameSet = string.IsNullOrWhiteSpace(DefaultFrameSet) ? BuiltInDefaultSet : DefaultFrameSet.Trim(),
        FrameSets = (FrameSets ?? Array.Empty<FrameSetSettings>())
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Folder))
            .Select(f => new FrameSetSettings(
                f.Name.Trim(),
                f.Folder.Trim(),
                f.Frames?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()))
            .ToList(),
        Rules = (Rules ?? Array.Empty<KeyRule>())
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.FrameSet))
            .Select(r => r with
            {
                Keys = (r.Keys ?? Array.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList(),
                FrameSet = r.FrameSet.Trim(),
                HoldMs = Math.Max(0, r.HoldMs),
                FrameIndex = r.FrameIndex is < 0 ? null : r.FrameIndex,
            })
            .ToList(),
    };

    // 레코드의 기본 동등성은 리스트 속성을 참조로 비교하므로, 부분별 값 비교 도우미를 둔다.

    public static bool RulesEqual(IReadOnlyList<KeyRule> a, IReadOnlyList<KeyRule> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.FrameSet != y.FrameSet || x.HoldMs != y.HoldMs || x.ResetIndex != y.ResetIndex
                || x.FrameIndex != y.FrameIndex || !x.Keys.SequenceEqual(y.Keys))
            {
                return false;
            }
        }

        return true;
    }

    public static bool FrameSetsEqual(IReadOnlyList<FrameSetSettings> a, IReadOnlyList<FrameSetSettings> b) =>
        ReferenceEquals(a, b) || a.SequenceEqual(b);
}
