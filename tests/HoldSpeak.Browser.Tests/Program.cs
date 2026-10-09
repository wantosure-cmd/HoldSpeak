using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using HoldSpeak.Core;
using HoldSpeak.Windows;

// Uses a dedicated browser profile and our own local fixture; never inspects user tabs.
string browser = args.FirstOrDefault() ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe";
if (!File.Exists(browser)) { Console.Error.WriteLine("Browser executable missing; pass its absolute path."); return 2; }
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string directory = Path.Combine(root, "artifacts", "browser-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string fixture = Path.Combine(directory, "fixture.html");
File.WriteAllText(fixture, """
<!doctype html><meta charset="utf-8"><title>HoldSpeak browser fixture</title>
<style>body{font:18px sans-serif;margin:24px}input,textarea,div[contenteditable],button{display:block;margin:12px 0;width:500px;min-height:38px;border:1px solid #888}textarea{height:60px}</style>
<input aria-label="HoldSpeak input" value="fixture">
<textarea aria-label="HoldSpeak textarea">fixture</textarea>
<div role="textbox" aria-label="HoldSpeak rich editor" contenteditable="true"><b>Rich</b> text fixture</div>
<input aria-label="HoldSpeak readonly" readonly value="fixture">
<input aria-label="HoldSpeak password" type="password" value="fixture">
<button aria-label="HoldSpeak button">Not an editor</button>
""");
var info = new ProcessStartInfo(browser) { UseShellExecute = false };
foreach (var argument in new[] { "--user-data-dir=" + Path.Combine(directory, "profile"), "--no-first-run", "--no-default-browser-check", "--window-size=920,800", new Uri(fixture).AbsoluteUri }) info.ArgumentList.Add(argument);
using var process = Process.Start(info) ?? throw new IOException("Cannot launch isolated browser");
int failures = 0;
var cases = new List<BrowserCase>();
try
{
    var deadline = DateTime.UtcNow.AddSeconds(15); nint handle;
    do { await Task.Delay(100); process.Refresh(); handle = process.MainWindowHandle; } while (handle == 0 && !process.HasExited && DateTime.UtcNow < deadline);
    if (handle == 0) throw new IOException("Isolated browser window not found");
    BrowserNative.ShowWindow(handle, 9); BrowserNative.SetForegroundWindow(handle);
    var window = AutomationElement.FromHandle(handle);
    await Task.Delay(1200);
    foreach (var (name, editable) in new[] { ("input", true), ("textarea", true), ("rich editor", true), ("readonly", false), ("password", false), ("button", false) })
    {
        var condition = new PropertyCondition(AutomationElement.NameProperty, "HoldSpeak " + name);
        AutomationElement? element = null;
        deadline = DateTime.UtcNow.AddSeconds(5);
        do { element = window.FindFirst(TreeScope.Descendants, condition); if (element == null) await Task.Delay(100); } while (element == null && DateTime.UtcNow < deadline);
        if (element == null) { failures++; cases.Add(new(name, false, Editability.Unknown, "", "fixture_missing")); Console.WriteLine("FAIL missing fixture " + name); continue; }
        BrowserNative.SetForegroundWindow(handle); element.SetFocus(); await Task.Delay(150);
        var rectangle = element.Current.BoundingRectangle;
        if (rectangle.IsEmpty) throw new IOException("Fixture has no bounds: " + name);
        var target = WindowsProbe.Inspect(rectangle.Left + Math.Min(15, rectangle.Width / 2), rectangle.Top + Math.Min(15, rectangle.Height / 2));
        bool passed = editable ? target.Editability == Editability.Editable : target.Editability != Editability.Editable;
        if (!passed) failures++;
        cases.Add(new(name, passed, target.Editability, target.ControlType, target.Reason));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {target.ControlType}/{target.Editability}/{target.Reason}");
    }
    var editors = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
    AutomationElement? addressBar = null;
    foreach (AutomationElement editor in editors)
    {
        string className = editor.Current.ClassName;
        if (className.Contains("Omnibox", StringComparison.OrdinalIgnoreCase)) { addressBar = editor; break; }
    }
    if (addressBar == null)
    {
        failures++; cases.Add(new("address bar", false, Editability.Unknown, "", "fixture_missing")); Console.WriteLine("FAIL address bar not identified by native Omnibox class");
        foreach (AutomationElement editor in editors) Console.WriteLine("fixture editor class: " + editor.Current.ClassName);
    }
    else
    {
        BrowserNative.SetForegroundWindow(handle); addressBar.SetFocus(); await Task.Delay(150);
        var rectangle = addressBar.Current.BoundingRectangle;
        var target = WindowsProbe.Inspect(rectangle.Left + 20, rectangle.Top + rectangle.Height / 2);
        if (target.Editability != Editability.Editable) failures++;
        cases.Add(new("address bar", target.Editability == Editability.Editable, target.Editability, target.ControlType, target.Reason));
        Console.WriteLine($"{(target.Editability == Editability.Editable ? "PASS" : "FAIL")} address bar: {target.ControlType}/{target.Editability}/{target.Reason}");
        if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) && raw is ValuePattern value)
        {
            var initial = target;
            var sessionPlatform = new SessionPlatform(initial.AppId);
            var engine = new GestureEngine(sessionPlatform, new());
            engine.Handle(new(PointerKind.Down, rectangle.Left + 20, rectangle.Top + rectangle.Height / 2, 0));
            engine.CompleteProbe(engine.Generation, initial); engine.Tick(600);
            engine.CompleteProbe(engine.Generation, initial); engine.Tick(620);
            long time = 1200;
            foreach (string text in new[] { "hold", "holdspeak fixture", "holdspeak fixture continued" })
            {
                BrowserNative.SetForegroundWindow(handle); addressBar.SetFocus();
                value.SetValue(text); await Task.Delay(650);
                BrowserNative.SetForegroundWindow(handle); addressBar.SetFocus(); await Task.Delay(100);
                var updated = WindowsProbe.Inspect(rectangle.Left + 20, rectangle.Top + rectangle.Height / 2);
                engine.Tick(time); engine.CompleteProbe(engine.Generation, updated); time += 600;
                bool passed = updated.Editability == Editability.Editable && updated.ControlId == initial.ControlId && updated.WindowId == initial.WindowId;
                if (!passed) failures++;
                cases.Add(new("address bar text update", passed, updated.Editability, updated.ControlType, updated.Reason));
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} address bar text update: {updated.ControlType}/{updated.Editability}/{updated.Reason}; sameControl={updated.ControlId == initial.ControlId}");
                if (!passed)
                {
                    foreach (var entry in new[] { ("focus", AutomationElement.FocusedElement), ("point", AutomationElement.FromPoint(new System.Windows.Point(rectangle.Left + 20, rectangle.Top + rectangle.Height / 2))) })
                    {
                        var node = entry.Item2;
                        for (int depth = 0; node != null && depth < 4; depth++, node = TreeWalker.ControlViewWalker.GetParent(node))
                            Console.WriteLine($"probe {entry.Item1}/{depth}: {node.Current.ControlType.ProgrammaticName}, class={node.Current.ClassName}, bounds={node.Current.BoundingRectangle}, id={string.Join('.', node.GetRuntimeId())}");
                    }
                }
            }
            bool held = engine.State == GestureState.Holding && sessionPlatform.Ends == 0;
            if (!held) failures++;
            cases.Add(new("address bar continuous session", held, target.Editability, target.ControlType, engine.State.ToString()));
            Console.WriteLine($"{(held ? "PASS" : "FAIL")} address bar remains Holding across text updates (shortcut session stub)");
            sessionPlatform.Left = false; engine.Handle(new(PointerKind.Up, 0, 0, time));
            if (sessionPlatform.Ends != 1) { failures++; Console.WriteLine("FAIL session did not end exactly once on release"); }
            var suggestion = WindowsProbe.Inspect(rectangle.Left + 20, rectangle.Bottom + 35);
            bool rejected = suggestion.Editability != Editability.Editable;
            if (!rejected) failures++;
            cases.Add(new("address suggestion area", rejected, suggestion.Editability, suggestion.ControlType, suggestion.Reason));
            Console.WriteLine($"{(rejected ? "PASS" : "FAIL")} address suggestion area is not an editor");
        }
    }
    var firstInput = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "HoldSpeak input"));
    var secondInput = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "HoldSpeak textarea"));
    if (firstInput != null && secondInput != null)
    {
        BrowserNative.SetForegroundWindow(handle); secondInput.SetFocus(); await Task.Delay(150);
        var rectangle = firstInput.Current.BoundingRectangle;
        var target = WindowsProbe.Inspect(rectangle.Left + 15, rectangle.Top + 15);
        bool passed = target.Editability != Editability.Editable;
        if (!passed) failures++;
        cases.Add(new("point and focus differ", passed, target.Editability, target.ControlType, target.Reason));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} point and focus differ: {target.Editability}/{target.Reason}");
    }
    else { failures++; cases.Add(new("point and focus differ", false, Editability.Unknown, "", "fixture_missing")); }
    File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new { Browser = Path.GetFileName(browser), Version = FileVersionInfo.GetVersionInfo(browser).ProductVersion, ForceAccessibility = false, VoiceShortcutSent = false, Cases = cases, Failures = failures }, ConfigurationStore.Json));
    Console.WriteLine($"Browser recognition failures: {failures}; no voice shortcut was sent");
    return failures == 0 ? 0 : 1;
}
finally
{
    // The unique profile guarantees this is the process launched by this test.
    if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(3000)) process.Kill(true); }
}
sealed record BrowserCase(string Name, bool Passed, Editability Editability, string ControlType, string Reason);
sealed class SessionPlatform(string app) : IDesktopPlatform
{
    internal bool Left = true;
    internal int Ends;
    public bool IsMac => false;
    public bool PhysicalLeftDown => Left;
    public string ForegroundAppId => app;
    public string PermissionStatus => "session stub";
    public (double X, double Y) PointerPosition => (0, 0);
    public event Action<PointerEvent>? Pointer { add { } remove { } }
    public event Action<string>? Faulted { add { } remove { } }
    public ITargetProbe Probe => throw new NotSupportedException();
    public void Start() { }
    public IInputSession Begin(VoiceProfile profile, bool releaseMouse) => new Session(this);
    public void EmergencyRelease() { }
    public void Dispose() { }
    private sealed class Session(SessionPlatform owner) : IInputSession
    {
        private bool ended;
        public void End() { if (ended) return; ended = true; owner.Ends++; }
        public void Dispose() => End();
    }
}
static class BrowserNative
{
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint handle, int command);
}
