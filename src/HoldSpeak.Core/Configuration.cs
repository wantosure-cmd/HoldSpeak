using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoldSpeak.Core;

public enum ActivationMode { Hold, Toggle, Separate }
public enum Key
{
    LCtrl, RCtrl, LWin, RWin, LAlt, RAlt, LShift, RShift, Command, RightCommand, Control, Option, Shift, Space, F8, F9, F10, F11, F12,
    Enter = 30, Tab, Escape, Backspace, Delete, Home, End, PageUp, PageDown, Left, Right, Up, Down,
    A = 100, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0 = 130, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1 = 200, F2, F3, F4, F5, F6, F7, F13 = 212, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24
}
public sealed record VoiceProfile
{
    public string Id { get; init; } = "default";
    public string Name { get; init; } = "Ctrl + Win 长按";
    public ActivationMode Mode { get; init; } = ActivationMode.Hold;
    public Key[] WindowsKeys { get; init; } = [Key.LCtrl, Key.LWin];
    public Key[] MacKeys { get; init; } = [];
    public Key[] WindowsStopKeys { get; init; } = [];
    public Key[] MacStopKeys { get; init; } = [];
    public int MaxHoldSeconds { get; init; } = 120;
    public string[] AllowedOverlayApps { get; init; } = [];
    public Key[] Keys(bool mac) => mac ? MacKeys : WindowsKeys;
    public Key[] StopKeys(bool mac) => mac ? MacStopKeys : WindowsStopKeys;
}

public sealed record AppRule
{
    public string AppId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string? ProfileId { get; init; }
}

