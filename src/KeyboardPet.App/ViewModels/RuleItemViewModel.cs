using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyboardPet.Core.Effects;
using KeyboardPet.Core.Rules;

namespace KeyboardPet.App.ViewModels;

/// <summary>규칙의 "프레임" 콤보박스 항목. Index가 null이면 세트의 루프 애니메이션. Exists가 false면 세트에 없는 프레임(자리표시).</summary>
public sealed record FrameChoice(int? Index, string Label, ImageSource? Thumbnail, bool Exists = true)
{
    public static FrameChoice WholeSet { get; } = new(null, "애니메이션 재생", null);

    public override string ToString() => Label;
}

/// <summary>
/// 설정 창 "키 매핑" 탭의 한 행. 규칙은 사용 중인 세트에 귀속되므로 프레임 목록도 그 세트에서 가져온다.
/// 편집 즉시 검증하고 소유자에게 커밋한다.
/// </summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    private bool _refreshingChoices;

    [ObservableProperty]
    private string _keysText;

    [ObservableProperty]
    private int _holdMs;

    [ObservableProperty]
    private bool _resetIndex;

    [ObservableProperty]
    private FrameChoice? _selectedFrame;

    [ObservableProperty]
    private string? _error;

    public RuleItemViewModel(SettingsViewModel owner, KeyRule rule)
    {
        Owner = owner;
        _keysText = string.Join(", ", rule.Keys);
        _holdMs = rule.HoldMs;
        _resetIndex = rule.ResetIndex;
        RefreshFrameChoices(rule.FrameIndex);
        foreach (var effect in rule.Effects ?? Array.Empty<FrameEffect>())
        {
            Effects.Add(new EffectItemViewModel(effect, Effects, Changed));
        }

        Validate();
    }

    public SettingsViewModel Owner { get; }

    /// <summary>키를 누른 순간부터 유지 시간 동안 재생할 효과(합성됨).</summary>
    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();

    public ObservableCollection<FrameChoice> FrameChoices { get; } = new();

    public int? FrameIndex => SelectedFrame?.Index;

    public KeyRule ToRule() => new(
        ParseKeys(),
        Math.Max(0, HoldMs),
        ResetIndex,
        FrameIndex,
        Effects.Count == 0 ? null : Effects.Select(e => e.ToEffect()).ToList());

    [RelayCommand]
    private void AddEffect()
    {
        Effects.Add(new EffectItemViewModel(new FrameEffect(FrameEffectKind.Bounce, Strength: 60, PeriodMs: 500), Effects, Changed));
        Changed();
    }

    public void AppendKey(string spec)
    {
        KeysText = string.IsNullOrWhiteSpace(KeysText) ? spec : $"{KeysText}, {spec}";
    }

    public void Revalidate()
    {
        RefreshFrameChoices(FrameIndex);
        Validate();
    }

    /// <summary>사용 중인 세트의 프레임 목록으로 콤보박스 항목을 다시 만든다. 가능하면 기존 선택을 유지한다.</summary>
    public void RefreshFrameChoices(int? keepIndex)
    {
        _refreshingChoices = true;
        try
        {
            var setName = Owner.CurrentSetName;
            var frameCount = Owner.GetFrames(setName).Count;
            var thumbnails = Owner.GetThumbnails(setName);            // 20px 항목에 전체 해상도 프레임을 묶지 않는다(준비 전엔 null)
            var loop = Owner.GetLoopFrames(setName)?.ToHashSet();   // 프레임마다 Contains: 목록이면 O(n²)
            var choices = new List<FrameChoice> { FrameChoice.WholeSet };
            for (var i = 0; i < frameCount; i++)
            {
                var keyOnly = loop is not null && !loop.Contains(i);
                var thumbnail = thumbnails is not null && i < thumbnails.Count ? thumbnails[i] : null;
                choices.Add(new FrameChoice(i, keyOnly ? $"{i + 1}번 프레임 (키 전용)" : $"{i + 1}번 프레임", thumbnail));
            }

            // 세트가 아직 로드되지 않았거나 프레임이 줄었어도 저장된 선택을 잃지 않도록 자리표시 항목을 하나 둔다.
            if (keepIndex is int k && k >= frameCount)
            {
                choices.Add(new FrameChoice(k, $"{(long)k + 1}번 프레임 (없음)", null, Exists: false));
            }

            if (!choices.SequenceEqual(FrameChoices))
            {
                FrameChoices.Clear();
                foreach (var choice in choices)
                {
                    FrameChoices.Add(choice);
                }
            }

            SelectedFrame = FrameChoices.FirstOrDefault(c => c.Index is not null && c.Index == keepIndex) ?? FrameChoice.WholeSet;
        }
        finally
        {
            _refreshingChoices = false;
        }
    }

    partial void OnKeysTextChanged(string value) => Changed();

    partial void OnHoldMsChanged(int value) => Changed();

    partial void OnResetIndexChanged(bool value) => Changed();

    partial void OnSelectedFrameChanged(FrameChoice? value)
    {
        OnPropertyChanged(nameof(FrameIndex));
        if (!_refreshingChoices)
        {
            Changed();
        }
    }

    private void Changed()
    {
        Validate();
        Owner.CommitRules();
    }

    private List<string> ParseKeys() =>
        (KeysText ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private void Validate()
    {
        var keys = ParseKeys();
        if (keys.Count == 0)
        {
            Error = "키를 하나 이상 지정하세요. 예: Enter, Ctrl+S, * (모든 키)";
            return;
        }

        var invalid = keys.Where(k => !KeySpec.TryParse(k, out _)).ToList();
        if (invalid.Count > 0)
        {
            Error = $"알 수 없는 키: {string.Join(", ", invalid)}";
            return;
        }

        if (SelectedFrame is { Exists: false } && FrameIndex is int missing)
        {
            // 세트에 프레임이 하나도 없으면(빈 세트·폴더 없음) 예시 세트가 대신 보이므로 '마지막 프레임' 안내는 맞지 않다.
            Error = FrameChoices.Any(c => c.Index is not null && c.Exists)
                ? $"사용 중인 세트에 {(long)missing + 1}번 프레임이 없습니다. 마지막 프레임이 대신 표시됩니다."
                : $"사용 중인 세트에 표시할 프레임이 없습니다. 이미지를 추가하면 {(long)missing + 1}번 프레임이 쓰입니다.";
            return;
        }

        Error = null;
    }
}
