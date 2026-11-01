using Avalonia.Input;
using HoldSpeak.Core;
using InputKey = Avalonia.Input.Key;
using Key = HoldSpeak.Core.Key;

namespace HoldSpeak.App;

internal sealed class ShortcutCapture(bool mac)
{
    private readonly List<Key> chord = [];
    private readonly HashSet<Key> down = [];
    private bool releasing;

    public void Press(InputKey logical, PhysicalKey physical, KeyModifiers modifiers)
    {
        var key = Map(logical, physical, mac) ?? throw new InvalidDataException(L.T("此按键暂不支持，请手动填写已支持的按键。"));
        var expected = down.Aggregate(Modifier(key), (flags, held) => flags | Modifier(held));
        var relevant = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;
        if ((modifiers & relevant & ~expected) != 0)
            throw new InvalidDataException(L.T("录入前已有修饰键按住，请松开所有键后重新录入。"));
        if (down.Contains(key)) return; // Ignore OS key repeat.
        if (releasing) throw new InvalidDataException(L.T("请同时按下一个组合键，不支持连续快捷键序列。"));
        if (chord.Count == 5) throw new InvalidDataException(L.T("组合键最多包含 5 个不同按键。"));
        down.Add(key); chord.Add(key);
    }

    public Key[]? Release(InputKey logical, PhysicalKey physical)
    {
        var key = Map(logical, physical, mac);
        if (key == null || !down.Remove(key.Value)) return null;
        releasing = true;
        // Send modifiers before ordinary keys even when the user's down events
        // arrived in the opposite order; preserve order within each group.
        if (down.Count != 0) return null;
        return [..chord.Where(k => Modifier(k) != KeyModifiers.None), ..chord.Where(k => Modifier(k) == KeyModifiers.None)];
    }

    internal static Key? Map(InputKey logical, PhysicalKey physical, bool mac)
    {
        string name = physical == PhysicalKey.None ? logical.ToString() : physical.ToString();
        // Mac injection uses physical US key positions; logical letters on another
        // layout cannot safely stand in for a missing physical-key observation.
        if (mac && physical == PhysicalKey.None && (name.Length == 1 || name.StartsWith("D", StringComparison.Ordinal) && name.Length == 2)) return null;
        string? mapped = name switch
        {
            "ControlLeft" or "LeftCtrl" => mac ? "Control" : "LCtrl",
            "ControlRight" or "RightCtrl" => mac ? null : "RCtrl",
            "AltLeft" or "LeftAlt" => mac ? "Option" : "LAlt",
            "AltRight" or "RightAlt" => mac ? null : "RAlt",
            "ShiftLeft" or "LeftShift" => mac ? "Shift" : "LShift",
            "ShiftRight" or "RightShift" => mac ? null : "RShift",
            "MetaLeft" or "LWin" => mac ? "Command" : "LWin",
            "MetaRight" or "RWin" => mac ? "RightCommand" : "RWin",
            "ArrowLeft" => "Left", "ArrowRight" => "Right", "ArrowUp" => "Up", "ArrowDown" => "Down",
            _ when name.StartsWith("Digit", StringComparison.Ordinal) => "D" + name[5..],
            _ => name
        };
        if (mapped == null || !Enum.TryParse<Key>(mapped, out var key)) return null;
        return (mac ? KeyCatalog.TryMac(key, out _, out _) : KeyCatalog.TryWindows(key, out _, out _)) ? key : null;
    }

    private static KeyModifiers Modifier(Key key) => key switch
    {
        Key.LCtrl or Key.RCtrl or Key.Control => KeyModifiers.Control,
        Key.LAlt or Key.RAlt or Key.Option => KeyModifiers.Alt,
        Key.LShift or Key.RShift or Key.Shift => KeyModifiers.Shift,
        Key.LWin or Key.RWin or Key.Command or Key.RightCommand => KeyModifiers.Meta,
        _ => KeyModifiers.None
    };
}
