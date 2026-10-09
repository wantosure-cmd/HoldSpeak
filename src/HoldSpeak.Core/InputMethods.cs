namespace HoldSpeak.Core;

public enum ShortcutSelectionMode { Fixed, Automatic }
public sealed record InputMethodInfo(string Id, string Name, string Preset = "")
{
    public override string ToString() => Name;
}
public sealed record InputMethodBinding
{
    public string InputMethodId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ProfileId { get; init; } = "";
}
public static class InputMethodPresets
{
    public static VoiceProfile? Create(InputMethodInfo input) => input.Preset switch
    {
        "wechat" => new() { Id = "ime-wechat", Name = "微信输入法" },
        "sogou" => new() { Id = "ime-sogou", Name = "搜狗输入法", Mode = ActivationMode.Toggle, WindowsKeys = [Key.F2] },
        _ => null
    };
}
