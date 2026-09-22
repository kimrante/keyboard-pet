namespace KeyboardPet.Core.Rules;

/// <summary>
/// 설정 파일과 UI에서 쓰는 키 이름 ↔ Windows 가상 키 코드(VK_*) 매핑.
/// 이름은 대소문자를 구분하지 않는다.
/// </summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> VkToName = new();

    static KeyNames()
    {
        // 문자·숫자
        for (var c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c);
        for (var d = '0'; d <= '9'; d++) Add(d.ToString(), d);

        // 기능키
        for (var i = 1; i <= 24; i++) Add($"F{i}", 0x70 + i - 1);

        // 편집·이동
        Add("Backspace", 0x08); Add("Tab", 0x09); Add("Enter", 0x0D); Add("Escape", 0x1B, "Esc");
        Add("Space", 0x20); Add("PageUp", 0x21); Add("PageDown", 0x22); Add("End", 0x23); Add("Home", 0x24);
        Add("Left", 0x25); Add("Up", 0x26); Add("Right", 0x27); Add("Down", 0x28);
        Add("Insert", 0x2D, "Ins"); Add("Delete", 0x2E, "Del");
        Add("PrintScreen", 0x2C); Add("Pause", 0x13); Add("CapsLock", 0x14);
        Add("NumLock", 0x90); Add("ScrollLock", 0x91); Add("Apps", 0x5D, "Menu");

        // 한국어 IME
        Add("Hangul", 0x15, "HanYeong", "한영"); Add("Hanja", 0x19, "한자");

        // 조합키 자체(단독 규칙용). 조합 접두어(Ctrl+)는 KeySpec.Parse가 따로 처리한다.
        Add("Shift", 0x10); Add("Control", 0x11, "Ctrl"); Add("Alt", 0x12, "Menu_Alt");
        Add("LShift", 0xA0); Add("RShift", 0xA1); Add("LControl", 0xA2, "LCtrl"); Add("RControl", 0xA3, "RCtrl");
        Add("LAlt", 0xA4); Add("RAlt", 0xA5); Add("LWin", 0x5B); Add("RWin", 0x5C);

        // 숫자 패드
        for (var i = 0; i <= 9; i++) Add($"NumPad{i}", 0x60 + i);
        Add("Multiply", 0x6A, "NumPad*"); Add("Add", 0x6B, "NumPad+"); Add("Subtract", 0x6D, "NumPad-");
        Add("Decimal", 0x6E, "NumPad."); Add("Divide", 0x6F, "NumPad/");

        // OEM (US 배열 기준 이름)
        Add("Semicolon", 0xBA, ";"); Add("Equals", 0xBB, "=", "Plus"); Add("Comma", 0xBC, ",");
        Add("Minus", 0xBD, "-"); Add("Period", 0xBE, "."); Add("Slash", 0xBF, "/");
        Add("Backquote", 0xC0, "`", "Tilde", "Grave"); Add("OpenBracket", 0xDB, "[");
        Add("Backslash", 0xDC, "\\"); Add("CloseBracket", 0xDD, "]"); Add("Quote", 0xDE, "'", "Apostrophe");

        // 미디어/브라우저 (일부)
        Add("VolumeMute", 0xAD); Add("VolumeDown", 0xAE); Add("VolumeUp", 0xAF);
        Add("MediaNext", 0xB0); Add("MediaPrev", 0xB1); Add("MediaStop", 0xB2); Add("MediaPlayPause", 0xB3);
    }

    /// <summary>순수 조합키(Shift/Ctrl/Alt/Win 계열)의 가상 키 코드인지.</summary>
    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>이름 또는 "0x41" / "65" 형태의 숫자를 가상 키 코드로 변환한다.</summary>
    public static bool TryGetVirtualKey(string name, out int vk)
    {
        name = name.Trim();
        if (NameToVk.TryGetValue(name, out vk))
        {
            return true;
        }

        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out vk))
        {
            return vk is > 0 and < 256;
        }

        if (int.TryParse(name, out vk))
        {
            return vk is > 0 and < 256;
        }

        vk = 0;
        return false;
    }

    /// <summary>표시용 이름. 표에 없으면 "0x.." 형식.</summary>
    public static string GetName(int vk) =>
        VkToName.TryGetValue(vk, out var name) ? name : $"0x{vk:X2}";

    public static IReadOnlyCollection<string> AllNames => VkToName.Values;

    private static void Add(string canonical, int vk, params string[] aliases)
    {
        NameToVk[canonical] = vk;
        VkToName.TryAdd(vk, canonical);
        foreach (var alias in aliases)
        {
            NameToVk[alias] = vk;
        }
    }
}
