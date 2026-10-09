using System.Runtime.InteropServices;
using HoldSpeak.Core;

namespace HoldSpeak.Mac;

public static class MacInputMethods
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private static readonly Lazy<nint> Library = new(() => NativeLibrary.Load(Carbon));
    [DllImport(Carbon)] private static extern nint TISCopyCurrentKeyboardInputSource();
    [DllImport(Carbon)] private static extern nint TISCreateInputSourceList(nint properties, byte includeAll);
    [DllImport(Carbon)] private static extern nint TISGetInputSourceProperty(nint source, nint key);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern nint CFArrayGetCount(nint array);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    private static InputMethodInfo? Describe(nint source)
    {
        if (source == 0) return null;
        nint Key(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Library.Value, name));
        string id = MacNative.ReadString(TISGetInputSourceProperty(source, Key("kTISPropertyInputSourceID")));
        string name = MacNative.ReadString(TISGetInputSourceProperty(source, Key("kTISPropertyLocalizedName")));
        return string.IsNullOrEmpty(id) ? null : new("mac-source:" + id, string.IsNullOrEmpty(name) ? id : name);
    }
    public static InputMethodInfo? Current()
    {
        nint source = TISCopyCurrentKeyboardInputSource();
        try { return Describe(source); } finally { if (source != 0) MacNative.CFRelease(source); }
    }
    public static InputMethodInfo[] List()
    {
        nint array = TISCreateInputSourceList(0, 0);
        if (array == 0) return [];
        try
        {
            var result = new List<InputMethodInfo>();
            for (nint i = 0; i < CFArrayGetCount(array); i++)
                if (Describe(CFArrayGetValueAtIndex(array, i)) is { } source) result.Add(source);
            return result.DistinctBy(s => s.Id).ToArray();
        }
        finally { MacNative.CFRelease(array); }
    }
}
