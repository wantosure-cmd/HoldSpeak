using System.Runtime.InteropServices;
using System.IO;
using Microsoft.Win32;
using HoldSpeak.Core;

namespace HoldSpeak.Windows;

public static class WindowsInputMethods
{
    private static readonly Guid KeyboardCategory = new("34745c63-b2f0-4784-8b67-5e12c8701a31");
    [StructLayout(LayoutKind.Sequential)]
    private struct Profile
    {
        public uint Type; public ushort Language; public Guid Class, Id, Category;
        public nint Substitute; public uint Caps; public nint Layout; public uint Flags;
    }
    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProfileManager
    {
        [PreserveSig] int Activate(uint type, ushort language, in Guid clsid, in Guid profile, nint hkl, uint flags);
        [PreserveSig] int Deactivate(uint type, ushort language, in Guid clsid, in Guid profile, nint hkl, uint flags);
        [PreserveSig] int GetProfile(uint type, ushort language, in Guid clsid, in Guid profile, nint hkl, out Profile result);
        [PreserveSig] int EnumProfiles(ushort language, out IProfiles profiles);
        [PreserveSig] int ReleaseProcessor(in Guid clsid, uint flags);
        [PreserveSig] int Register(in Guid clsid, ushort language, in Guid profile, nint description, uint length, nint icon, uint iconLength, uint index, nint substitute, uint preferred, int enabled, uint flags);
        [PreserveSig] int Unregister(in Guid clsid, ushort language, in Guid profile, uint flags);
        [PreserveSig] int GetActive(in Guid category, out Profile result);
    }
    [ComImport, Guid("71c6e74d-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProfiles
    {
        [PreserveSig] int Clone(out IProfiles clone);
        [PreserveSig] int Next(uint count, out Profile profile, out uint fetched);
        [PreserveSig] int Reset();
        [PreserveSig] int Skip(uint count);
    }
    [DllImport("msctf.dll")] private static extern int TF_CreateInputProcessorProfiles([MarshalAs(UnmanagedType.IUnknown)] out object profiles);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    private static (Profile[] Profiles, Profile? Active) Query(ushort language)
    {
        object? owner = null; IProfiles? enumeration = null;
        try
        {
            if (TF_CreateInputProcessorProfiles(out owner) < 0) return ([], null);
            var manager = (IProfileManager)owner;
            var result = new List<Profile>();
            if (manager.EnumProfiles(language, out enumeration) >= 0)
                while (result.Count < 256 && enumeration.Next(1, out var p, out uint fetched) == 0 && fetched == 1) result.Add(p);
            Profile? active = manager.GetActive(KeyboardCategory, out var current) == 0 ? current : null;
            return (result.ToArray(), active);
        }
        catch (COMException) { return ([], null); }
        finally
        {
            if (enumeration != null) Marshal.ReleaseComObject(enumeration);
            if (owner != null) Marshal.ReleaseComObject(owner);
        }
    }
    private static InputMethodInfo Describe(Profile p)
    {
        if (p.Type != 1) return new($"win-layout:{unchecked((uint)p.Layout):x8}", $"键盘布局 {unchecked((uint)p.Layout):x8}");
        string name = "";
        string path = $"SOFTWARE\\Microsoft\\CTF\\TIP\\{{{p.Class:D}}}\\LanguageProfile\\0x{p.Language:x8}\\{{{p.Id:D}}}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(path);
            if (key?.GetValue("Description") is string description && !string.IsNullOrWhiteSpace(description)) { name = description; break; }
        }
        if (string.IsNullOrEmpty(name))
        {
            using var key = Registry.ClassesRoot.OpenSubKey($"CLSID\\{{{p.Class:D}}}");
            name = key?.GetValue("") as string ?? "";
        }
        // Stable identifiers are used for mappings; names only select optional presets.
        bool wechat = p.Class == new Guid("86598fb9-66a2-463e-b9c2-aeb906d477ad") || name.Contains("微信") || name.Contains("WeType", StringComparison.OrdinalIgnoreCase);
        bool sogou = p.Class == new Guid("e7ea138e-69f8-11d7-a6ea-00065b844310") || name.Contains("搜狗") || name.Contains("Sogou", StringComparison.OrdinalIgnoreCase);
        if (wechat) name = "微信输入法";
        if (sogou) name = "搜狗输入法";
        if (string.IsNullOrEmpty(name)) name = $"输入法 {p.Class:D}";
        return new($"win-tip:{p.Class:D}:{p.Id:D}", name, wechat ? "wechat" : sogou ? "sogou" : "");
    }
    public static InputMethodInfo[] List()
    {
        int count = GetKeyboardLayoutList(0, null); var layouts = new nint[Math.Max(1, count)];
        count = GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.Take(count).Select(h => (ushort)h).Distinct().SelectMany(l => Query(l).Profiles)
            .Where(p => p.Type == 1 && p.Category == KeyboardCategory && (p.Flags & 2) != 0).Select(Describe).DistinctBy(p => p.Id).ToArray();
    }
    public static InputMethodInfo? Current()
    {
        try { return ReadCurrent(); }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or System.Security.SecurityException or IOException) { return null; }
    }
    private static InputMethodInfo? ReadCurrent()
    {
        nint window = Native.GetForegroundWindow();
        uint thread = Native.GetWindowThreadProcessId(window, out _);
        if (window == 0 || thread == 0) return null;
        nint layout = GetKeyboardLayout(thread);
        var (profiles, active) = Query((ushort)layout);
        var matches = profiles.Where(p => p.Type == 1 && p.Category == KeyboardCategory && p.Substitute != 0 && unchecked((uint)p.Substitute) == unchecked((uint)layout)).ToArray();
        // TIPs may share the language's base HKL. A same-language active TSF
        // profile disambiguates them; no process-name or language-only inference.
        if (Native.GetForegroundWindow() != window || GetKeyboardLayout(thread) != layout) return null;
        if (matches.Length == 1) return Describe(matches[0]);
        if (active is { Type: 1 } p && p.Substitute != 0 && unchecked((uint)p.Substitute) == unchecked((uint)layout)) return Describe(p);
        if (active is { Type: 1 } tip && tip.Language == (ushort)layout && tip.Category == KeyboardCategory && (tip.Flags & 1) != 0 && profiles.Any(p => p.Class == tip.Class && p.Id == tip.Id)) return Describe(tip);
        return null;
    }
    public static object Inspect()
    {
        uint thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _); nint layout = GetKeyboardLayout(thread);
        var (profiles, active) = Query((ushort)layout);
        return new { foregroundLayout = unchecked((uint)layout).ToString("x8"), current = Current(), active = active.HasValue ? Describe(active.Value) : null,
            profiles = profiles.Select(p => new { input = Describe(p), substitute = unchecked((uint)p.Substitute).ToString("x8"), layout = unchecked((uint)p.Layout).ToString("x8"), p.Flags }) };
    }
}
