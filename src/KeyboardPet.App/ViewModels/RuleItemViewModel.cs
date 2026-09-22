using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KeyboardPet.Core.Rules;

namespace KeyboardPet.App.ViewModels;

/// <summary>규칙의 "프레임" 콤보박스 항목. Index가 null이면 세트 전체 애니메이션.</summary>
public sealed record FrameChoice(int? Index, string Label, ImageSource? Thumbnail)
{
    public static FrameChoice WholeSet { get; } = new(null, "전체 애니메이션", null);

    public override string ToString() => Label;
}

/// <summary>설정 창 "키 매핑" 탭의 한 행. 편집 즉시 검증하고 소유자에게 커밋한다.</summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    private bool _refreshingChoices;

    [ObservableProperty]
    private string _keysText;

    [ObservableProperty]
    private string _frameSet;

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
        _frameSet = rule.FrameSet;
        _holdMs = rule.HoldMs;
        _resetIndex = rule.ResetIndex;
        RefreshFrameChoices(rule.FrameIndex);
        Validate();
    }

    public SettingsViewModel Owner { get; }

    public ObservableCollection<FrameChoice> FrameChoices { get; } = new();

    public int? FrameIndex => SelectedFrame?.Index;

    public KeyRule ToRule() => new(ParseKeys(), FrameSet ?? string.Empty, Math.Max(0, HoldMs), ResetIndex, FrameIndex);

    public void AppendKey(string spec)
    {
        KeysText = string.IsNullOrWhiteSpace(KeysText) ? spec : $"{KeysText}, {spec}";
    }

    public void Revalidate()
    {
        RefreshFrameChoices(FrameIndex);
        Validate();
    }

    /// <summary>선택된 세트의 프레임 목록으로 콤보박스 항목을 다시 만든다. 가능하면 기존 선택을 유지한다.</summary>
    public void RefreshFrameChoices(int? keepIndex)
    {
        _refreshingChoices = true;
        try
        {
            var frames = Owner.GetFrames(FrameSet ?? string.Empty);
            var loop = Owner.GetLoopFrames(FrameSet ?? string.Empty);
            var choices = new List<FrameChoice> { FrameChoice.WholeSet };
            for (var i = 0; i < frames.Count; i++)
            {
                var keyOnly = loop is not null && !loop.Contains(i);
                choices.Add(new FrameChoice(i, keyOnly ? $"{i + 1}번 프레임 (키 전용)" : $"{i + 1}번 프레임", frames[i]));
            }

            if (!choices.SequenceEqual(FrameChoices))
            {
                FrameChoices.Clear();
                foreach (var choice in choices)
                {
                    FrameChoices.Add(choice);
                }
            }

            SelectedFrame = keepIndex is int k && k >= 0 && k < frames.Count
                ? FrameChoices[k + 1]
                : FrameChoice.WholeSet;
        }
        finally
        {
            _refreshingChoices = false;
        }
    }

    partial void OnKeysTextChanged(string value) => Changed();

    partial void OnFrameSetChanged(string value)
    {
        RefreshFrameChoices(FrameIndex);
        Changed();
    }

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

        if (string.IsNullOrWhiteSpace(FrameSet) || !Owner.AvailableSetNames.Contains(FrameSet, StringComparer.OrdinalIgnoreCase))
        {
            Error = "이미지 세트를 선택하세요.";
            return;
        }

        Error = null;
    }
}
