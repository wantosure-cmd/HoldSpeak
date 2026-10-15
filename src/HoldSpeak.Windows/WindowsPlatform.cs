using System.Diagnostics;
using System.Runtime.InteropServices;
using HoldSpeak.Core;

namespace HoldSpeak.Windows;

public sealed class WindowsPlatform : IDesktopPlatform
{
    private readonly ProcessProbe probe = new();
    private readonly Native.HookProc mouseCallback;
    private readonly Native.HookProc keyboardCallback;
    private readonly object keyGate = new();
    private Key[] owned = [];
    private RecoveryClient? recovery;
    private ListenerLease? listenerLease;
    private WindowsSessionMonitor? sessionMonitor;
    private Thread? thread;
    private uint threadId;
    private nint mouseHook, keyHook;
    private int leftDown, suppressUp;
    private int disposed;
    private long lastDown;
    private Native.Point lastPoint;
    private bool detectionEnabled = true;
    private HookWatchdog? watchdog;
    public void SetDetectionEnabled(bool enabled) => detectionEnabled = enabled;
    public bool IsMac => false;
    public bool PhysicalLeftDown => Volatile.Read(ref leftDown) == 1;
    public long ForegroundWindowId => Native.GetForegroundWindow().ToInt64();
    public string PermissionStatus => "普通权限运行；管理员应用需要另行验证";
    public ITargetProbe Probe => probe;
    public InputMethodInfo? CurrentInputMethod => WindowsInputMethods.Current();
    public InputMethodInfo[] ListInputMethods() => WindowsInputMethods.List();
    public (double X, double Y) PointerPosition
    {
        get { if (!Native.GetCursorPos(out var point)) throw new InvalidOperationException("无法获取鼠标位置。"); return (point.X, point.Y); }
    }
    public event Action<PointerEvent>? Pointer;
    public event Action<string>? Faulted;
    public event Action<string>? RecoveryRequested;
    public string ForegroundAppId => AppId(Native.GetForegroundWindow());
    public WindowsPlatform() { mouseCallback = MouseHook; keyboardCallback = KeyboardHook; }
    public static string AppId(nint hwnd)
    {
        if (hwnd == 0) return "";
        try { Native.GetWindowThreadProcessId(hwnd, out uint pid); using var p = Process.GetProcessById((int)pid); return p.ProcessName + ".exe"; }
        catch (ArgumentException) { return ""; } catch (InvalidOperationException) { return ""; } catch (System.ComponentModel.Win32Exception) { return ""; }
    }
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (thread != null) return;
        listenerLease = ListenerLease.Acquire();
        using var warmup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var recoveryStart = Task.Run(() => { recovery = new(); recovery.Failed += () => Faulted?.Invoke("按键恢复进程失联，已暂停；请重新启用工具"); });
        Task.WhenAll(recoveryStart, detectionEnabled ? probe.WarmupAsync(warmup.Token) : Task.CompletedTask).GetAwaiter().GetResult();
        Exception? error = null; using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = Native.GetCurrentThreadId(); Native.PeekMessage(out _, 0, 0, 0, 0);
            mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
            keyHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
            if (mouseHook == 0 || keyHook == 0) error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (error == null) while (Native.GetMessage(out var message, 0, 0, 0) > 0) { Native.TranslateMessage(message); Native.DispatchMessage(message); }
            if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
            if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
            ReleaseListenerLease();
        }) { IsBackground = true, Name = "HoldSpeak input hook" };
        thread.Start(); ready.Wait(); if (error != null) throw error;
        sessionMonitor = new(message => Faulted?.Invoke(message), message => RecoveryRequested?.Invoke(message));
        watchdog = new(() =>
        {
            Faulted?.Invoke("鼠标监听无响应，正在自动重建");
            RecoveryRequested?.Invoke("鼠标监听需要重建");
        });
    }
    private nint MouseHook(int code, nuint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(mouseHook, code, message, data);
        var e = Marshal.PtrToStructure<Native.Mouse>(data);
        if ((e.Flags & 1) != 0 && e.Extra == HookWatchdog.Marker)
        {
            watchdog?.Observe(); return 1;
        }
        if ((e.Flags & 1) != 0) return Native.CallNextHookEx(mouseHook, code, message, data);
        long now = Environment.TickCount64;
        if (message == 0x201)
        {
            Interlocked.Exchange(ref leftDown, 1);
            bool multi = now - lastDown <= Native.GetDoubleClickTime() && Math.Abs(e.Point.X - lastPoint.X) <= Native.GetSystemMetrics(36) && Math.Abs(e.Point.Y - lastPoint.Y) <= Native.GetSystemMetrics(37);
            lastDown = now; lastPoint = e.Point;
            double scale = Math.Max(96u, Native.GetDpiForWindow(Native.GetForegroundWindow())) / 96.0;
            Pointer?.Invoke(new(PointerKind.Down, e.Point.X, e.Point.Y, now, scale, multi));
        }
        else if (message == 0x202)
        {
            Interlocked.Exchange(ref leftDown, 0); Pointer?.Invoke(new(PointerKind.Up, e.Point.X, e.Point.Y, now));
            if (Interlocked.Exchange(ref suppressUp, 0) == 1) return 1;
        }
        else if (message == 0x200 && PhysicalLeftDown) Pointer?.Invoke(new(PointerKind.Move, e.Point.X, e.Point.Y, now));
        else if (message is 0x204 or 0x207 or 0x20B or 0x20A && PhysicalLeftDown) Pointer?.Invoke(new(PointerKind.Cancel, 0, 0, now));
        return Native.CallNextHookEx(mouseHook, code, message, data);
    }
    private nint KeyboardHook(int code, nuint message, nint data)
    {
        if (code >= 0 && message is 0x100 or 0x104)
        {
            var e = Marshal.PtrToStructure<Native.Keyboard>(data);
            if ((e.Flags & 0x10) == 0 && PhysicalLeftDown) Pointer?.Invoke(new(PointerKind.Cancel, 0, 0, Environment.TickCount64));
        }
        return Native.CallNextHookEx(keyHook, code, message, data);
    }
    public IInputSession Begin(VoiceProfile profile, bool releaseMouse)
    {
        lock (keyGate)
        {
            if (!PhysicalLeftDown) throw new InvalidOperationException("鼠标已经松开。");
            if (profile.WindowsKeys.Length == 0) throw new InvalidOperationException("请先配置 Windows 快捷键。");
            CheckShortcut(profile.WindowsKeys, false);
            if (releaseMouse)
            {
                Interlocked.Exchange(ref suppressUp, 1);
                try { Send([new() { Type = 0, Union = new() { Mouse = new() { Flags = 4, Extra = Native.Marker } } }]); }
                catch { Interlocked.Exchange(ref suppressUp, 0); throw; }
            }
            if (!PhysicalLeftDown) throw new InvalidOperationException("鼠标已经松开。");
            var keys = profile.Keys(false); recovery?.Arm(keys); owned = keys;
            try { Send(keys.Select(k => KeyInput(k, false)).ToArray()); }
            catch { EmergencyRelease(); throw; }
            if (profile.Mode != ActivationMode.Hold) ReleaseOwned();
            return new Session(this, profile);
        }
    }
    private sealed class Session(WindowsPlatform owner, VoiceProfile profile) : IInputSession
    {
        private bool ended;
        public void End()
        {
            if (ended) return; ended = true;
            lock (owner.keyGate)
            {
                if (profile.Mode == ActivationMode.Hold) owner.ReleaseOwned();
                else
                {
                    var keys = profile.Mode == ActivationMode.Toggle ? profile.WindowsKeys : profile.WindowsStopKeys;
                    CheckShortcut(keys, true);
                    owner.recovery?.Arm(keys);
                    owner.owned = keys;
                    try { Send(keys.Select(k => KeyInput(k, false)).ToArray()); } finally { owner.ReleaseOwned(); }
                }
            }
        }
        public void Dispose() => End();
    }
    private void ReleaseOwned()
    {
        if (owned.Length == 0) return;
        Send(owned.Reverse().Select(k => KeyInput(k, true)).ToArray()); owned = []; recovery?.Clear();
    }
    private static void CheckShortcut(Key[] keys, bool ending) => ShortcutGuard.EnsureAvailable(keys,
        new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => Native.GetAsyncKeyState(k) < 0),
        key => Native.GetAsyncKeyState(KeyInput(key, false).Union.Keyboard.Key) < 0, ending);
    public void EmergencyRelease() { lock (keyGate) { try { ReleaseOwned(); } catch (System.ComponentModel.Win32Exception) { } } }
    public static void RecoverKeys(Key[] keys) => Send(keys.Reverse().Select(k => KeyInput(k, true)).ToArray());
    private static Native.Input KeyInput(Key key, bool up)
    {
        if (!KeyCatalog.TryWindows(key, out ushort vk, out bool extended)) throw new InvalidOperationException("不支持的 Windows 按键。");
        uint flags = up ? 2u : 0u;
        if (extended) flags |= 1;
        return new() { Type = 1, Union = new() { Keyboard = new() { Key = vk, Flags = flags, Extra = Native.Marker } } };
    }
    private static void Send(Native.Input[] inputs)
    {
        if (Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>()) != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "按键注入失败，可能存在权限限制。");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        watchdog?.Dispose();
        sessionMonitor?.Dispose();
        try { EmergencyRelease(); }
        finally
        {
            if (thread != null) { Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(1000); }
            // A slow listener keeps the lease until its own thread has unhooked.
            if (thread == null || !thread.IsAlive) ReleaseListenerLease();
            try { probe.Dispose(); } finally { recovery?.Dispose(); }
        }
    }
    private void ReleaseListenerLease() => Interlocked.Exchange(ref listenerLease, null)?.Dispose();
}
