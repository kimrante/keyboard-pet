using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Rules;

/// <summary>
/// 규칙 하나가 반응하는 키 조건. "Enter", "Ctrl+S", "Ctrl+Shift+A", 와일드카드 "*" 를 지원한다.
/// 조합키를 명시하지 않은 스펙("A")은 조합키 상태와 무관하게 매칭된다(Shift+A로 대문자를 쳐도 반응).
/// 조합키를 명시한 스펙("Ctrl+S")은 조합키 상태가 정확히 같을 때만 매칭된다.
/// 와일드카드는 순수 조합키(Shift, Ctrl 등 단독 입력)에는 반응하지 않는다.
/// </summary>
public readonly record struct KeySpec(int VirtualKey, KeyModifiers Modifiers, bool IsWildcard)
{
    public const string WildcardToken = "*";

    public static KeySpec Wildcard { get; } = new(0, KeyModifiers.None, true);

    public static KeySpec Parse(string text)
    {
        if (!TryParse(text, out var spec))
        {
            throw new FormatException($"키 이름을 해석할 수 없습니다: '{text}'");
        }

        return spec;
    }

    public static bool TryParse(string? text, out KeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // "Ctrl+" 처럼 비어 있는 조각이 있으면 형식 오류로 본다(빈 항목을 제거하면 "Ctrl" 단독 키로 오인된다).
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!TryParseModifier(parts[i], out var mod))
            {
                return false;
            }

            modifiers |= mod;
        }

        var last = parts[^1];
        if (last == WildcardToken)
        {
            spec = new KeySpec(0, modifiers, true);
            return true;
        }

        if (!KeyNames.TryGetVirtualKey(last, out var vk))
        {
            return false;
        }

        spec = new KeySpec(vk, modifiers, false);
        return true;
    }

    public bool Matches(KeyEvent e)
    {
        if (IsWildcard)
        {
            if (KeyNames.IsModifierKey(e.VirtualKey))
            {
                return false;
            }
        }
        else if (e.VirtualKey != VirtualKey)
        {
            return false;
        }

        return Modifiers == KeyModifiers.None || e.Modifiers == Modifiers;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(IsWildcard ? WildcardToken : KeyNames.GetName(VirtualKey));
        return string.Join("+", parts);
    }

    private static bool TryParseModifier(string token, out KeyModifiers modifier)
    {
        modifier = token.ToLowerInvariant() switch
        {
            "ctrl" or "control" => KeyModifiers.Control,
            "shift" => KeyModifiers.Shift,
            "alt" => KeyModifiers.Alt,
            "win" or "windows" or "meta" => KeyModifiers.Win,
            _ => KeyModifiers.None,
        };
        return modifier != KeyModifiers.None;
    }
}