public sealed record Settings
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public int HoldMs { get; init; } = 600;
    public int MovementDip { get; init; } = 8;
    public int ProbeTimeoutMs { get; init; } = 250;
    public bool ReleaseMouseOnActivate { get; init; } = true;
    public bool AllowUnconfirmedTargets { get; init; } = true;
    public bool TriggerAnywhere { get; init; } = true;
    public string DefaultProfileId { get; init; } = "default";
    public ShortcutSelectionMode ShortcutSelection { get; init; } = ShortcutSelectionMode.Fixed;
    public InputMethodBinding[] InputMethodBindings { get; init; } = [];
    public VoiceProfile[] Profiles { get; init; } = [new()];
    public AppRule[] AppRules { get; init; } = [];

    public void Validate()
    {
        if (Profiles == null || AppRules == null || string.IsNullOrWhiteSpace(DefaultProfileId) || Profiles.Any(p => p == null) || AppRules.Any(r => r == null))
            throw new InvalidDataException("配置集合不能为空。");
        if (SchemaVersion != 1) throw new InvalidDataException("不支持的配置版本。");
        if (!Enum.IsDefined(ShortcutSelection) || InputMethodBindings == null || InputMethodBindings.Length > 100 ||
            InputMethodBindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.InputMethodId) || string.IsNullOrWhiteSpace(b.Name) || !Profiles.Any(p => p.Id == b.ProfileId)) ||
            InputMethodBindings.Select(b => b.InputMethodId).Distinct(StringComparer.Ordinal).Count() != InputMethodBindings.Length)
            throw new InvalidDataException("输入法映射无效、重复或引用了不存在的快捷键配置。");
        if (HoldMs is < 300 or > 1500 || MovementDip is < 2 or > 40 || ProbeTimeoutMs is < 50 or > 1000)
            throw new InvalidDataException("长按、移动容差或探测超时超出允许范围。");
        if (Profiles.Length is < 1 or > 100 || Profiles.Any(p => string.IsNullOrWhiteSpace(p.Id)) ||
            Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != Profiles.Length ||
            !Profiles.Any(p => p.Id == DefaultProfileId)) throw new InvalidDataException("配置 ID 必须唯一，且默认配置必须存在。");
        Key[] win = KeyCatalog.ForPlatform(false);
        Key[] mac = KeyCatalog.ForPlatform(true);
        foreach (var p in Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.WindowsKeys == null || p.MacKeys == null || p.WindowsStopKeys == null || p.MacStopKeys == null || p.AllowedOverlayApps == null)
                throw new InvalidDataException("语音配置字段不能为空。");
            if (!Enum.IsDefined(p.Mode) || p.MaxHoldSeconds is < 5 or > 600) throw new InvalidDataException("动作协议或最长保持时间无效。");
            if (p.WindowsKeys.Length == 0 && p.MacKeys.Length == 0) throw new InvalidDataException("至少配置一个平台的快捷键。");
            if (p.WindowsKeys.Length > 0) Check(p.WindowsKeys, win);
            if (p.MacKeys.Length > 0) Check(p.MacKeys, mac);
            if (p.WindowsStopKeys.Length > 0) Check(p.WindowsStopKeys, win);
            if (p.MacStopKeys.Length > 0) Check(p.MacStopKeys, mac);
            if (p.Mode == ActivationMode.Separate)
            {
                if (p.WindowsKeys.Length > 0) Check(p.WindowsStopKeys, win);
                if (p.MacKeys.Length > 0) Check(p.MacStopKeys, mac);
            }
            if (p.AllowedOverlayApps.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("辅助窗口应用标识不能为空。");
        }
        if (AppRules.Any(r => string.IsNullOrWhiteSpace(r.AppId) || (r.ProfileId != null && !Profiles.Any(p => p.Id == r.ProfileId))) ||
            AppRules.Select(r => r.AppId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != AppRules.Length)
            throw new InvalidDataException("应用规则必须唯一，引用的语音配置必须存在。");
    }
    private static void Check(Key[] keys, Key[] allowed)
    {
        if (keys.Length is < 1 or > 5 || keys.Distinct().Count() != keys.Length || keys.Any(k => !allowed.Contains(k)))
            throw new InvalidDataException("快捷键为空、重复或包含不支持的平台按键。");
    }
    public VoiceProfile? Resolve(string appId)
    {
        var rule = AppRules.FirstOrDefault(r => string.Equals(r.AppId, appId, StringComparison.OrdinalIgnoreCase));
        if (rule is { Enabled: false }) return null;
        return Profiles.First(p => p.Id == (rule?.ProfileId ?? DefaultProfileId));
    }
    public VoiceProfile? ResolveShortcut(string appId, InputMethodInfo? inputMethod)
    {
        if (AppRules.Any(r => !r.Enabled && r.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase))) return null;
        // Application overrides must not defeat the user's fixed shortcut or IME choice.
        if (ShortcutSelection == ShortcutSelectionMode.Fixed) return Profiles.First(p => p.Id == DefaultProfileId);
        var binding = InputMethodBindings.FirstOrDefault(b => b.InputMethodId == inputMethod?.Id);
        return binding == null ? null : Profiles.First(p => p.Id == binding.ProfileId);
    }
}

public static class ConfigurationStore
{
    public static Settings Load(string path, out bool recovered)
    {
        recovered = false;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new();
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            // Recovery does not overwrite the damaged file; the user can inspect it.
            var settings = Parse(File.ReadAllText(path + ".bak"));
            recovered = true;
            return settings;
        }
    }
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) }
    };
    public static Settings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json, Json) ?? throw new InvalidDataException("配置不能为空。");
        settings.Validate(); return settings;
    }
    public static void Save(string path, Settings settings)
    {
        settings.Validate();
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, Json);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                // Never replace a known-good backup with an invalid primary file.
                bool valid;
                try { Parse(File.ReadAllText(path)); valid = true; }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { valid = false; }
                if (valid)
                {
                    string backupTemp = temp + ".bak";
                    try { File.Copy(path, backupTemp); File.Move(backupTemp, path + ".bak", true); }
                    finally { if (File.Exists(backupTemp)) File.Delete(backupTemp); }
                }
                File.Move(temp, path, true);
            }
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
