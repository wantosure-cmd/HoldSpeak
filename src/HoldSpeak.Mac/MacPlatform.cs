using HoldSpeak.Core;

namespace HoldSpeak.Mac;

public sealed class MacPlatform : IDesktopPlatform
{
    private readonly ProcessProbe probe = new();
    private readonly MacNative.TapCallback callback;
    private readonly object gate = new();
    private Key[] owned = [];
    private RecoveryClient? recovery;
    private ListenerLease? listenerLease;
    private MacSessionMonitor? sessionMonitor;
    private Thread? thread;
    private nint tap, runLoop;
    private int leftDown, suppressUp;
    private int disposed;
    private bool detectionEnabled = true;
    public void SetDetectionEnabled(bool enabled) => detectionEnabled = enabled;
    public bool IsMac => true;
    public bool PhysicalLeftDown => Volatile.Read(ref leftDown) == 1;
    public string ForegroundAppId => MacNative.Foreground().Id;
    public long ForegroundWindowId => MacNative.Foreground().Pid;
    public string PermissionStatus => MacNative.AXIsProcessTrusted() && MacNative.CGPreflightListenEventAccess() ? "辅助功能和输入监控权限已就绪" : "请授予辅助功能及输入监控权限，然后重新启动";
    public ITargetProbe Probe => probe;
    public InputMethodInfo? CurrentInputMethod => MacInputMethods.Current();
    public InputMethodInfo[] ListInputMethods() => MacInputMethods.List();
    public (double X, double Y) PointerPosition
    {
        get
        {
            var e = MacNative.CGEventCreate(0); if (e == 0) throw new InvalidOperationException("无法获取鼠标位置。");
            try { var p = MacNative.CGEventGetLocation(e); return (p.X, p.Y); } finally { MacNative.CFRelease(e); }
        }
    }
    public event Action<PointerEvent>? Pointer;
    public event Action<string>? Faulted;
    public event Action<string>? RecoveryRequested;
    public MacPlatform() => callback = EventTap;
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (thread != null) return;
        if (!MacNative.AXIsProcessTrusted() || !MacNative.CGPreflightListenEventAccess()) throw new InvalidOperationException(PermissionStatus);
        listenerLease = ListenerLease.Acquire();
        using var warmup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var recoveryStart = Task.Run(() => { recovery = new(); recovery.Failed += () => Faulted?.Invoke("按键恢复进程失联，已暂停；请重新启用工具"); });
        Task.WhenAll(recoveryStart, detectionEnabled ? probe.WarmupAsync(warmup.Token) : Task.CompletedTask).GetAwaiter().GetResult();
        Exception? error = null; using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            ulong mask = (1UL << 1) | (1UL << 2) | (1UL << 5) | (1UL << 6) | (1UL << 10) | (1UL << 12) | (1UL << 3) | (1UL << 25);
            tap = MacNative.CGEventTapCreate(1, 0, 0, mask, callback, 0);
            if (tap == 0) { error = new InvalidOperationException("无法创建输入事件监听，请检查系统权限。"); ReleaseListenerLease(); ready.Set(); return; }
            runLoop = MacNative.CFRunLoopGetCurrent();
            var source = MacNative.CFMachPortCreateRunLoopSource(0, tap, 0);
            var mode = MacNative.String("kCFRunLoopDefaultMode");
            MacNative.CFRunLoopAddSource(runLoop, source, mode); MacNative.CFRelease(mode);
            MacNative.CGEventTapEnable(tap, true); ready.Set(); MacNative.CFRunLoopRun();
            MacNative.CFRelease(source); MacNative.CFRelease(tap); tap = 0;
            ReleaseListenerLease();
        }) { IsBackground = true, Name = "HoldSpeak event tap" };
        thread.Start(); ready.Wait(); if (error != null) throw error;
        sessionMonitor = new(message => Faulted?.Invoke(message), message => RecoveryRequested?.Invoke(message));
    }
    private nint EventTap(nint proxy, uint type, nint e, nint context)
    {
        if (type is 0xFFFFFFFE or 0xFFFFFFFF)
        {
            Faulted?.Invoke("输入监听被系统停用，正在自动重建");
            if (tap != 0) MacNative.CGEventTapEnable(tap, true);
            RecoveryRequested?.Invoke("输入监听需要恢复"); return e;
        }
        if (e == 0 || MacNative.CGEventGetIntegerValueField(e, 42) == MacNative.Marker) return e;
        var point = MacNative.CGEventGetLocation(e); long now = Environment.TickCount64;
        if (type == 1)
        {
            Interlocked.Exchange(ref leftDown, 1);
            Pointer?.Invoke(new(PointerKind.Down, point.X, point.Y, now, 1, MacNative.CGEventGetIntegerValueField(e, 1) > 1));
        }
        else if (type == 2)
        {
            Interlocked.Exchange(ref leftDown, 0); Pointer?.Invoke(new(PointerKind.Up, point.X, point.Y, now));
            if (Interlocked.Exchange(ref suppressUp, 0) == 1) return 0;
        }
        else if (type is 5 or 6 && PhysicalLeftDown)
        {
            Pointer?.Invoke(new(PointerKind.Move, point.X, point.Y, now));
            if (type == 6 && Volatile.Read(ref suppressUp) == 1) MacNative.CGEventSetType(e, 5);
        }
        else if (type is 3 or 10 or 12 or 25 && PhysicalLeftDown) Pointer?.Invoke(new(PointerKind.Cancel, 0, 0, now));
        return e;
    }
    public IInputSession Begin(VoiceProfile profile, bool releaseMouse)
    {
        lock (gate)
        {
            if (!PhysicalLeftDown) throw new InvalidOperationException("鼠标已经松开。");
            if (profile.MacKeys.Length == 0) throw new InvalidOperationException("请先配置 Mac 快捷键。");
            CheckShortcut(profile.MacKeys, false);
            if (releaseMouse)
            {
                var current = MacNative.CGEventCreate(0); var point = MacNative.CGEventGetLocation(current); MacNative.CFRelease(current);
                var up = MacNative.CGEventCreateMouseEvent(0, 2, point, 0);
                if (up == 0) throw new InvalidOperationException("无法创建鼠标事件。");
                Interlocked.Exchange(ref suppressUp, 1); Post(up);
            }
            if (!PhysicalLeftDown) throw new InvalidOperationException("鼠标已经松开。");
            recovery?.Arm(profile.MacKeys); owned = profile.MacKeys;
            try { SendKeys(owned, true); if (profile.Mode != ActivationMode.Hold) ReleaseOwned(); }
            catch { EmergencyRelease(); throw; }
            return new Session(this, profile);
        }
    }
    private sealed class Session(MacPlatform owner, VoiceProfile profile) : IInputSession
    {
        private bool ended;
        public void End()
        {
            if (ended) return; ended = true;
            lock (owner.gate)
            {
                if (profile.Mode == ActivationMode.Hold) owner.ReleaseOwned();
                else
                {
                    var keys = profile.Mode == ActivationMode.Toggle ? profile.MacKeys : profile.MacStopKeys;
                    CheckShortcut(keys, true);
                    owner.recovery?.Arm(keys); owner.owned = keys;
                    try { SendKeys(owner.owned, true); } finally { owner.ReleaseOwned(); }
                }
            }
        }
        public void Dispose() => End();
    }
    private void ReleaseOwned() { if (owned.Length > 0) { SendKeys(owned, false); owned = []; recovery?.Clear(); } }
    private static void CheckShortcut(Key[] keys, bool ending) => ShortcutGuard.EnsureAvailable(keys,
        (MacNative.CGEventSourceFlagsState(1) & ((1UL << 17) | (1UL << 18) | (1UL << 19) | (1UL << 20))) != 0,
        key => MacNative.CGEventSourceKeyState(1, KeyInfo(key).Code), ending);
    public void EmergencyRelease() { lock (gate) { try { ReleaseOwned(); } catch (InvalidOperationException) { } } }
    public static void RecoverKeys(Key[] keys) => SendKeys(keys, false);
    private static void SendKeys(Key[] keys, bool down)
    {
        foreach (var stroke in MacKeySequence.Build(keys, down))
        {
            var e = MacNative.CGEventCreateKeyboardEvent(0, stroke.Code, stroke.Down);
            if (e == 0) throw new InvalidOperationException("无法创建键盘事件。");
            if (stroke.IsModifier) MacNative.CGEventSetType(e, 12);
            MacNative.CGEventSetFlags(e, stroke.Flags); Post(e);
        }
    }
    private static (ushort Code, ulong Flag) KeyInfo(Key key)
    {
        if (!KeyCatalog.TryMac(key, out ushort code, out ulong flag)) throw new InvalidOperationException("不支持的 macOS 按键。");
        return (code, flag);
    }
    private static void Post(nint e) { MacNative.CGEventSetIntegerValueField(e, 42, MacNative.Marker); MacNative.CGEventPost(1, e); MacNative.CFRelease(e); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        sessionMonitor?.Dispose();
        try { EmergencyRelease(); }
        finally
        {
            if (runLoop != 0) MacNative.CFRunLoopStop(runLoop); thread?.Join(1000);
            if (thread == null || !thread.IsAlive) ReleaseListenerLease();
            try { probe.Dispose(); } finally { recovery?.Dispose(); }
        }
    }
    private void ReleaseListenerLease() => Interlocked.Exchange(ref listenerLease, null)?.Dispose();
}
