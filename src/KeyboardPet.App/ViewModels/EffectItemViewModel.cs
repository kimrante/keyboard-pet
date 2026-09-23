using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyboardPet.Core.Effects;

namespace KeyboardPet.App.ViewModels;

/// <summary>효과 종류 콤보박스 항목.</summary>
public sealed record EffectKindChoice(FrameEffectKind Kind, string Label, string Description)
{
    public static IReadOnlyList<EffectKindChoice> All { get; } = new[]
    {
        new EffectKindChoice(FrameEffectKind.BobVertical, "위아래 흔들기", "위아래로 둥실둥실 움직입니다."),
        new EffectKindChoice(FrameEffectKind.ShakeHorizontal, "좌우 흔들기", "좌우로 흔들립니다."),
        new EffectKindChoice(FrameEffectKind.Shrink, "작아지기", "바닥에 붙은 채 작아졌다가 돌아옵니다."),
        new EffectKindChoice(FrameEffectKind.Grow, "커지기", "바닥에 붙은 채 커졌다가 돌아옵니다."),
        new EffectKindChoice(FrameEffectKind.Bounce, "튀어오르기", "바닥에서 통통 튀어오릅니다."),
        new EffectKindChoice(FrameEffectKind.Tilt, "기울이기", "바닥을 축으로 좌우로 갸웃거립니다."),
        new EffectKindChoice(FrameEffectKind.Squash, "말랑하게 찌그러지기", "가로·세로가 번갈아 늘었다 줄어듭니다."),
        new EffectKindChoice(FrameEffectKind.Blink, "깜빡이기", "투명해졌다가 돌아옵니다."),
    };

    public static EffectKindChoice Of(FrameEffectKind kind) => All.FirstOrDefault(c => c.Kind == kind) ?? All[0];

    public override string ToString() => Label;
}

/// <summary>"적용 프레임"의 프레임 한 칸(썸네일 + 체크).</summary>
public sealed partial class FrameToggleViewModel : ObservableObject
{
    private readonly Action<FrameToggleViewModel> _changed;

    [ObservableProperty]
    private bool _isChecked;

    public FrameToggleViewModel(int index, ImageSource? thumbnail, bool isChecked, Action<FrameToggleViewModel> changed)
    {
        Index = index;
        Thumbnail = thumbnail;
        _isChecked = isChecked;
        _changed = changed;
    }

    public int Index { get; }

    public string Label => $"{Index + 1}";

    public ImageSource? Thumbnail { get; }

    /// <summary>세트에 없는 프레임(프레임이 줄었거나 아직 로드 전). 선택은 유지된다.</summary>
    public bool IsMissing => Thumbnail is null;

    partial void OnIsCheckedChanged(bool value) => _changed(this);
}

/// <summary>
/// 효과 카드 하나: 종류, 강도, 속도, (세트 효과라면) 적용 프레임.
/// 세트의 "효과" 탭과 키 매핑 규칙의 효과 목록이 함께 쓴다. 편집 즉시 소유자에게 커밋한다.
/// </summary>
public sealed partial class EffectItemViewModel : ObservableObject
{
    private readonly Action _commit;
    private readonly Action<EffectItemViewModel> _remove;
    private readonly Func<IReadOnlyList<BitmapSource>> _frames;
    private readonly SortedSet<int> _selectedFrames;
    private bool _suspend;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindDescription))]
    private EffectKindChoice _selectedKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthText))]
    private int _strength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodText))]
    private int _periodMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FramesSummary))]
    private bool _allFrames;

    /// <param name="showFrameSelection">세트 효과면 true(적용 프레임 선택), 키 규칙 효과면 false</param>
    public EffectItemViewModel(
        FrameEffect effect,
        bool showFrameSelection,
        Func<IReadOnlyList<BitmapSource>> frames,
        Action commit,
        Action<EffectItemViewModel> remove)
    {
        _commit = commit;
        _remove = remove;
        _frames = frames;
        ShowFrameSelection = showFrameSelection;

        _selectedKind = EffectKindChoice.Of(effect.Kind);
        _strength = effect.Strength;
        _periodMs = effect.PeriodMs;
        _allFrames = effect.Frames is null;
        _selectedFrames = new SortedSet<int>(effect.Frames ?? Array.Empty<int>());
        RefreshFrames();
    }

    public IReadOnlyList<EffectKindChoice> KindChoices => EffectKindChoice.All;

    public bool ShowFrameSelection { get; }

    public ObservableCollection<FrameToggleViewModel> FrameToggles { get; } = new();

    public string KindDescription => SelectedKind.Description;

    public string StrengthText => $"{Strength}%";

    public string PeriodText => $"{PeriodMs} ms";

    public string FramesSummary => AllFrames
        ? "모든 프레임"
        : _selectedFrames.Count == 0 ? "선택한 프레임 없음 (효과가 나타나지 않음)" : string.Join(", ", _selectedFrames.Select(i => $"{i + 1}번")) + " 프레임";

    public FrameEffect ToEffect() => new(
        SelectedKind.Kind,
        Strength,
        PeriodMs,
        ShowFrameSelection && !AllFrames ? _selectedFrames.ToList() : null);

    /// <summary>세트 프레임이 다시 로드되면 썸네일 목록을 새로 만든다. 선택은 프레임 번호로 유지된다.</summary>
    public void RefreshFrames()
    {
        if (!ShowFrameSelection)
        {
            return;
        }

        _suspend = true;
        try
        {
            var frames = _frames();
            var count = Math.Max(frames.Count, _selectedFrames.Count == 0 ? 0 : _selectedFrames.Max + 1);

            // 프레임이 그대로면 다시 만들지 않는다(클릭 중인 체크박스가 교체되지 않도록).
            var unchanged = FrameToggles.Count == count
                            && FrameToggles.All(t => ReferenceEquals(t.Thumbnail, t.Index < frames.Count ? frames[t.Index] : null));
            if (unchanged)
            {
                return;
            }

            FrameToggles.Clear();
            for (var i = 0; i < count; i++)
            {
                FrameToggles.Add(new FrameToggleViewModel(i, i < frames.Count ? frames[i] : null, _selectedFrames.Contains(i), OnFrameToggled));
            }
        }
        finally
        {
            _suspend = false;
        }

        OnPropertyChanged(nameof(FramesSummary));
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    partial void OnSelectedKindChanged(EffectKindChoice value) => Changed();

    partial void OnStrengthChanged(int value) => Changed();

    partial void OnPeriodMsChanged(int value) => Changed();

    partial void OnAllFramesChanged(bool value) => Changed();

    private void OnFrameToggled(FrameToggleViewModel toggle)
    {
        if (_suspend)
        {
            return;
        }

        if (toggle.IsChecked)
        {
            _selectedFrames.Add(toggle.Index);
        }
        else
        {
            _selectedFrames.Remove(toggle.Index);
        }

        OnPropertyChanged(nameof(FramesSummary));
        Changed();
    }

    private void Changed()
    {
        if (!_suspend)
        {
            _commit();
        }
    }
}
