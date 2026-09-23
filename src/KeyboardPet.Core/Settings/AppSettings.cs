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
/// <paramref name="AnimationFrames"/>가 null이면 모든 프레임이 루프 애니메이션에 참여하고,
/// 값이 있으면 그 파일들만 참여한다. 나머지는 키 매핑 규칙의 단일 프레임 표시로만 쓰인다.
/// </summary>
public sealed record FrameSetSettings(
    string Name,
    string Folder,
    IReadOnlyList<string>? Frames = null,
    IReadOnlyList<string>? AnimationFrames = null,
    string? IdleFrame = null)
{
    public bool HasCustomFrames => Frames is not null;

    public bool HasCustomAnimationFrames => AnimationFrames is not null;

    public bool Equals(FrameSetSettings? other) =>
        other is not null
        && Name == other.Name
        && Folder == other.Folder
        && ListsEqual(Frames, other.Frames)
        && ListsEqual(AnimationFrames, other.AnimationFrames)
        && string.Equals(IdleFrame, other.IdleFrame, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Name, Folder, Frames?.Count ?? -1, AnimationFrames?.Count ?? -1, IdleFrame?.ToLowerInvariant());

    private static bool ListsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);
}

/// <summary>
/// 이미지 세트에 귀속된 설정: 그 세트가 사용 중일 때 적용되는 애니메이션 옵션과 키 매핑 규칙.
/// 항목이 null이면 세트 기본값(<see cref="AppSettings.DefaultRulesFor"/>, 기본 AnimationOptions)을 쓴다.
/// </summary>
public sealed record SetProfile(AnimationOptions? Animation = null, IReadOnlyList<KeyRule>? Rules = null);

/// <summary>
/// 앱 전체 설정. 불변 레코드이며 변경은 with 식으로 새 인스턴스를 만든다.
/// JSON(settings.json)에 그대로 직렬화된다.
///
/// 앱 공통 설정은 창·입력·자동 시작 같은 기본 기능뿐이고, 애니메이션 옵션과 키 매핑 규칙은 모두 이미지 세트에 귀속된다
/// (<see cref="SetProfiles"/>: 세트 이름 → 프로필). 지금 사용 중인 세트(<see cref="DefaultFrameSet"/>)의 프로필이
/// 적용된다(<see cref="EffectiveAnimation"/>, <see cref="EffectiveRules"/>).
/// </summary>
public sealed record AppSettings
{
    /// <summary>v2: 공통 Animation/Rules 제거, 규칙이 다른 세트를 가리키지 않고 자기 세트의 프레임만 다룬다.</summary>
    public const int CurrentVersion = 2;

    /// <summary>앱에 내장된 유일한 샘플 세트.</summary>
    public const string ExampleSetName = "예시";

    public const string BuiltInDefaultSet = ExampleSetName;

    /// <summary>예시 세트의 기본 규칙: Enter를 누르면 2번 프레임(점프)을 0.8초간 보여준다.</summary>
    public static IReadOnlyList<KeyRule> ExampleRules { get; } = new[]
    {
        new KeyRule("Enter", HoldMs: 800, ResetIndex: true, FrameIndex: 1),
    };

    private static readonly AnimationOptions DefaultAnimation = new();

    public int Version { get; init; } = CurrentVersion;

    public bool IsTopmost { get; init; } = true;

    public WindowSettings Window { get; init; } = new();

    /// <summary>키를 길게 눌러 발생하는 반복 입력도 타수·규칙에 반영할지.</summary>
    public bool CountAutoRepeat { get; init; }

    public bool StartWithWindows { get; init; }

    public IReadOnlyList<FrameSetSettings> FrameSets { get; init; } = Array.Empty<FrameSetSettings>();

    /// <summary>지금 사용 중인 세트. 애니메이션과 키 매핑은 이 세트의 프로필을 따른다.</summary>
    public string DefaultFrameSet { get; init; } = BuiltInDefaultSet;

    /// <summary>세트 이름 → 프로필. 이름 비교는 대소문자를 구분하지 않는다(Normalized가 보장).</summary>
    public IReadOnlyDictionary<string, SetProfile> SetProfiles { get; init; } = EmptyProfiles;

    private static readonly IReadOnlyDictionary<string, SetProfile> EmptyProfiles =
        new Dictionary<string, SetProfile>(StringComparer.OrdinalIgnoreCase);

    public static AppSettings Default => new();

    public static bool IsExampleSet(string? setName) =>
        string.Equals(setName, ExampleSetName, StringComparison.OrdinalIgnoreCase);

    /// <summary>프로필에 규칙이 없을 때 쓰는 세트 기본 규칙. 예시 세트만 샘플 규칙을 갖고, 나머지 세트는 규칙 없이 시작한다.</summary>
    public static IReadOnlyList<KeyRule> DefaultRulesFor(string setName) =>
        IsExampleSet(setName) ? ExampleRules : Array.Empty<KeyRule>();

    // ── 세트에 귀속된 설정 ──

    public SetProfile? ProfileOf(string setName) =>
        SetProfiles.TryGetValue(setName, out var profile) ? profile : null;

    public AnimationOptions AnimationOf(string setName) => ProfileOf(setName)?.Animation ?? DefaultAnimation;

    public IReadOnlyList<KeyRule> RulesOf(string setName) => ProfileOf(setName)?.Rules ?? DefaultRulesFor(setName);

    /// <summary>사용 중인 세트에 적용되는 애니메이션 옵션.</summary>
    public AnimationOptions EffectiveAnimation => AnimationOf(DefaultFrameSet);

    /// <summary>사용 중인 세트에 적용되는 키 매핑 규칙.</summary>
    public IReadOnlyList<KeyRule> EffectiveRules => RulesOf(DefaultFrameSet);

