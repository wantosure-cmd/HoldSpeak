using HoldSpeak.Core;
using HoldSpeak.Mac;

int failures = 0;
CheckManagedSequences();
if (args.Contains("--managed-only"))
{
    Console.WriteLine($"Mac managed sequence failures: {failures}; no native frameworks loaded");
    return failures == 0 ? 0 : 1;
}
// Real macOS framework test, not a mock. No keyboard event is posted and no OS sleep/lock occurs.
if (!OperatingSystem.IsMacOS()) { Console.Error.WriteLine("Run native tests on macOS, or use --managed-only for event planning."); return 2; }
var pool = MacNative.objc_autoreleasePoolPush();
try
{
    var messages = new List<string>();
    var resumes = new List<string>();
    using var monitor = new MacSessionMonitor(messages.Add, resumes.Add);
    var workspace = MacNative.objc_msgSend(MacNative.objc_getClass("NSWorkspace"), MacNative.sel_registerName("sharedWorkspace"));
    var center = MacNative.objc_msgSend(workspace, MacNative.sel_registerName("notificationCenter"));
    var post = MacNative.sel_registerName("postNotificationName:object:");
    MacNative.objc_msgSendTwoArguments(center, post, MacNative.AppKitConstant("NSWorkspaceWillSleepNotification"), workspace);
    Assert(messages.Count == 1 && messages[0].Contains("休眠"), "owned workspace sleep notification reaches observer");
    MacNative.objc_msgSendTwoArguments(center, post, MacNative.AppKitConstant("NSWorkspaceSessionDidResignActiveNotification"), workspace);
    Assert(messages.Count == 2 && messages[1].Contains("切出"), "owned workspace inactive notification reaches observer");
    MacNative.objc_msgSendTwoArguments(center, post, MacNative.AppKitConstant("NSWorkspaceDidWakeNotification"), workspace);
    MacNative.objc_msgSendTwoArguments(center, post, MacNative.AppKitConstant("NSWorkspaceSessionDidBecomeActiveNotification"), workspace);
    Assert(resumes.Count == 2 && messages.Count == 2, "owned wake/active notifications request recovery");
    monitor.Dispose();
    MacNative.objc_msgSendTwoArguments(center, post, MacNative.AppKitConstant("NSWorkspaceWillSleepNotification"), workspace);
    Assert(messages.Count == 2, "disposed observer is unsubscribed");
    foreach (var key in new[] { Key.Command, Key.Control, Key.Space, Key.A, Key.D1, Key.F20 })
    {
        if (!KeyCatalog.TryMac(key, out ushort code, out ulong flag)) throw new Exception("Missing native key mapping");
        var e = MacNative.CGEventCreateKeyboardEvent(0, code, true);
        Assert(e != 0, "construct keyboard event: " + key); if (e == 0) continue;
        try
        {
            MacNative.CGEventSetFlags(e, flag);
            Assert(MacNative.CGEventGetIntegerValueField(e, 9) == code && MacNative.CGEventGetFlags(e) == flag, "native keyboard fields: " + key);
        }
        finally { MacNative.CFRelease(e); }
    }
    foreach (bool down in new[] { true, false })
    foreach (var stroke in MacKeySequence.Build([Key.Control, Key.Command, Key.RightCommand, Key.A], down))
    {
        var e = MacNative.CGEventCreateKeyboardEvent(0, stroke.Code, stroke.Down);
        Assert(e != 0, "construct planned native event: " + stroke.Key); if (e == 0) continue;
        try
        {
            if (stroke.IsModifier) MacNative.CGEventSetType(e, 12);
            MacNative.CGEventSetFlags(e, stroke.Flags);
            Assert(MacNative.CGEventGetIntegerValueField(e, 9) == stroke.Code && MacNative.CGEventGetFlags(e) == stroke.Flags, "planned native code and remaining modifiers: " + stroke.Key);
        }
        finally { MacNative.CFRelease(e); }
    }
    var system = MacNative.AXUIElementCreateSystemWide();
    Assert(system != 0, "create native accessibility root"); if (system != 0) MacNative.CFRelease(system);
    var applications = MacApplications.List();
    Assert(applications.All(a => !string.IsNullOrWhiteSpace(a.AppId) && !string.IsNullOrWhiteSpace(a.Name)) && applications.Select(a => a.AppId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == applications.Length, "native running applications have unique nonempty bundle IDs and names");
    Console.WriteLine($"Permission observation only: accessibility={MacNative.AXIsProcessTrusted()}, inputMonitoring={MacNative.CGPreflightListenEventAccess()}");
    Console.WriteLine($"Mac native failures: {failures}; no event was posted to the OS input stream");
}
catch (Exception ex) { failures++; Console.WriteLine("FAIL " + ex); }
finally { MacNative.objc_autoreleasePoolPop(pool); }
return failures == 0 ? 0 : 1;
void Assert(bool yes, string name) { if (!yes) failures++; Console.WriteLine((yes ? "PASS " : "FAIL ") + name); }
void CheckManagedSequences()
{
    const ulong command = 1UL << 20, control = 1UL << 18;
    var held = MacKeySequence.Build([Key.Control, Key.Command, Key.RightCommand, Key.A], true);
    Assert(held.Select(e => e.Flags).SequenceEqual(new[] { control, control | command, control | command, control | command }) && held.All(e => e.Down), "down events accumulate modifier state");
    var released = MacKeySequence.Build([Key.Control, Key.Command, Key.RightCommand, Key.A], false);
    Assert(released.Select(e => e.Key).SequenceEqual(new[] { Key.A, Key.RightCommand, Key.Command, Key.Control }) && released.All(e => !e.Down), "release reverses key order");
    Assert(released.Select(e => e.Flags).SequenceEqual(new[] { control | command, control | command, control, 0UL }), "one Command release retains the other Command mask");
    Assert(held.Count(e => e.IsModifier) == 3 && !held[^1].IsModifier && released[^1].Flags == 0, "ordinary keys keep modifiers and final release clears owned flags");
    foreach (var key in KeyCatalog.ForPlatform(true))
    {
        KeyCatalog.TryMac(key, out ushort code, out ulong flag);
        var down = MacKeySequence.Build([key], true).Single(); var up = MacKeySequence.Build([key], false).Single();
        Assert(down.Code == code && down.Flags == flag && up.Code == code && up.Flags == 0, "single-key plan: " + key);
    }
    Assert(MacKeySequence.Build([], false).Length == 0, "empty recovery journal produces no events");
    Reject([Key.Command, Key.F24]); Reject([Key.Command, Key.Command]);
    Reject([Key.A, Key.B, Key.C, Key.D, Key.E, Key.F]);
    void Reject(Key[] keys)
    {
        bool rejected = false;
        try { MacKeySequence.Build(keys, true); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "invalid full sequence rejected before posting: " + string.Join(',', keys));
    }
}
