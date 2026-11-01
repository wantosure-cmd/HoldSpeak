using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace HoldSpeak.App;

public interface IAutoStart
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

// System registration is the source of truth; importing settings does not change login items.
public sealed class AutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HoldSpeak";
    private readonly string executable, configuration, agentPath, runKey;
    public AutoStart(string executable, string configuration, string? agentPath = null, string? runKey = null)
    {
        this.executable = Path.GetFullPath(executable);
        this.configuration = Path.GetFullPath(configuration);
        this.runKey = runKey ?? RunKey;
        this.agentPath = agentPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "org.holdspeak.app.plist");
    }
    public static IAutoStart Create() => new AutoStart(Environment.ProcessPath ?? throw new InvalidOperationException(L.T("无法确定程序路径。")), AppPaths.Directory);
    public string[] Arguments => [executable, "--autostart", "--config-dir", configuration];
    public string WindowsCommand => string.Join(" ", Arguments.Select(QuoteWindowsArgument));
    public static string QuoteWindowsArgument(string argument)
    {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public string MacPlist => new XDocument(new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            new XElement("key", "Label"), new XElement("string", "org.holdspeak.app"),
            new XElement("key", "ProgramArguments"), new XElement("array", Arguments.Select(a => new XElement("string", a))),
            new XElement("key", "RunAtLoad"), new XElement("true")))).ToString();
    public bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var run = Registry.CurrentUser.OpenSubKey(runKey);
            return string.Equals(run?.GetValue(ValueName) as string, WindowsCommand, StringComparison.Ordinal);
        }
        if (OperatingSystem.IsMacOS())
        {
            if (!File.Exists(agentPath)) return false;
            var document = XDocument.Load(agentPath);
            var array = document.Descendants("key").FirstOrDefault(k => k.Value == "ProgramArguments")?.ElementsAfterSelf().FirstOrDefault();
            return array?.Name == "array" && array.Elements("string").Select(s => s.Value).SequenceEqual(Arguments);
        }
        return false;
    }
    public void SetEnabled(bool enabled)
    {
        if (enabled && (!File.Exists(executable) || Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.T("请使用发布包中的程序设置开机自启动。"));
        if (OperatingSystem.IsWindows())
        {
            using var run = Registry.CurrentUser.CreateSubKey(runKey, true);
            if (enabled) run.SetValue(ValueName, WindowsCommand, RegistryValueKind.String);
            else run.DeleteValue(ValueName, false);
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            if (!enabled) { File.Delete(agentPath); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);
            var temporary = agentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, MacPlist, new UTF8Encoding(false)); File.Move(temporary, agentPath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return;
        }
        throw new PlatformNotSupportedException(L.T("当前系统不支持开机自启动。"));
    }
}
