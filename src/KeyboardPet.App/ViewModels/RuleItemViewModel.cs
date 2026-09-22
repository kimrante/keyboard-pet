using CommunityToolkit.Mvvm.ComponentModel;
using KeyboardPet.Core.Rules;

namespace KeyboardPet.App.ViewModels;

/// <summary>설정 창 "키 매핑" 탭의 한 행. 편집 즉시 검증하고 소유자에게 커밋한다.</summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _keysText;

    [ObservableProperty]
    private string _frameSet;

    [ObservableProperty]
    private int _holdMs;

    [ObservableProperty]
    private bool _resetIndex;

    [ObservableProperty]
    private string? _error;

    public RuleItemViewModel(SettingsViewModel owner, KeyRule rule)
    {
        Owner = owner;
        _keysText = string.Join(", ", rule.Keys);
        _frameSet = rule.FrameSet;
        _holdMs = rule.HoldMs;
        _resetIndex = rule.ResetIndex;
        Validate();
    }

    public SettingsViewModel Owner { get; }

    public KeyRule ToRule() => new(ParseKeys(), FrameSet ?? string.Empty, Math.Max(0, HoldMs), ResetIndex);

    public void AppendKey(string spec)
    {
        KeysText = string.IsNullOrWhiteSpace(KeysText) ? spec : $"{KeysText}, {spec}";
    }

    public void Revalidate() => Validate();

    partial void OnKeysTextChanged(string value) => Changed();

    partial void OnFrameSetChanged(string value) => Changed();

    partial void OnHoldMsChanged(int value) => Changed();

    partial void OnResetIndexChanged(bool value) => Changed();

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
