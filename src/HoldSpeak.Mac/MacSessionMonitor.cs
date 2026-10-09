using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace HoldSpeak.Mac;

internal sealed class MacSessionMonitor : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NotificationCallback(nint receiver, nint selector, nint notification);
    private static readonly NotificationCallback Callback = OnNotification;
    private static readonly ConcurrentDictionary<nint, Action<string>> Observers = new();
    private static readonly ConcurrentDictionary<nint, Action<string>> Resumers = new();
    private static readonly Lazy<nint> ObserverClass = new(CreateObserverClass);
    private nint observer, center;
    internal MacSessionMonitor(Action<string> interrupt, Action<string>? resume = null)
    {
        var pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            // Loading an AppKit constant also ensures NSWorkspace is registered.
            var sleep = MacNative.AppKitConstant("NSWorkspaceWillSleepNotification");
            var inactive = MacNative.AppKitConstant("NSWorkspaceSessionDidResignActiveNotification");
            var workspace = MacNative.objc_msgSend(MacNative.objc_getClass("NSWorkspace"), MacNative.sel_registerName("sharedWorkspace"));
            center = MacNative.objc_msgSend(workspace, MacNative.sel_registerName("notificationCenter"));
            observer = MacNative.objc_msgSend(MacNative.objc_msgSend(ObserverClass.Value, MacNative.sel_registerName("alloc")), MacNative.sel_registerName("init"));
            if (center == 0 || observer == 0) throw new InvalidOperationException("无法注册系统会话通知。");
            Observers[observer] = interrupt;
            if (resume != null) Resumers[observer] = resume;
            var register = MacNative.sel_registerName("addObserver:selector:name:object:");
            var callback = MacNative.sel_registerName("holdSpeakSessionChanged:");
            MacNative.objc_msgSendObserver(center, register, observer, callback, sleep, 0);
            MacNative.objc_msgSendObserver(center, register, observer, callback, inactive, 0);
            MacNative.objc_msgSendObserver(center, register, observer, callback, MacNative.AppKitConstant("NSWorkspaceDidWakeNotification"), 0);
            MacNative.objc_msgSendObserver(center, register, observer, callback, MacNative.AppKitConstant("NSWorkspaceSessionDidBecomeActiveNotification"), 0);
        }
        catch { Dispose(); throw; }
        finally { MacNative.objc_autoreleasePoolPop(pool); }
    }
    private static nint CreateObserverClass()
    {
        var cls = MacNative.objc_allocateClassPair(MacNative.objc_getClass("NSObject"), "HoldSpeakSessionObserver", 0);
        if (cls == 0 || !MacNative.class_addMethod(cls, MacNative.sel_registerName("holdSpeakSessionChanged:"), Marshal.GetFunctionPointerForDelegate(Callback), "v@:@"))
            throw new InvalidOperationException("无法创建系统通知监听器。");
        MacNative.objc_registerClassPair(cls); return cls;
    }
    private static void OnNotification(nint receiver, nint selector, nint notification)
    {
        // Exceptions must never cross an Objective-C callback boundary.
        try
        {
            if (!Observers.TryGetValue(receiver, out var interrupt)) return;
            string name = MacNative.ReadString(MacNative.objc_msgSend(notification, MacNative.sel_registerName("name")));
            if (name is "NSWorkspaceDidWakeNotification" or "NSWorkspaceSessionDidBecomeActiveNotification")
            { if (Resumers.TryGetValue(receiver, out var resume)) resume("用户会话已返回或系统已唤醒"); }
            else interrupt(name == "NSWorkspaceWillSleepNotification" ? "系统即将休眠，已暂停；唤醒后将自动恢复" : "用户会话已切出，已暂停；返回后将自动恢复");
        }
        catch { }
    }
    public void Dispose()
    {
        var value = Interlocked.Exchange(ref observer, 0); if (value == 0) return;
        Observers.TryRemove(value, out _);
        Resumers.TryRemove(value, out _);
        if (center != 0) MacNative.objc_msgSendArgument(center, MacNative.sel_registerName("removeObserver:"), value);
        MacNative.objc_msgSend(value, MacNative.sel_registerName("release"));
    }
}