    /// <summary>사용 중인 세트의 프로필에 애니메이션 옵션을 기록한다.</summary>
    public AppSettings WithEffectiveAnimation(AnimationOptions animation) =>
        WithProfile(DefaultFrameSet, p => p with { Animation = animation });

    /// <summary>사용 중인 세트의 프로필에 규칙을 기록한다.</summary>
    public AppSettings WithEffectiveRules(IReadOnlyList<KeyRule> rules) =>
        WithProfile(DefaultFrameSet, p => p with { Rules = rules });

    /// <summary>
    /// 세트 이름이 바뀌면 프로필과 사용 중인 세트 참조도 따라가게 한다.
    /// 새 이름에 이미 프로필이 있으면(예: 예시 세트를 대체하는 이름) 덮어쓰지 않는다. 설정은 이름에 귀속되므로
    /// 그 세트는 그 이름의 기존 설정을 이어받고, 옛 이름의 프로필은 버린다.
    /// </summary>
    public AppSettings WithSetRenamed(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)
            || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        var profiles = new Dictionary<string, SetProfile>(SetProfiles, StringComparer.OrdinalIgnoreCase);
        if (profiles.Remove(oldName, out var moved))
        {
            profiles.TryAdd(newName, moved);
        }

        return this with
        {
            SetProfiles = profiles,
            DefaultFrameSet = string.Equals(DefaultFrameSet, oldName, StringComparison.OrdinalIgnoreCase) ? newName : DefaultFrameSet,
        };
    }

    /// <summary>
    /// 세트가 삭제되면 그 세트의 프로필도 함께 지운다. 사용 중인 세트였다면 예시 세트로 돌아간다.
    /// 예시 세트는 내장 세트로 계속 존재하므로 같은 이름의 사용자 세트를 지워도 프로필은 남긴다.
    /// </summary>
    public AppSettings WithSetRemoved(string setName)
    {
        if (string.IsNullOrWhiteSpace(setName) || IsExampleSet(setName))
        {
            return this;
        }

        var profiles = new Dictionary<string, SetProfile>(SetProfiles, StringComparer.OrdinalIgnoreCase);
        profiles.Remove(setName);
        return this with
        {
            SetProfiles = profiles,
            DefaultFrameSet = string.Equals(DefaultFrameSet, setName, StringComparison.OrdinalIgnoreCase) ? BuiltInDefaultSet : DefaultFrameSet,
        };
    }

    private AppSettings WithProfile(string setName, Func<SetProfile, SetProfile> mutate)
    {
        var current = ProfileOf(setName) ?? new SetProfile();
        var profiles = new Dictionary<string, SetProfile>(SetProfiles, StringComparer.OrdinalIgnoreCase)
        {
            [setName] = mutate(current),
        };
        return this with { SetProfiles = profiles };
    }

    /// <summary>역직렬화 결과의 null·범위 밖 값을 보정한 복사본.</summary>
    public AppSettings Normalized() => this with
    {
        Version = CurrentVersion,
        Window = (Window ?? new WindowSettings()).Normalized(),
        DefaultFrameSet = string.IsNullOrWhiteSpace(DefaultFrameSet) ? BuiltInDefaultSet : DefaultFrameSet.Trim(),
        FrameSets = (FrameSets ?? Array.Empty<FrameSetSettings>())
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Folder))
            .Select(f => new FrameSetSettings(
                f.Name.Trim(),
                f.Folder.Trim(),
                CleanNames(f.Frames),
                CleanNames(f.AnimationFrames),
                string.IsNullOrWhiteSpace(f.IdleFrame) ? null : f.IdleFrame.Trim()))
            .ToList(),
        SetProfiles = CleanProfiles(SetProfiles),
    };

    private static List<string>? CleanNames(IReadOnlyList<string>? names) =>
        names?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

    private static List<KeyRule> CleanRules(IReadOnlyList<KeyRule> rules) =>
        rules
            .Where(r => r is not null)
            .Select(r => r with
            {
                Keys = (r.Keys ?? Array.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList(),
                HoldMs = Math.Max(0, r.HoldMs),
                FrameIndex = r.FrameIndex is < 0 ? null : r.FrameIndex,
            })
            .ToList();

    private static IReadOnlyDictionary<string, SetProfile> CleanProfiles(IReadOnlyDictionary<string, SetProfile>? profiles)
    {
        var result = new Dictionary<string, SetProfile>(StringComparer.OrdinalIgnoreCase);
        if (profiles is null)
        {
            return result;
        }

        foreach (var (name, profile) in profiles)
        {
            if (string.IsNullOrWhiteSpace(name) || profile is null)
            {
                continue;
            }

            result[name.Trim()] = new SetProfile(
                profile.Animation?.Normalized(),
                profile.Rules is null ? null : CleanRules(profile.Rules));
        }

        return result;
    }

    public static bool ProfilesEqual(IReadOnlyDictionary<string, SetProfile> a, IReadOnlyDictionary<string, SetProfile> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        foreach (var (name, pa) in a)
        {
            if (!b.TryGetValue(name, out var pb)) return false;
            if (pa.Animation != pb.Animation) return false;
            if (pa.Rules is null != pb.Rules is null) return false;
            if (pa.Rules is not null && !RulesEqual(pa.Rules, pb.Rules!)) return false;
        }

        return true;
    }

    // 레코드의 기본 동등성은 리스트 속성을 참조로 비교하므로, 부분별 값 비교 도우미를 둔다.

    public static bool RulesEqual(IReadOnlyList<KeyRule> a, IReadOnlyList<KeyRule> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.HoldMs != y.HoldMs || x.ResetIndex != y.ResetIndex
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
