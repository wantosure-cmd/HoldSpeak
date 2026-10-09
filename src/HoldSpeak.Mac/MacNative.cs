using System.Runtime.InteropServices;

namespace HoldSpeak.Mac;

internal static class MacNative
{
    internal const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    internal const string AX = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    internal const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    internal const string ObjC = "/usr/lib/libobjc.A.dylib";
    internal const long Marker = 0x484F4C44;
    private static readonly Lazy<nint> AppKit = new(() => NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
    private static readonly Lazy<nint> BooleanTrue = new(() => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CF), "kCFBooleanTrue")));
    internal static nint True => BooleanTrue.Value;
    internal static void EnsureAppKit() => _ = AppKit.Value;
    internal static nint AppKitConstant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(AppKit.Value, name));
    [StructLayout(LayoutKind.Sequential)] internal readonly record struct Point(double X, double Y);
    internal delegate nint TapCallback(nint proxy, uint type, nint e, nint context);
    [DllImport(CG)] internal static extern nint CGEventTapCreate(uint tap, uint place, uint options, ulong mask, TapCallback callback, nint context);
    [DllImport(CG)] internal static extern void CGEventTapEnable(nint tap, [MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(CG)] internal static extern Point CGEventGetLocation(nint e);
    [DllImport(CG)] internal static extern long CGEventGetIntegerValueField(nint e, uint field);
    [DllImport(CG)] internal static extern void CGEventSetIntegerValueField(nint e, uint field, long value);
    [DllImport(CG)] internal static extern ulong CGEventGetFlags(nint e);
    [DllImport(CG)] internal static extern void CGEventSetFlags(nint e, ulong flags);
    [DllImport(CG)] internal static extern void CGEventSetType(nint e, uint type);
    [DllImport(CG)] internal static extern nint CGEventCreateKeyboardEvent(nint source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CG)] internal static extern nint CGEventCreateMouseEvent(nint source, uint type, Point point, uint button);
    [DllImport(CG)] internal static extern nint CGEventCreate(nint source);
    [DllImport(CG)] internal static extern void CGEventPost(uint tap, nint e);
    [DllImport(CG)] internal static extern ulong CGEventSourceFlagsState(uint state);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CGEventSourceKeyState(uint state, ushort key);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CGPreflightListenEventAccess();
    [DllImport(AX)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool AXIsProcessTrusted();
    [DllImport(AX)] internal static extern nint AXUIElementCreateSystemWide();
    [DllImport(AX)] internal static extern nint AXUIElementCreateApplication(int pid);
    [DllImport(AX)] internal static extern int AXUIElementCopyElementAtPosition(nint app, float x, float y, out nint element);
    [DllImport(AX)] internal static extern int AXUIElementCopyAttributeValue(nint element, nint name, out nint value);
    [DllImport(AX)] internal static extern int AXUIElementSetAttributeValue(nint element, nint name, nint value);
    [DllImport(AX)] internal static extern int AXUIElementIsAttributeSettable(nint element, nint name, [MarshalAs(UnmanagedType.I1)] out bool settable);
    [DllImport(AX)] internal static extern int AXUIElementGetPid(nint element, out int pid);
    [DllImport(AX)] internal static extern int AXUIElementSetMessagingTimeout(nint element, float timeout);
    [DllImport(CF)] internal static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CFStringGetCString(nint value, byte[] buffer, nint size, uint encoding);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CFBooleanGetValue(nint value);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CFEqual(nint a, nint b);
    [DllImport(CF)] internal static extern void CFRelease(nint value);
    [DllImport(CF)] internal static extern nuint CFHash(nint value);
    [DllImport(CF)] internal static extern nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);
    [DllImport(CF)] internal static extern nint CFRunLoopGetCurrent();
    [DllImport(CF)] internal static extern void CFRunLoopAddSource(nint loop, nint source, nint mode);
    [DllImport(CF)] internal static extern void CFRunLoopRun();
    [DllImport(CF)] internal static extern void CFRunLoopStop(nint loop);
    [DllImport(ObjC)] internal static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] internal static extern nint objc_allocateClassPair(nint superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extraBytes);
    [DllImport(ObjC)] internal static extern void objc_registerClassPair(nint cls);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool class_addMethod(nint cls, nint selector, nint implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);
    [DllImport(ObjC)] internal static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] internal static extern nint objc_msgSend(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint objc_msgSendArgument(nint receiver, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void objc_msgSendObserver(nint receiver, nint selector, nint observer, nint callback, nint name, nint obj);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void objc_msgSendTwoArguments(nint receiver, nint selector, nint first, nint second);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern int objc_msgSendInt(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nuint objc_msgSendCount(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint objc_msgSendIndex(nint receiver, nint selector, nuint index);
    [DllImport(ObjC)] internal static extern nint objc_autoreleasePoolPush();
    [DllImport(ObjC)] internal static extern void objc_autoreleasePoolPop(nint pool);
    internal static nint String(string value) => CFStringCreateWithCString(0, value, 0x08000100);
    internal static string ReadString(nint value)
    {
        if (value == 0) return "";
        byte[] bytes = new byte[2048];
        if (!CFStringGetCString(value, bytes, bytes.Length, 0x08000100)) return "";
        int end = Array.IndexOf(bytes, (byte)0); return System.Text.Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
    internal static (string Id, int Pid) Foreground()
    {
        // Probe children do not initialize Avalonia/Cocoa, so load the Objective-C classes explicitly.
        _ = AppKit.Value;
        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = objc_msgSend(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var app = objc_msgSend(workspace, sel_registerName("frontmostApplication"));
            return (ReadString(objc_msgSend(app, sel_registerName("bundleIdentifier"))), objc_msgSendInt(app, sel_registerName("processIdentifier")));
        }
        finally { objc_autoreleasePoolPop(pool); }
    }
}
