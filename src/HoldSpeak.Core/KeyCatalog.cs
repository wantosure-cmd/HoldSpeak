namespace HoldSpeak.Core;

public static class KeyCatalog
{
    private static readonly Key[] FunctionKeys = [Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12, Key.F13, Key.F14, Key.F15, Key.F16, Key.F17, Key.F18, Key.F19, Key.F20, Key.F21, Key.F22, Key.F23, Key.F24];
    private static readonly ushort[] MacFunctionCodes = [122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111, 105, 107, 113, 106, 64, 79, 80, 90];
    private static readonly ushort[] MacLetterCodes = [0, 11, 8, 2, 14, 3, 5, 4, 34, 38, 40, 37, 46, 45, 31, 35, 12, 15, 1, 17, 32, 9, 13, 7, 16, 6];
    private static readonly ushort[] MacDigitCodes = [29, 18, 19, 20, 21, 23, 22, 26, 28, 25];
    public static Key[] ForPlatform(bool mac) => Enum.GetValues<Key>().Where(k => mac ? TryMac(k, out _, out _) : TryWindows(k, out _, out _)).ToArray();
    public static Key[] Parse(string? text)
    {
        var names = Enum.GetNames<Key>();
        return (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(token =>
        {
            string name = token.Length == 1 && token[0] is >= '0' and <= '9' ? "D" + token : token;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("未知按键：" + token);
            return Enum.Parse<Key>(name, true);
        }).ToArray();
    }
    public static bool TryWindows(Key key, out ushort code, out bool extended)
    {
        extended = key is Key.RCtrl or Key.RAlt or Key.LWin or Key.RWin or Key.Delete or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Left or Key.Right or Key.Up or Key.Down;
        if (key is >= Key.A and <= Key.Z) { code = (ushort)(0x41 + key - Key.A); return true; }
        if (key is >= Key.D0 and <= Key.D9) { code = (ushort)(0x30 + key - Key.D0); return true; }
        int fn = Array.IndexOf(FunctionKeys, key);
        if (fn >= 0) { code = (ushort)(0x70 + fn); return true; }
        code = key switch
        {
            Key.LCtrl => 0xA2, Key.RCtrl => 0xA3, Key.LWin => 0x5B, Key.RWin => 0x5C, Key.LAlt => 0xA4, Key.RAlt => 0xA5, Key.LShift => 0xA0, Key.RShift => 0xA1,
            Key.Space => 0x20, Key.Enter => 0x0D, Key.Tab => 0x09, Key.Escape => 0x1B, Key.Backspace => 0x08, Key.Delete => 0x2E, Key.Home => 0x24, Key.End => 0x23,
            Key.PageUp => 0x21, Key.PageDown => 0x22, Key.Left => 0x25, Key.Right => 0x27, Key.Up => 0x26, Key.Down => 0x28, _ => ushort.MaxValue
        };
        return code != ushort.MaxValue;
    }
    public static bool TryMac(Key key, out ushort code, out ulong flag)
    {
        flag = key switch { Key.Command or Key.RightCommand => 1UL << 20, Key.Control => 1UL << 18, Key.Option => 1UL << 19, Key.Shift => 1UL << 17, _ => 0 };
        if (key is >= Key.A and <= Key.Z) { code = MacLetterCodes[key - Key.A]; return true; }
        if (key is >= Key.D0 and <= Key.D9) { code = MacDigitCodes[key - Key.D0]; return true; }
        int fn = Array.IndexOf(FunctionKeys, key);
        if (fn is >= 0 and < 20) { code = MacFunctionCodes[fn]; return true; }
        code = key switch
        {
            Key.Command => 55, Key.RightCommand => 54, Key.Control => 59, Key.Option => 58, Key.Shift => 56, Key.Space => 49, Key.Enter => 36, Key.Tab => 48, Key.Escape => 53,
            Key.Backspace => 51, Key.Delete => 117, Key.Home => 115, Key.End => 119, Key.PageUp => 116, Key.PageDown => 121, Key.Left => 123, Key.Right => 124, Key.Up => 126, Key.Down => 125, _ => ushort.MaxValue
        };
        return code != ushort.MaxValue;
    }
}
