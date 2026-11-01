using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoldSpeak.App;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>UI-only translations. Settings, user names and diagnostic exports stay unchanged.</summary>
public static class L
{
    public static IReadOnlyList<LanguageOption> Languages { get; } =
        [new("zh-CN", "简体中文"), new("en", "English"), new("ja", "日本語"), new("ko", "한국어")];
    private static readonly Dictionary<string, string[]> resources = LoadResources();
    private static readonly (Regex Pattern, string Key)[] templates = resources.Keys
        .Where(k => k.Contains("{0}", StringComparison.Ordinal))
        .Select(k => (new Regex("^" + Regex.Replace(Regex.Escape(k), @"\\\{\d+}", "(.+?)") + "$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), k)).ToArray();
    public static string CurrentLanguage { get; private set; } = ReadPreference();
    private static string PreferencePath => Path.Combine(AppPaths.Directory, "language.json");
    public static void ReloadPreference() => CurrentLanguage = ReadPreference();
    private static string ReadPreference()
    {
        try
        {
            var code = JsonSerializer.Deserialize<string>(File.ReadAllText(PreferencePath));
            return Languages.Any(l => l.Code == code) ? code! : "zh-CN";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return "zh-CN"; }
    }
    public static void SavePreference(string code)
    {
        if (!Languages.Any(l => l.Code == code)) throw new ArgumentException("Unknown UI language", nameof(code));
        Directory.CreateDirectory(AppPaths.Directory);
        var temporary = PreferencePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(code));
        File.Move(temporary, PreferencePath, true);
    }
    private static Dictionary<string, string[]> LoadResources()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("HoldSpeak.App.Translations.json")
            ?? throw new InvalidOperationException("Missing translation resources");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
    }
    public static string T(string text) => Translate(text, CurrentLanguage);
    public static string Translate(string text, string language)
    {
        if (language == "zh-CN" || !resources.TryGetValue(text, out var values)) return text;
        int index = language switch { "en" => 0, "ja" => 1, "ko" => 2, _ => -1 };
        return index < 0 ? text : values[index];
    }
    public static string F(string template, params object?[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(CurrentLanguage), T(template), arguments);

    // Engine and OS errors remain stable in the core. Translate only their displayed copy.
    public static string Message(string text) => Message(text, 0);
    private static string Message(string text, int depth)
    {
        if (CurrentLanguage == "zh-CN" || depth > 4) return text;
        if (resources.ContainsKey(text)) return T(text);
        foreach (var (pattern, key) in templates)
        {
            var match = pattern.Match(text);
            if (match.Success) return F(key, match.Groups.Cast<Group>().Skip(1)
                .Select(g => (object)(key == "说话已结束：{0}" ? Message(g.Value, depth + 1) : g.Value)).ToArray());
        }
        // A translated prefix may be followed by an OS error with arbitrary detail.
        foreach (var prefix in resources.Keys.Where(k => k.EndsWith('：')).OrderByDescending(k => k.Length))
            if (text.StartsWith(prefix, StringComparison.Ordinal)) return T(prefix) + Message(text[prefix.Length..], depth + 1);
        return text;
    }
}
