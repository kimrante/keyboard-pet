using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.Services;

/// <summary>세트 하나의 로드 결과. UI(설정 창)에서 상태 표시에 쓴다.</summary>
/// <param name="LoopCount">루프 애니메이션에 참여하는 프레임 수(전체면 FrameCount와 같음)</param>
public sealed record FrameSetStatus(int FrameCount, bool IsBuiltIn, string? Error, int MissingCount = 0, int LoopCount = -1)
{
    public bool HasError => Error is not null;

    public int EffectiveLoopCount => LoopCount < 0 ? FrameCount : LoopCount;
}

/// <summary>
/// 이미지 세트들, AnimationEngine, KeyRuleController를 묶어 ShellViewModel.CurrentFrame에 프레임을 공급한다.
/// 설정(세트 목록, 기본 세트, 규칙, 애니메이션 옵션)이 바뀌면 해당 부분만 다시 적용한다.
/// 내장 세트(idle/jump/typing)는 같은 이름의 사용자 세트가 없을 때 대체로 쓰인다.
/// </summary>
public sealed class AnimationService : IDisposable
{
    private static readonly Dictionary<string, string[]> BuiltInSets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = PackUris("idle", 4),
        ["jump"] = PackUris("jump", 2),
        ["typing"] = PackUris("typing", 2),
    };

    private readonly AnimationEngine _engine;
    private readonly ImageCache _cache;
    private readonly ShellViewModel _shell;
    private readonly IFrameTimerFactory _timers;
    private readonly SettingsService _settings;
    private readonly Dictionary<string, LoadedFrameSet> _sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameSetStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private KeyRuleController? _rules;
    private LoadedFrameSet _active = new(FrameSet.Empty, Array.Empty<BitmapSource>());

    public AnimationService(
        AnimationEngine engine,
        ImageCache cache,
        ShellViewModel shell,
        IFrameTimerFactory timers,
        SettingsService settings)
    {
        _engine = engine;
        _cache = cache;
        _shell = shell;
        _timers = timers;
        _settings = settings;
    }

    public static IReadOnlyList<string> BuiltInSetNames { get; } = BuiltInSets.Keys.ToList();

    /// <summary>현재 실제로 사용 중인 기본 세트 이름(설정의 기본 세트가 없으면 idle).</summary>
    public string DefaultSetName { get; private set; } = AppSettings.BuiltInDefaultSet;

    public IReadOnlyDictionary<string, FrameSetStatus> SetStatuses => _statuses;

    public IReadOnlyList<string> RuleErrors { get; private set; } = Array.Empty<string>();

    /// <summary>세트 목록/규칙이 다시 적용된 뒤 발생. 설정 창이 상태 표시를 갱신하는 데 쓴다.</summary>
    public event Action? Reloaded;

    public void Initialize()
    {
        _engine.FrameChanged += OnFrameChanged;
        _settings.Changed += OnSettingsChanged;

        ReloadAll(_settings.Current);
        _engine.ApplyOptions(_settings.Current.Animation);
        _engine.Start();
    }

    /// <summary>
    /// 키 다운 1건. 규칙 매칭 → 세트 전환 → 타수 스케줄러 순으로 처리한다.
    /// 조합키 단독 입력(Shift, Ctrl 등)은 규칙에는 전달하지만 타수 스케줄러는 움직이지 않는다.
    /// </summary>
    public void OnKeyDown(KeyEvent e)
    {
        _rules?.OnKeyDown(e);
        if (!KeyNames.IsModifierKey(e.VirtualKey))
        {
            _engine.OnKeystroke();
        }
    }

    /// <summary>로드된 세트의 디코딩 프레임(설정 창의 프레임 선택 미리보기용). 없으면 null.</summary>
    public IReadOnlyList<BitmapSource>? TryGetFrames(string name) =>
        _sets.TryGetValue(name, out var set) ? set.Frames : null;

    /// <summary>로드된 세트의 루프 프레임 인덱스 목록. 전체가 루프면 null(세트가 없어도 null).</summary>
    public IReadOnlyList<int>? TryGetLoopFrames(string name) =>
        _sets.TryGetValue(name, out var set) ? set.Set.LoopFrames : null;

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _engine.FrameChanged -= OnFrameChanged;
        _rules?.Dispose();
        _engine.Dispose();
    }

    private void OnSettingsChanged(AppSettings old, AppSettings @new)
    {
        var setsChanged = !AppSettings.FrameSetsEqual(old.FrameSets, @new.FrameSets)
                          || !string.Equals(old.DefaultFrameSet, @new.DefaultFrameSet, StringComparison.OrdinalIgnoreCase);

        if (setsChanged)
        {
            ReloadAll(@new);
        }
        else if (!AppSettings.RulesEqual(old.Rules, @new.Rules))
        {
            ConfigureRules(@new.Rules);
            ShowDefault();
            Reloaded?.Invoke();
        }

        if (old.Animation != @new.Animation)
        {
            _engine.ApplyOptions(@new.Animation);
        }
    }

    private void ReloadAll(AppSettings s)
    {
        LoadSets(s.FrameSets);
        DefaultSetName = _sets.ContainsKey(s.DefaultFrameSet) ? s.DefaultFrameSet : AppSettings.BuiltInDefaultSet;
        ConfigureRules(s.Rules);
        ShowDefault();
        Reloaded?.Invoke();
    }

    private void LoadSets(IReadOnlyList<FrameSetSettings> userSets)
    {
        _sets.Clear();
        _statuses.Clear();

        // 파일 단위 디코딩 캐시: 순서 편집처럼 세트 목록만 바뀔 때 디스크를 다시 읽지 않는다.
        _cache.BeginGeneration();

        foreach (var fs in userSets)
        {
            try
            {
                var set = _cache.LoadFolder(fs.Name, fs.Folder, fs.Frames, fs.AnimationFrames);
                var missing = set.MissingFiles?.Count ?? 0;
                if (set.Set.IsEmpty)
                {
                    _statuses[fs.Name] = new FrameSetStatus(0, false,
                        fs.Frames is null ? "폴더에 지원하는 이미지 파일이 없습니다." : "남은 프레임이 없습니다. 폴더 순서로 되돌리거나 파일을 확인하세요.",
                        missing);
                    continue;
                }

                _sets[fs.Name] = set;
                _statuses[fs.Name] = new FrameSetStatus(set.Set.FrameCount, false, null, missing, set.Set.LoopCount);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                _statuses[fs.Name] = new FrameSetStatus(0, false, ex.Message);
                Debug.WriteLine($"[KeyboardPet] 세트 '{fs.Name}' 로드 실패: {ex.Message}");
            }
        }

        foreach (var (name, uris) in BuiltInSets)
        {
            if (!_sets.ContainsKey(name))
            {
                var set = _cache.LoadBuiltIn(name, uris);
                _sets[name] = set;
                _statuses.TryAdd(name, new FrameSetStatus(set.Set.FrameCount, true, null));
            }
        }

        // 이번 로드에서 쓰이지 않은 파일의 비트맵은 버려 메모리를 되돌린다.
        _cache.EndGeneration();
    }

    private void ConfigureRules(IEnumerable<KeyRule> rules)
    {
        _rules?.Dispose();

        var errors = new List<string>();
        var usable = new List<KeyRule>();
        var index = 0;
        foreach (var rule in rules)
        {
            index++;
            if (!_sets.TryGetValue(rule.FrameSet, out var set))
            {
                errors.Add($"규칙 #{index}: 세트 '{rule.FrameSet}'이(가) 없어 무시합니다.");
                continue;
            }

            if (rule.FrameIndex is int frame && frame >= set.Set.FrameCount)
            {
                errors.Add($"규칙 #{index}: 세트 '{rule.FrameSet}'에 {frame + 1}번 프레임이 없어 마지막 프레임을 사용합니다.");
            }

            usable.Add(rule);
        }

        var matcher = new RuleMatcher(usable);
        errors.AddRange(matcher.Errors);
        RuleErrors = errors;
        foreach (var error in errors)
        {
            Debug.WriteLine($"[KeyboardPet] {error}");
        }

        _rules = new KeyRuleController(_timers, matcher, DefaultSetName);
        _rules.ActiveSetChanged += Show;
    }

    private void ShowDefault() => Show(new ActiveSetRequest(DefaultSetName, true));

    private void Show(ActiveSetRequest request)
    {
        var frameIndex = request.FrameIndex;
        if (!_sets.TryGetValue(request.FrameSet, out var set))
        {
            // 요청한 세트가 없으면 기본 세트로 대체하되, 다른 세트의 프레임 번호를 그대로 고정하면 안 된다.
            frameIndex = null;
            if (!_sets.TryGetValue(DefaultSetName, out set))
            {
                set = _sets[AppSettings.BuiltInDefaultSet];
            }
        }

        _active = set;
        _engine.SetActiveSet(set.Set, request.ResetIndex, frameIndex);
    }

    private void OnFrameChanged(int index)
    {
        _shell.CurrentFrame = index >= 0 && index < _active.Frames.Count ? _active.Frames[index] : null;
    }

    private static string[] PackUris(string set, int count) =>
        Enumerable.Range(0, count)
            .Select(i => $"pack://application:,,,/Assets/{set}/frame-{i}.png")
            .ToArray();
}
