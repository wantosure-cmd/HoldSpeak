using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using HoldSpeak.Core;
using HoldSpeak.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.IO;

namespace HoldSpeak.Windows.Tests;

internal static class Program
{
    private static int failures;
    private static int recoveredUps;
    private static readonly HookProc recoveryHook = OnKeyboard;
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application();
        if (args.Contains("--recovery-only"))
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                try { await CheckRecovery(); await CheckSessionNotifications(); await CheckHookWatchdog(); }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + ex); }
                finally { app.Shutdown(); }
            }));
            app.Run(); Console.WriteLine($"Background recovery failures: {failures}"); return failures == 0 ? 0 : 1;
        }
        var edit = new TextBox { Text = "editable fixture", Margin = new Thickness(10), Height = 50 };
        var readOnly = new TextBox { Text = "read only", IsReadOnly = true, Margin = new Thickness(10), Height = 50 };
        var password = new PasswordBox { Margin = new Thickness(10), Height = 50 };
        var rich = new RichTextBox(new FlowDocument(new Paragraph(new Run("editable rich text fixture")))) { Margin = new Thickness(10), Height = 70 };
        var richReadOnly = new RichTextBox(new FlowDocument(new Paragraph(new Run("read only rich text fixture")))) { IsReadOnly = true, Margin = new Thickness(10), Height = 70 };
        var button = new Button { Content = "Non-input fixture", Margin = new Thickness(10), Height = 30 };
        var panel = new StackPanel(); panel.Children.Add(edit); panel.Children.Add(readOnly); panel.Children.Add(password); panel.Children.Add(rich); panel.Children.Add(richReadOnly);
        panel.Children.Add(button);
        var window = new Window { Title = "HoldSpeak integration fixture", Width = 460, Height = 540, Content = panel, Topmost = true };
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(150); window.Activate();
                await Check(edit, Editability.Editable, "editable textbox");
                await Check(readOnly, Editability.NotEditable, "read-only textbox");
                await Check(password, Editability.NotEditable, "password exclusion");
                await Check(rich, Editability.Editable, "editable Document rich text");
                await Check(richReadOnly, Editability.NotEditable, "read-only Document rich text");
                await Check(button, Editability.NotEditable, "explicit non-input button");
                FocusFixture(edit); await Task.Delay(80);
                var hoverPoint = rich.PointToScreen(new Point(30, 20));
                var hover = await Task.Run(() => WindowsProbe.Inspect(hoverPoint.X, hoverPoint.Y, false));
                var strict = await Task.Run(() => WindowsProbe.Inspect(hoverPoint.X, hoverPoint.Y));
                Assert(hover.Editability == Editability.Editable && strict.Editability != Editability.Editable, "hover detects unfocused editor while activation still requires focus");
                var passwordPoint = password.PointToScreen(new Point(30, 20));
                var secret = await Task.Run(() => WindowsProbe.Inspect(passwordPoint.X, passwordPoint.Y, false));
                Assert(secret.Editability == Editability.NotEditable, "hover excludes password field");
                FocusFixture(edit); await Task.Delay(80);
                var editPoint = edit.PointToScreen(new Point(30, 20));
                var initialTarget = await Task.Run(() => WindowsProbe.Inspect(editPoint.X, editPoint.Y));
                edit.Margin = new Thickness(120, 10, 10, 10); window.UpdateLayout(); await Task.Delay(80);
                var oldPoint = await Task.Run(() => WindowsProbe.Inspect(editPoint.X, editPoint.Y));
                var retained = await Task.Run(() => WindowsProbe.Inspect(editPoint.X, editPoint.Y, true, true));
                Assert(oldPoint.Editability != Editability.Editable && retained.Editability == Editability.Editable && retained.ControlId == initialTarget.ControlId && retained.WindowId == initialTarget.WindowId, "active focus survives editor movement beyond original pointer position");
                FocusFixture(password); await Task.Delay(80);
                var activeSecret = await Task.Run(() => WindowsProbe.Inspect(-9999, -9999, true, true));
                Assert(activeSecret.Editability == Editability.NotEditable && activeSecret.Reason == "password_field", "active focus still excludes password");
                FocusFixture(readOnly); await Task.Delay(80);
                var activeReadOnly = await Task.Run(() => WindowsProbe.Inspect(-9999, -9999, true, true));
                Assert(activeReadOnly.Editability == Editability.NotEditable, "active focus still excludes read-only field");
                using var platform = new WindowsPlatform();
                bool rejected = false;
                try { platform.Begin(new(), false); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected, "no physical hold cannot inject");
                await CheckRecovery();
                await CheckSessionNotifications();
            }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + ex); }
            finally { window.Close(); app.Shutdown(); }
        };
        app.Run(window); Console.WriteLine($"Windows integration failures: {failures}"); return failures == 0 ? 0 : 1;
    }
    private static async Task Check(Control control, Editability expected, string name)
    {
        FocusFixture(control); await Task.Delay(80);
        var point = control.PointToScreen(new Point(30, 20));
        Console.WriteLine($"fixture PID={Environment.ProcessId}, control={control.GetType().Name}, keyboardFocus={control.IsKeyboardFocused}, point={point}");
        var result = await Task.Run(() => WindowsProbe.Inspect(point.X, point.Y));
        Console.WriteLine($"probe app={result.AppId}, window={result.WindowId}, reason={result.Reason}");
        Assert(result.Editability == expected, $"{name}: {result.Editability}/{result.Reason}");
    }
    private static void Assert(bool yes, string name) { if (!yes) { failures++; Console.WriteLine("FAIL " + name); } else Console.WriteLine("PASS " + name); }
    private static void FocusFixture(Control control)
    {
        var fixture = Window.GetWindow(control)!;
        var hwnd = new WindowInteropHelper(fixture).Handle;
        uint own = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = foreground != 0 && foreground != own && AttachThreadInput(own, foreground, true);
        try { SetForegroundWindow(hwnd); fixture.Activate(); control.Focus(); }
        finally { if (attached) AttachThreadInput(own, foreground, false); }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint current, uint target, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    private static async Task CheckSessionNotifications()
    {
        int notifications = 0, resumes = 0;
        using var monitor = new WindowsSessionMonitor(_ => Interlocked.Increment(ref notifications), _ => Interlocked.Increment(ref resumes));
        nint receiver = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pid != Environment.ProcessId) return true;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (!name.ToString().StartsWith(".NET-BroadcastEventWindow.", StringComparison.Ordinal)) return true;
            receiver = window; return false;
        }, 0);
        Assert(receiver != 0, "owned SystemEvents receiver exists"); if (receiver == 0) return;
        // Only this test's hidden window receives simulated messages. The OS is not locked/suspended.
        Assert(PostMessage(receiver, 0x218, 4, 0), "post simulated suspend to owned receiver");
        await WaitNotifications(1);
        Assert(notifications == 1, "suspend notification reaches monitor");
        Assert(PostMessage(receiver, 0x2B1, 7, Process.GetCurrentProcess().SessionId), "post simulated session lock to owned receiver");
        await WaitNotifications(2);
        Assert(notifications == 2, "session lock notification reaches monitor");
        PostMessage(receiver, 0x218, 7, 0); PostMessage(receiver, 0x2B1, 8, Process.GetCurrentProcess().SessionId);
        var resumeDeadline = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref resumes) < 2 && DateTime.UtcNow < resumeDeadline) await Task.Delay(10);
        Assert(notifications == 2 && resumes == 2, "resume/unlock request recovery without counting as new faults");
        monitor.Dispose(); PostMessage(receiver, 0x2B1, 7, Process.GetCurrentProcess().SessionId);
        await Task.Delay(100); Assert(notifications == 2, "disposed monitor is unsubscribed");
        PostMessage(receiver, 0x2B1, 8, Process.GetCurrentProcess().SessionId); await Task.Delay(100);
        Assert(resumes == 2, "disposed monitor does not request recovery");
        async Task WaitNotifications(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (Volatile.Read(ref notifications) < count && DateTime.UtcNow < deadline) await Task.Delay(10);
        }
    }
    private static async Task CheckHookWatchdog()
    {
        int failuresObserved = 0;
        using var watchdog = new HookWatchdog(() => failuresObserved++, false);
        HookProc callback = (code, message, data) =>
        {
            if (code >= 0)
            {
                var e = Marshal.PtrToStructure<Native.Mouse>(data);
                if ((e.Flags & 1) != 0 && e.Extra == HookWatchdog.Marker) { watchdog.Observe(); return 1; }
            }
            return CallNextHookEx(0, code, message, data);
        };
        var hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            await watchdog.CheckOnceAsync(); await watchdog.CheckOnceAsync();
            Assert(failuresObserved == 0, "healthy mouse hook receives consumed zero-motion markers");
            UnhookWindowsHookEx(hook); hook = 0;
            await watchdog.CheckOnceAsync(); Assert(failuresObserved == 0, "one missed heartbeat does not request recovery");
            await watchdog.CheckOnceAsync(); Assert(failuresObserved == 1, "removed own mouse hook requests recovery after two misses");
            await watchdog.CheckOnceAsync(); Assert(failuresObserved == 1, "lost-hook recovery notification is emitted only once");
        }
        finally { if (hook != 0) UnhookWindowsHookEx(hook); GC.KeepAlive(callback); }
    }
    private delegate bool EnumWindowCallback(nint window, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint param, nint data);
    private static async Task CheckRecovery()
    {
        string exe = Path.GetFullPath("src/HoldSpeak.App/bin/Debug/net10.0-windows/HoldSpeak.App.exe");
        if (!File.Exists(exe)) { Assert(false, "build application before recovery integration test"); return; }
        // Empty journals measure warm guardian round trips without injecting any keys.
        using (var child = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, ArgumentList = { "--recovery-stdio" } })!)
        {
            var samples = new List<double>();
            for (int i = 0; i < 21; i++)
            {
                var watch = Stopwatch.StartNew();
                await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new RecoveryMessage([]))); await child.StandardInput.FlushAsync();
                Assert(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "ok", "empty guardian round trip acknowledged");
                if (i > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            Console.WriteLine($"Guardian warm RTT: median={samples[10]:F2} ms, p95={samples[18]:F2} ms, max={samples[19]:F2} ms");
            child.StandardInput.Close(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        var hook = SetWindowsHookEx(13, recoveryHook, GetModuleHandle(null), 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            foreach (bool closeInput in new[] { true, false })
            {
                int before = recoveredUps;
                using var child = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, ArgumentList = { "--recovery-stdio" } })!;
                await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new RecoveryMessage([HoldSpeak.Core.Key.F9]))); await child.StandardInput.FlushAsync();
                Assert(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "ok", "recovery lease acknowledged");
                if (closeInput) child.StandardInput.Close();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); await Task.Delay(100);
                Assert(recoveredUps > before, closeInput ? "pipe closed releases registered F9 (key-up only)" : "expired heartbeat releases registered F9 (key-up only)");
            }
        }
        finally { UnhookWindowsHookEx(hook); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardEvent { public uint Key, Scan, Flags, Time; public nuint Extra; }
    private delegate nint HookProc(int code, nuint message, nint data);
    private static nint OnKeyboard(int code, nuint message, nint data)
    {
        if (code >= 0 && message == 0x101)
        {
            var e = Marshal.PtrToStructure<KeyboardEvent>(data);
            if (e.Key == 0x78 && e.Extra == 0x484F4C44) recoveredUps++;
        }
        return CallNextHookEx(0, code, message, data);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
