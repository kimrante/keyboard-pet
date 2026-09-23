using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyboardPet.App.Services;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;
using Microsoft.Win32;

namespace KeyboardPet.App.ViewModels;

/// <summary>
/// 설정 창의 뷰모델. 모든 변경은 즉시 SettingsService에 반영되고(즉시 적용·자동 저장),
/// 설정이 바깥(트레이 메뉴, 창 드래그)에서 바뀌면 여기에도 반영된다.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly AnimationService _animation;
    private readonly IKeyboardSource _keyboard;
    private bool _syncing;
    private RuleItemViewModel? _captureTarget;

    // ── 일반 ──
    [ObservableProperty] private bool _isTopmost;
    [ObservableProperty] private double _scale;
    [ObservableProperty] private double _opacity;
    [ObservableProperty] private bool _clickThrough;
    [ObservableProperty] private bool _showCounter;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _countAutoRepeat;

    // ── 애니메이션 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedMode), nameof(IsRandomMode), nameof(IsKeystrokeMode), nameof(IsAdaptiveMode), nameof(UsesIdleReturn))]
    private FrameMode _frameMode;
    [ObservableProperty] private int _fixedIntervalMs;
    [ObservableProperty] private int _randomMinMs;
    [ObservableProperty] private int _randomMaxMs;
    [ObservableProperty] private int _keysPerFrame;
    [ObservableProperty] private int _idleReturnMs;
    [ObservableProperty] private int _adaptiveSlowMs;
    [ObservableProperty] private int _adaptiveFastMs;
    [ObservableProperty] private double _adaptiveTargetKeysPerSecond;
    [ObservableProperty] private int _adaptiveWindowMs;

    // ── 이미지 세트 ──
    /// <summary>사용 중인 세트. 애니메이션·키 매핑 탭은 이 세트의 설정을 편집한다.</summary>
    [ObservableProperty] private string? _defaultFrameSet;

    // ── 키 매핑 ──
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _ruleErrorsText = string.Empty;

    // ── 정보 ──
    [ObservableProperty] private string? _statusMessage;

    /// <summary>내장 예시 세트의 사용 상태 요약.</summary>
    [ObservableProperty] private string _builtInSummary = string.Empty;

    /// <summary>애니메이션·키 매핑 탭이 어느 세트의 설정을 편집 중인지.</summary>
    [ObservableProperty] private string _profileTargetText = string.Empty;

    public SettingsViewModel(SettingsService settings, AnimationService animation, IKeyboardSource keyboard)
    {
        _settings = settings;
        _animation = animation;
        _keyboard = keyboard;

        SyncFrom(settings.Current);
        RefreshStatuses();

        _settings.Changed += OnSettingsChanged;
        _animation.Reloaded += RefreshStatuses;
    }

    public ObservableCollection<FrameSetItemViewModel> FrameSets { get; } = new();

    public ObservableCollection<string> AvailableSetNames { get; } = new();

    public ObservableCollection<RuleItemViewModel> Rules { get; } = new();

    public bool IsFixedMode
    {
        get => FrameMode == FrameMode.Fixed;
        set { if (value) FrameMode = FrameMode.Fixed; }
    }

    public bool IsRandomMode
    {
        get => FrameMode == FrameMode.Random;
        set { if (value) FrameMode = FrameMode.Random; }
    }

    public bool IsKeystrokeMode
    {
        get => FrameMode == FrameMode.Keystroke;
        set { if (value) FrameMode = FrameMode.Keystroke; }
    }

    public bool IsAdaptiveMode
    {
        get => FrameMode == FrameMode.Adaptive;
        set { if (value) FrameMode = FrameMode.Adaptive; }
    }

    /// <summary>무입력 복귀 설정을 쓰는 모드인지(타수 기반, 타이핑 속도 연동).</summary>
    public bool UsesIdleReturn => FrameMode is FrameMode.Keystroke or FrameMode.Adaptive;

    /// <summary>목표 속도를 분당 타수로도 보여준다.</summary>
    public string AdaptiveTargetPerMinuteText => $"{AdaptiveTargetKeysPerSecond * 60:0}타/분";

    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public string SettingsFilePath => _settings.FilePath;

    public string? LoadError => _settings.LastLoadError is null
        ? null
        : $"설정 파일이 손상되어 기본값으로 시작했습니다. 손상된 파일은 같은 폴더에 .corrupt-* 이름으로 보관됩니다.\n({_settings.LastLoadError})";

    public string PrivacyNotice =>
        "Keyboard Pet은 전역 키보드 훅으로 키 입력 '이벤트'만 받습니다. " +
        "어떤 키가 눌렸는지는 기록·저장·전송하지 않으며, 메모리에서도 규칙 매칭과 타수 계산에만 순간적으로 사용됩니다. " +
        "네트워크 통신을 하지 않습니다.";

    public void Dispose()
    {
        StopCapture();
        _settings.Changed -= OnSettingsChanged;
        _animation.Reloaded -= RefreshStatuses;
    }

    // ── 커밋 (항목 뷰모델이 호출) ──

    /// <param name="removedName">삭제된 세트의 이름. 그 세트에 귀속된 설정도 함께 지운다.</param>
    public void CommitFrameSets(string? removedName = null)
    {
        // 이름을 비운 세트가 있으면 이름이 다시 채워질 때까지 저장을 미룬다. 그대로 저장하면 이름 없는 세트는
        // 정규화에서 빠지고, 카드와 그 세트에 귀속된 설정이 함께 사라진다.
        if (FrameSets.Any(f => string.IsNullOrWhiteSpace(f.Name)))
        {
            StatusMessage = "세트 이름을 입력하세요. 이름이 비어 있는 동안에는 세트 변경이 저장되지 않습니다.";
            return;
        }

        var renames = FrameSets
            .Where(f => !string.Equals(f.CommittedName, f.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(f => (Old: f.CommittedName, New: f.Name.Trim()))
            .ToList();

        Push(s =>
        {
            // 세트에 귀속된 설정(애니메이션·키 매핑)과 사용 중인 세트 참조가 이름 변경·삭제를 따라가게 한다.
            var next = removedName is null ? s : s.WithSetRemoved(removedName);
            next = renames.Aggregate(next, (acc, r) => acc.WithSetRenamed(r.Old, r.New));
            return next with { FrameSets = FrameSets.Select(f => f.ToSettings()).ToList() };
        });

        foreach (var item in FrameSets)
        {
            item.CommittedName = item.Name.Trim();
        }
    }

    /// <summary>로드된 세트의 프레임 비트맵(규칙의 프레임 선택 미리보기용). 로드되지 않았으면 빈 목록.</summary>
    public IReadOnlyList<System.Windows.Media.Imaging.BitmapSource> GetFrames(string setName) =>
        _animation.TryGetFrames(setName) ?? Array.Empty<System.Windows.Media.Imaging.BitmapSource>();

    /// <summary>세트의 루프 프레임 인덱스. 전체가 루프면 null.</summary>
    public IReadOnlyList<int>? GetLoopFrames(string setName) => _animation.TryGetLoopFrames(setName);

    /// <summary>규칙은 사용 중인 세트의 프로필에 저장된다.</summary>
    public void CommitRules()
    {
        Push(s => s.WithEffectiveRules(Rules.Select(r => r.ToRule()).ToList()));
    }

    // ── 명령: 일반 ──

    [RelayCommand]
    private void ResetPosition() => Push(s => s with { Window = s.Window with { X = null, Y = null } });

    // ── 명령: 이미지 세트 ──

    [RelayCommand]
    private void AddFrameSet()
    {
        var dialog = new OpenFolderDialog { Title = "이미지 세트 폴더 선택", Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = UniqueSetName(Path.GetFileName(dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar)));
        FrameSets.Add(new FrameSetItemViewModel(this, new FrameSetSettings(name, dialog.FolderName)));
        CommitFrameSets();
    }

    [RelayCommand]
    private void RemoveFrameSet(FrameSetItemViewModel item)
    {
        FrameSets.Remove(item);

        // 같은 이름의 세트가 남아 있으면(중복 이름) 설정은 그 세트가 계속 쓴다.
        var stillUsed = FrameSets.Any(f => string.Equals(f.CommittedName, item.CommittedName, StringComparison.OrdinalIgnoreCase));
        CommitFrameSets(stillUsed ? null : item.CommittedName);
    }

    /// <summary>폴더 없이 빈 세트를 만든다. 앱 관리 폴더를 만들어 두고, 이미지는 카드에 끌어다 넣는다.</summary>
    [RelayCommand]
    private void AddEmptySet()
    {
        try
        {
            var name = UniqueSetName("새 세트");
            var folder = UniqueManagedFolder(name);
            Directory.CreateDirectory(folder);
            FrameSets.Add(new FrameSetItemViewModel(this, new FrameSetSettings(name, folder)));
            CommitFrameSets();
            StatusMessage = $"'{name}' 세트를 만들었습니다. 이미지 파일을 카드에 끌어다 놓으면 {folder} 에 복사됩니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsLog.Write("빈 세트 만들기 실패", ex);
            StatusMessage = $"세트 폴더를 만들지 못했습니다: {ex.Message}";
        }
    }

    private static string UniqueManagedFolder(string name)
    {
        var folder = Path.Combine(ManagedSetsRoot, name);
        for (var i = 2; Directory.Exists(folder); i++)
        {
            folder = Path.Combine(ManagedSetsRoot, $"{name}-{i}");
        }

        return folder;
    }

    /// <summary>앱이 관리하는 세트 폴더. 드래그앤드롭으로 가져온 파일은 이 아래에 복사된다.</summary>
    public static string ManagedSetsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardPet", "sets");

    /// <summary>
    /// 탐색기에서 끌어다 놓은 경로들을 가져온다.
    /// - 세트 카드 위에 놓은 경우(target 지정): 이미지 파일(폴더 안의 파일 포함)을 그 세트 폴더로 복사해 프레임으로 추가
    /// - 빈 곳에 놓은 경우: 폴더는 그대로 세트로 추가, 파일들은 앱 관리 폴더에 복사해 새 세트 생성
    /// </summary>
    public void ImportDroppedPaths(IReadOnlyList<string> paths, FrameSetItemViewModel? target)
    {
        try
        {
            var folders = paths.Where(Directory.Exists).ToList();
            var files = paths.Where(p => File.Exists(p) && ImageCache.IsSupported(p)).ToList();

            if (target is not null)
            {
                var all = files.Concat(folders.SelectMany(f => ImageCache.ListFolderFiles(f).Select(n => Path.Combine(f, n)))).ToList();
                var count = all.Count == 0 ? 0 : target.ImportFiles(all);
                StatusMessage = count == 0
                    ? "가져올 이미지 파일이 없습니다 (PNG/JPG/BMP/GIF)."
                    : $"'{target.Name}' 세트에 프레임 {count}개를 추가했습니다.";
                return;
            }

            foreach (var folder in folders)
            {
                AddFolderAsSet(folder);
            }

            if (files.Count > 0)
            {
                CreateSetFromFiles(files);
            }

            if (folders.Count == 0 && files.Count == 0)
            {
                StatusMessage = "가져올 이미지 파일이나 폴더가 없습니다 (PNG/JPG/BMP/GIF).";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            DiagnosticsLog.Write("드래그앤드롭 가져오기 실패", ex);
            System.Windows.MessageBox.Show($"이미지를 가져오지 못했습니다.\n\n{ex.Message}", "Keyboard Pet",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void AddFolderAsSet(string folder)
    {
        var name = UniqueSetName(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        FrameSets.Add(new FrameSetItemViewModel(this, new FrameSetSettings(name, folder)));
        CommitFrameSets();
        StatusMessage = $"폴더를 '{name}' 세트로 추가했습니다.";
    }

    private void CreateSetFromFiles(IReadOnlyList<string> files)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(files[0])?.TrimEnd(Path.DirectorySeparatorChar) ?? string.Empty);
        var name = UniqueSetName(string.IsNullOrWhiteSpace(parent) ? "set" : parent);

        var folder = UniqueManagedFolder(name);
        Directory.CreateDirectory(folder);
        var item = new FrameSetItemViewModel(this, new FrameSetSettings(name, folder));
        FrameSets.Add(item);
        var count = item.ImportFiles(files);
        CommitFrameSets();
        StatusMessage = $"이미지 {count}개를 복사해 '{name}' 세트를 만들었습니다 ({folder}).";
    }

    [RelayCommand]
    private void BrowseFrameSetFolder(FrameSetItemViewModel item)
    {
        var dialog = new OpenFolderDialog
        {
            Title = $"'{item.Name}' 세트의 폴더 선택",
            Multiselect = false,
            InitialDirectory = Directory.Exists(item.Folder) ? item.Folder : null,
        };
        if (dialog.ShowDialog() == true)
        {
            item.Folder = dialog.FolderName;
        }
    }

    // ── 명령: 키 매핑 ──

    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(new RuleItemViewModel(this, new KeyRule(Array.Empty<string>(), HoldMs: 500, ResetIndex: true)));
        CommitRules();
    }

    [RelayCommand]
    private void RemoveRule(RuleItemViewModel item)
    {
        if (_captureTarget == item)
        {
            StopCapture();
        }

        Rules.Remove(item);
        CommitRules();
    }

    [RelayCommand]
    private void MoveRuleUp(RuleItemViewModel item) => MoveRule(item, -1);

    [RelayCommand]
    private void MoveRuleDown(RuleItemViewModel item) => MoveRule(item, +1);

    [RelayCommand]
    private void CaptureKey(RuleItemViewModel item)
    {
        if (IsCapturing && _captureTarget == item)
        {
            StopCapture();
            return;
        }

        StopCapture();
        _captureTarget = item;
        IsCapturing = true;
        _keyboard.KeyEvent += OnCaptureKeyEvent;
    }

    // ── 명령: 정보 ──

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            StatusMessage = $"폴더를 열 수 없습니다: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        Push(_ => AppSettings.Default);
        StatusMessage = "모든 설정을 기본값으로 되돌렸습니다.";
    }

    // ── 속성 변경 → 설정 반영 ──

    partial void OnIsTopmostChanged(bool value) => Push(s => s with { IsTopmost = value });
    partial void OnScaleChanged(double value) => Push(s => s with { Window = s.Window with { Scale = value } });
    partial void OnOpacityChanged(double value) => Push(s => s with { Window = s.Window with { Opacity = value } });
    partial void OnClickThroughChanged(bool value) => Push(s => s with { Window = s.Window with { ClickThrough = value } });
    partial void OnShowCounterChanged(bool value) => Push(s => s with { Window = s.Window with { ShowCounter = value } });
    partial void OnStartWithWindowsChanged(bool value) => Push(s => s with { StartWithWindows = value });
    partial void OnCountAutoRepeatChanged(bool value) => Push(s => s with { CountAutoRepeat = value });

    // 애니메이션 옵션은 사용 중인 세트의 프로필에 기록된다.
    private void PushAnimation(Func<AnimationOptions, AnimationOptions> mutate) =>
        Push(s => s.WithEffectiveAnimation(mutate(s.EffectiveAnimation)));

    partial void OnFrameModeChanged(FrameMode value) => PushAnimation(a => a with { Mode = value });
    partial void OnFixedIntervalMsChanged(int value) => PushAnimation(a => a with { FixedIntervalMs = value });
    partial void OnRandomMinMsChanged(int value) => PushAnimation(a => a with { RandomMinMs = value });
    partial void OnRandomMaxMsChanged(int value) => PushAnimation(a => a with { RandomMaxMs = value });
    partial void OnKeysPerFrameChanged(int value) => PushAnimation(a => a with { KeysPerFrame = value });
    partial void OnIdleReturnMsChanged(int value) => PushAnimation(a => a with { IdleReturnMs = value });
    partial void OnAdaptiveSlowMsChanged(int value) => PushAnimation(a => a with { AdaptiveSlowMs = value });
    partial void OnAdaptiveFastMsChanged(int value) => PushAnimation(a => a with { AdaptiveFastMs = value });
    partial void OnAdaptiveWindowMsChanged(int value) => PushAnimation(a => a with { AdaptiveWindowMs = value });

    partial void OnAdaptiveTargetKeysPerSecondChanged(double value)
    {
        OnPropertyChanged(nameof(AdaptiveTargetPerMinuteText));
        PushAnimation(a => a with { AdaptiveTargetKeysPerSecond = value });
    }

    partial void OnDefaultFrameSetChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // 세트마다 자기 애니메이션·키 매핑을 가지므로, 세트를 바꾸면 그 세트의 설정으로 전환된다.
            Push(s => s with { DefaultFrameSet = value });
        }
    }

    private void Push(Func<AppSettings, AppSettings> mutate)
    {
        if (!_syncing)
        {
            _settings.Update(mutate);
        }
    }

    // ── 설정 → 뷰모델 동기화 ──

    private void OnSettingsChanged(AppSettings old, AppSettings @new) => SyncFrom(@new);

    private void SyncFrom(AppSettings s)
    {
        _syncing = true;
        try
        {
            IsTopmost = s.IsTopmost;
            Scale = s.Window.Scale;
            Opacity = s.Window.Opacity;
            ClickThrough = s.Window.ClickThrough;
            ShowCounter = s.Window.ShowCounter;
            StartWithWindows = s.StartWithWindows;
            CountAutoRepeat = s.CountAutoRepeat;

            var animation = s.EffectiveAnimation;
            FrameMode = animation.Mode;
            FixedIntervalMs = animation.FixedIntervalMs;
            RandomMinMs = animation.RandomMinMs;
            RandomMaxMs = animation.RandomMaxMs;
            KeysPerFrame = animation.KeysPerFrame;
            IdleReturnMs = animation.IdleReturnMs;
            AdaptiveSlowMs = animation.AdaptiveSlowMs;
            AdaptiveFastMs = animation.AdaptiveFastMs;
            AdaptiveTargetKeysPerSecond = animation.AdaptiveTargetKeysPerSecond;
            AdaptiveWindowMs = animation.AdaptiveWindowMs;

            var currentSets = FrameSets.Select(f => f.ToSettings()).ToList();
            if (!AppSettings.FrameSetsEqual(currentSets, s.FrameSets))
            {
                FrameSets.Clear();
                foreach (var fs in s.FrameSets)
                {
                    FrameSets.Add(new FrameSetItemViewModel(this, fs));
                }
            }

            RefreshAvailableSetNames();
            DefaultFrameSet = s.DefaultFrameSet;

            var currentRules = Rules.Select(r => r.ToRule()).ToList();
            var effectiveRules = s.EffectiveRules;
            if (!AppSettings.RulesEqual(currentRules, effectiveRules))
            {
                Rules.Clear();
                foreach (var rule in effectiveRules)
                {
                    Rules.Add(new RuleItemViewModel(this, rule));
                }
            }
            else
            {
                foreach (var rule in Rules)
                {
                    rule.Revalidate();
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshAvailableSetNames()
    {
        var names = FrameSets.Select(f => f.Name)
            .Concat(AnimationService.BuiltInSetNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.SequenceEqual(AvailableSetNames))
        {
            return;
        }

        // ComboBox의 SelectedItem이 잠시 null이 되므로 _syncing 중에만 호출한다.
        AvailableSetNames.Clear();
        foreach (var name in names)
        {
            AvailableSetNames.Add(name);
        }
    }

    private void RefreshStatuses()
    {
        foreach (var item in FrameSets)
        {
            _animation.SetStatuses.TryGetValue(item.Name, out var status);
            item.UpdateStatus(status);
        }

        RuleErrorsText = string.Join(Environment.NewLine, _animation.RuleErrors);

        // 세트가 다시 로드되면 규칙 행의 프레임 목록(썸네일)도 새 프레임으로 갱신한다.
        foreach (var rule in Rules)
        {
            rule.Revalidate();
        }

        var builtIns = _animation.SetStatuses
            .Where(kv => kv.Value.IsBuiltIn)
            .Select(kv => $"'{kv.Key}' ({kv.Value.FrameCount}프레임)")
            .ToList();
        BuiltInSummary = builtIns.Count == 0
            ? "내장 예시 세트는 같은 이름의 사용자 세트로 대체되었습니다."
            : "내장 세트: " + string.Join(", ", builtIns) + " — 목록에 없어도 사용할 세트로 고를 수 있습니다.";

        RefreshProfileTargetText();
    }

    private void RefreshProfileTargetText()
    {
        var name = _settings.Current.DefaultFrameSet;
        var text = $"세트 '{name}'의 설정을 편집 중입니다. 애니메이션과 키 매핑은 세트마다 따로 저장되며, 사용할 세트를 바꾸면 그 세트의 설정으로 전환됩니다.";
        if (!string.Equals(_animation.DefaultSetName, name, StringComparison.OrdinalIgnoreCase))
        {
            text += $"\n세트 '{name}'에 표시할 프레임이 없어 지금은 '{_animation.DefaultSetName}' 세트가 대신 표시됩니다.";
        }

        ProfileTargetText = text;
    }

    private void MoveRule(RuleItemViewModel item, int delta)
    {
        var index = Rules.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rules.Count)
        {
            return;
        }

        Rules.Move(index, target);
        CommitRules();
    }

    private void OnCaptureKeyEvent(object? sender, KeyEvent e)
    {
        if (!e.IsDown || e.IsAutoRepeat || KeyNames.IsModifierKey(e.VirtualKey))
        {
            return;
        }

        var spec = new KeySpec(e.VirtualKey, e.Modifiers, false).ToString();
        _captureTarget?.AppendKey(spec);
        StopCapture();
    }

    private void StopCapture()
    {
        if (IsCapturing)
        {
            _keyboard.KeyEvent -= OnCaptureKeyEvent;
        }

        IsCapturing = false;
        _captureTarget = null;
    }

    private string UniqueSetName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "set";
        }

        var existing = FrameSets.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}-{i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
