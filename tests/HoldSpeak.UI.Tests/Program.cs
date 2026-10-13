using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoldSpeak.App;
using HoldSpeak.Core;
using PhysicalKey = Avalonia.Input.PhysicalKey;
using InputKey = Avalonia.Input.Key;
using RawModifiers = Avalonia.Input.RawInputModifiers;
using Modifiers = Avalonia.Input.KeyModifiers;

string artifacts = Path.GetFullPath(Path.Combine("artifacts", "ui")); Directory.CreateDirectory(artifacts);
string configuration = Path.Combine(artifacts, "config", "settings.json"); if (File.Exists(configuration)) File.Delete(configuration);
if (File.Exists(configuration + ".bak")) File.Delete(configuration + ".bak");
Environment.SetEnvironmentVariable("HOLDSPEAK_CONFIG_DIR", Path.Combine(artifacts, "config"));
AppBuilder.Configure<DesktopApp>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
Brand.Export(Path.GetFullPath("branding"));
ApplicationOption[] applicationOptions = [new("test.exe", "Test application"), new("TEST.exe", "Test duplicate instance"), new("chrome.exe", "Chrome")];
bool listFailure = false;
var window = CreateWindow(() => new LifecyclePlatform(), () => listFailure ? throw new IOException("fixture failure") : applicationOptions); window.Show(); Pump();
var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
for (int i = 0; i < 5; i++) { tabs.SelectedIndex = i; Pump(); using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); frame.Save(Path.Combine(artifacts, $"tab-{i}.png")); }
tabs.SelectedIndex = 3; Pump();
var edit = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn && !t.IsReadOnly);
edit.Text = "{\"holdMs\":1}";
var apply = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "校验并应用"));
apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("配置无效：") == true)) throw new Exception("Invalid configuration feedback missing");
if (File.Exists(Path.Combine(artifacts, "config", "settings.json"))) throw new Exception("Invalid configuration was persisted");
tabs.SelectedIndex = 0; Pump();
var save = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存设置")); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (!File.Exists(Path.Combine(artifacts, "config", "settings.json"))) throw new Exception("Save did not persist configuration");
if (!ConfigurationStore.Parse(File.ReadAllText(configuration)).TriggerAnywhere) throw new Exception("Global mode was not enabled by default");
if (!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "各应用通用")) throw new Exception("Global mode description missing");
tabs.SelectedIndex = 1; Pump();
bool mac = OperatingSystem.IsMacOS();
var recordCaption = mac ? "录入 Mac 开始" : "录入 Windows 开始";
var recordButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, recordCaption));
var keyField = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == (mac ? "Control, Command" : "LCtrl, LWin"));
string? originalKeys = keyField.Text;
recordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (!keyField.IsReadOnly || !keyField.IsFocused) throw new Exception("Capture field was not focused and protected");
window.KeyPressQwerty(PhysicalKey.Escape, RawModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawModifiers.None); Pump();
if (keyField.IsReadOnly || keyField.Text != originalKeys) throw new Exception("Escape changed original shortcut");
recordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
window.KeyPressQwerty(PhysicalKey.ControlLeft, RawModifiers.Control);
window.KeyPressQwerty(PhysicalKey.MetaLeft, RawModifiers.Control | RawModifiers.Meta);
window.KeyReleaseQwerty(PhysicalKey.MetaLeft, RawModifiers.Control);
window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawModifiers.None); Pump();
string expectedKeys = mac ? "Control, Command" : "LCtrl, LWin";
if (keyField.IsReadOnly || keyField.Text != expectedKeys) throw new Exception("Chord capture mismatch");
recordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
tabs.SelectedIndex = 0; Pump();
if (keyField.IsReadOnly || keyField.Text != expectedKeys) throw new Exception("Tab change did not cancel capture safely");
tabs.SelectedIndex = 1; Pump();
var saveCaptured = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存语音配置"));
saveCaptured.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
var capturedProfile = ConfigurationStore.Parse(File.ReadAllText(configuration)).Profiles.Single();
if (!capturedProfile.Keys(mac).SequenceEqual(mac ? new[] { Key.Control, Key.Command } : new[] { Key.LCtrl, Key.LWin })) throw new Exception("Recorded shortcut did not persist");
CheckCaptureModel();
var addProfile = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "另存为新配置")); addProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
var saved = HoldSpeak.Core.ConfigurationStore.Parse(File.ReadAllText(configuration));
if (saved.Profiles.Length != 2) throw new Exception("Profile clone did not persist");
var deleteProfile = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "删除当前配置")); deleteProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (HoldSpeak.Core.ConfigurationStore.Parse(File.ReadAllText(configuration)).Profiles.Length != 1) throw new Exception("Profile delete did not persist");
tabs.SelectedIndex = 2; Pump();
var manualSection = window.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "找不到程序？手动填写程序名")); manualSection.IsExpanded = true; Pump();
var appId = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark?.StartsWith("Windows:") == true); appId.Text = "manual.exe";
var refreshApps = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "刷新应用列表"));
Refresh();
if (appId.Text != "manual.exe") throw new Exception("Refresh overwrote manually entered app ID");
var applicationPicker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.PlaceholderText, "选择当前运行的应用"));
if (applicationPicker.ItemsSource!.Cast<ApplicationOption>().Count() != 2) throw new Exception("Same-name running programs were not deduplicated");
applicationPicker.SelectedIndex = Array.FindIndex(applicationPicker.ItemsSource!.Cast<ApplicationOption>().ToArray(), a => a.AppId.Equals("test.exe", StringComparison.OrdinalIgnoreCase)); Pump();
if (appId.Text != "manual.exe" || ConfigurationStore.Parse(File.ReadAllText(configuration)).AppRules.Length != 0) throw new Exception("Application choice changed manual entry or saved without explicit action");
var addBlacklist = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "加入黑名单"));
addBlacklist.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
addBlacklist.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
var blacklistRule = ConfigurationStore.Parse(File.ReadAllText(configuration)).AppRules.Single();
if (blacklistRule.AppId != "test.exe" || blacklistRule.Enabled || ConfigurationStore.Parse(File.ReadAllText(configuration)).ResolveShortcut("TEST.EXE", null) != null) throw new Exception("One-click blacklist did not save name-only case-insensitive exclusion");
if (window.GetVisualDescendants().OfType<Button>().Count(b => Equals(b.Content, "移除") && Equals(b.Tag, "test.exe")) != 1) throw new Exception("Added rule is not visible with direct removal action");
var reopenedRules = CreateWindow(() => new LifecyclePlatform()); reopenedRules.Show(); Pump(); reopenedRules.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 2; Pump();
if (!reopenedRules.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "移除") && Equals(b.Tag, "test.exe"))) throw new Exception("Saved blacklist not visible after reopening");
Drive(reopenedRules.ShutdownAsync()); reopenedRules.Close();
manualSection.IsExpanded = false; Pump();
using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No blacklist frame")) frame.Save(Path.Combine(artifacts, "blacklist.png"));
applicationOptions = []; Refresh();
if (!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("未找到可选应用") == true)) throw new Exception("Empty application list feedback missing");
addBlacklist.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (ConfigurationStore.Parse(File.ReadAllText(configuration)).AppRules.Length != 1) throw new Exception("No-selection blacklist modified existing rules");
listFailure = true; Refresh();
if (!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("读取应用列表失败：") == true)) throw new Exception("Application list failure feedback missing");
manualSection.IsExpanded = true; Pump(); appId.Text = "other.exe";
var saveRule = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "手动加入")); saveRule.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (HoldSpeak.Core.ConfigurationStore.Parse(File.ReadAllText(configuration)).AppRules.Length != 2) throw new Exception("Manual blacklist did not persist");
manualSection.IsExpanded = false; Pump();
using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rule list frame")) frame.Save(Path.Combine(artifacts, "blacklist-list.png"));
window.Width = 900; window.Height = 700; Pump();
var addBounds = addBlacklist.TranslatePoint(new Point(0, 0), window);
if (addBounds == null || addBounds.Value.Y < 0 || addBounds.Value.Y + addBlacklist.Bounds.Height > 640) throw new Exception("Blacklist add action clipped in compact window");
using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No compact list frame")) frame.Save(Path.Combine(artifacts, "blacklist-list-900.png"));
window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "移除") && Equals(b.Tag, "test.exe")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (ConfigurationStore.Parse(File.ReadAllText(configuration)).AppRules.Single().AppId != "other.exe" || window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "移除") && Equals(b.Tag, "test.exe"))) throw new Exception("Remove targeted wrong row or list was not updated");
window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "移除") && Equals(b.Tag, "other.exe")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (HoldSpeak.Core.ConfigurationStore.Parse(File.ReadAllText(configuration)).AppRules.Length != 0) throw new Exception("App rule delete did not persist");
if (!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "还没有添加黑名单")) throw new Exception("Empty blacklist guidance missing");
manualSection.IsExpanded = false; window.Width = 900; window.Height = 700; Pump();
using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No compact blacklist frame")) frame.Save(Path.Combine(artifacts, "blacklist-empty-900.png"));
window.Close();
var platforms = new List<LifecyclePlatform>();
var recoveryWindow = CreateWindow(() => { var p = new LifecyclePlatform(); platforms.Add(p); return p; });
recoveryWindow.Show(); Pump(); Drive(recoveryWindow.EnableOnStartupAsync());
Drive(recoveryWindow.EnableOnStartupAsync());
if (platforms.Count != 1 || platforms[0].Starts != 1) throw new Exception("First platform not started");
if (platforms[0].DetectionEnabled) throw new Exception("Global startup did not skip detection warmup");
var holdSlider = recoveryWindow.GetVisualDescendants().OfType<Slider>().Single();
holdSlider.Value = 300; Pump();
if (ConfigurationStore.Parse(File.ReadAllText(configuration)).HoldMs != 300) throw new Exception("Hold slider did not save immediately");
using (var frame = recoveryWindow.CaptureRenderedFrame() ?? throw new Exception("No enabled frame")) frame.Save(Path.Combine(artifacts, "settings-enabled-1040.png"));
recoveryWindow.Width = 900; recoveryWindow.Height = 700; Pump();
using (var frame = recoveryWindow.CaptureRenderedFrame() ?? throw new Exception("No compact frame")) frame.Save(Path.Combine(artifacts, "settings-enabled-900.png"));
var settingsSave = recoveryWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存设置"));
var bounds = settingsSave.TranslatePoint(new Point(0, settingsSave.Bounds.Height), recoveryWindow);
if (bounds == null || bounds.Value.Y > recoveryWindow.Bounds.Height - 35) throw new Exception("Compact settings save button clipped");
var voiceSwitch = VoiceSwitch(recoveryWindow);
if (voiceSwitch.IsChecked != true) throw new Exception("Enabled switch not checked");
voiceSwitch.Focus(); recoveryWindow.KeyPressQwerty(PhysicalKey.Space, RawModifiers.None); recoveryWindow.KeyReleaseQwerty(PhysicalKey.Space, RawModifiers.None); Pump();
var switchDeadline = DateTime.UtcNow.AddSeconds(3);
while (!voiceSwitch.IsEnabled && DateTime.UtcNow < switchDeadline) { Pump(); Thread.Sleep(10); }
if (voiceSwitch.IsChecked != false) throw new Exception("Space did not pause voice switch");
using (var frame = recoveryWindow.CaptureRenderedFrame() ?? throw new Exception("No paused frame")) frame.Save(Path.Combine(artifacts, "settings-paused-900.png"));
if (holdSlider.Value != 300 || ConfigurationStore.Parse(File.ReadAllText(configuration)).HoldMs != 300) throw new Exception("Pause reverted hold threshold");
var switchPoint = voiceSwitch.TranslatePoint(new Point(26, 16), recoveryWindow) ?? throw new Exception("Switch is detached");
recoveryWindow.MouseDown(switchPoint, Avalonia.Input.MouseButton.Left, RawModifiers.None);
recoveryWindow.MouseUp(switchPoint, Avalonia.Input.MouseButton.Left, RawModifiers.None); Pump();
switchDeadline = DateTime.UtcNow.AddSeconds(3);
while (!voiceSwitch.IsEnabled && DateTime.UtcNow < switchDeadline) { Pump(); Thread.Sleep(10); }
if (voiceSwitch.IsChecked != true) throw new Exception("Mouse click did not enable voice switch");
using (var report = System.Text.Json.JsonDocument.Parse(recoveryWindow.DiagnosticJson()))
    if (report.RootElement.GetProperty("holdMs").GetInt32() != 300) throw new Exception("Re-enable reverted active hold threshold");
var reopenedWindow = CreateWindow(() => new LifecyclePlatform()); reopenedWindow.Show(); Pump();
if (reopenedWindow.GetVisualDescendants().OfType<Slider>().Single().Value != 300) throw new Exception("Reopening reverted saved hold threshold");
Drive(reopenedWindow.ShutdownAsync()); reopenedWindow.Close();
platforms[0].Fail();
var deadline = DateTime.UtcNow.AddSeconds(3);
while (!recoveryWindow.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text == "待恢复") && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
if (voiceSwitch.IsChecked != false || !recoveryWindow.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text == "待恢复")) throw new Exception("Fault did not turn switch off and offer recovery");
using (var report = System.Text.Json.JsonDocument.Parse(recoveryWindow.DiagnosticJson()))
{
    if (!report.RootElement.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("code").GetString() == "platform_failed")) throw new Exception("Diagnostic report lost platform fault");
    if (report.RootElement.TryGetProperty("profiles", out _)) throw new Exception("Diagnostic report includes configuration profiles");
}
Drive(recoveryWindow.ToggleAsync());
if (platforms.Count != 2 || !platforms[0].Disposed || platforms[1].Starts != 1) throw new Exception("Faulty platform was not replaced");
platforms[1].Fail(); platforms[1].Resume(); platforms[1].Resume();
var automaticDeadline = DateTime.UtcNow.AddSeconds(5);
while (platforms.Count < 3 && DateTime.UtcNow < automaticDeadline) { Pump(); Thread.Sleep(10); }
while (voiceSwitch.IsChecked != true && DateTime.UtcNow < automaticDeadline) { Pump(); Thread.Sleep(10); }
if (platforms.Count != 3 || !platforms[1].Disposed || platforms[2].Starts != 1) throw new Exception("Automatic recovery did not replace the interrupted platform exactly once");
var recoveryTabs = recoveryWindow.GetVisualDescendants().OfType<TabControl>().Single(); recoveryTabs.SelectedIndex = 1; Pump();
var pauseCapture = recoveryWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, recordCaption));
pauseCapture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (voiceSwitch.IsChecked != false) throw new Exception("Capture did not pause running engine");
platforms[2].Fail(); platforms[2].Resume();
var pausedDeadline = DateTime.UtcNow.AddMilliseconds(900);
while (DateTime.UtcNow < pausedDeadline) { Pump(); Thread.Sleep(10); }
if (platforms.Count != 3) throw new Exception("User pause was overridden by recovery");
Drive(recoveryWindow.ShutdownAsync()); recoveryWindow.Close();
if (!platforms[2].Disposed) throw new Exception("Replacement platform not disposed");
var startupPlatform = new LifecyclePlatform { FailDuringStart = true };
var startupWindow = CreateWindow(() => startupPlatform); startupWindow.Show(); Pump();
Drive(startupWindow.EnableOnStartupAsync());
if (!startupPlatform.Disposed || !startupWindow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("无法启用：") == true)) throw new Exception("Startup fault was incorrectly shown as enabled");
Drive(startupWindow.ShutdownAsync()); startupWindow.Close();
var retryPlatforms = new List<LifecyclePlatform>();
var retryWindow = CreateWindow(() => { var p = new LifecyclePlatform { FailDuringStart = retryPlatforms.Count == 1 }; retryPlatforms.Add(p); return p; });
retryWindow.Show(); Pump(); Drive(retryWindow.EnableOnStartupAsync());
retryPlatforms[0].Fail(); retryPlatforms[0].Resume();
var retryDeadline = DateTime.UtcNow.AddSeconds(7);
while ((retryPlatforms.Count < 3 || VoiceSwitch(retryWindow).IsChecked != true) && DateTime.UtcNow < retryDeadline) { Pump(); Thread.Sleep(10); }
if (retryPlatforms.Count != 3 || !retryPlatforms[1].Disposed || retryPlatforms[2].Starts != 1) throw new Exception("Automatic recovery did not retry after transient startup failure");
Drive(retryWindow.ShutdownAsync()); retryWindow.Close();
InputMethodInfo wechatInput = new("wechat", "微信输入法", "wechat"), sogouInput = new("sogou", "搜狗输入法", "sogou");
InputMethodInfo? activeInput = wechatInput;
var routingWindow = new MainWindow(() => new LifecyclePlatform(), () => [], () => [wechatInput, sogouInput], () => activeInput, new FakeAutoStart());
routingWindow.Show(); Pump();
var discoveryDeadline = DateTime.UtcNow.AddSeconds(5);
while (ConfigurationStore.Parse(File.ReadAllText(configuration)).InputMethodBindings.Length < 2 && DateTime.UtcNow < discoveryDeadline) { Pump(); Thread.Sleep(10); }
var routingSettings = ConfigurationStore.Parse(File.ReadAllText(configuration));
if (routingSettings.InputMethodBindings.Length != 2) throw new Exception("Input methods were not discovered and bound");
string fixedProfileId = routingSettings.DefaultProfileId;
var selection = routingWindow.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemsSource?.OfType<string>().FirstOrDefault() == "指定快捷方式");
selection.SelectedIndex = 1; Pump();
var displayDeadline = DateTime.UtcNow.AddSeconds(3);
while (!routingWindow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("当前：微信输入法") == true) && DateTime.UtcNow < displayDeadline) { Pump(); Thread.Sleep(10); }
if (ConfigurationStore.Parse(File.ReadAllText(configuration)).ShortcutSelection != ShortcutSelectionMode.Automatic) throw new Exception("Automatic mode not persisted");
activeInput = sogouInput; displayDeadline = DateTime.UtcNow.AddSeconds(3);
using (var timerLoop = new CancellationTokenSource(TimeSpan.FromMilliseconds(900))) Dispatcher.UIThread.MainLoop(timerLoop.Token);
while (!routingWindow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("当前：搜狗输入法") == true) && DateTime.UtcNow < displayDeadline) { Pump(); Thread.Sleep(10); }
if (!routingWindow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("当前：搜狗输入法") == true)) throw new Exception("Current IME display did not follow switch");
using (var frame = routingWindow.CaptureRenderedFrame() ?? throw new Exception("No automatic mode frame")) frame.Save(Path.Combine(artifacts, "automatic-mode.png"));
var routingTabs = routingWindow.GetVisualDescendants().OfType<TabControl>().Single(); routingTabs.SelectedIndex = 1; Pump();
var editProfile = routingWindow.GetVisualDescendants().OfType<ComboBox>().Single(c => c.PlaceholderText == "选择要编辑的快捷键配置");
editProfile.SelectedIndex = Array.FindIndex(routingSettings.Profiles, p => p.Id == "ime-sogou"); Pump();
var routingKeys = routingWindow.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "LCtrl, LWin");
if (routingKeys.Text != "F2") throw new Exception("Sogou preset did not use confirmed F2");
routingKeys.Text = "F3";
routingWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存语音配置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Drive(routingWindow.RefreshInputMethodsAsync());
routingSettings = ConfigurationStore.Parse(File.ReadAllText(configuration));
if (routingSettings.Profiles.Single(p => p.Id == "ime-sogou").WindowsKeys.Single() != Key.F3 || routingSettings.DefaultProfileId != fixedProfileId) throw new Exception("Refresh overwrote edited shortcut or fixed selection");
using (var frame = routingWindow.CaptureRenderedFrame() ?? throw new Exception("No input mapping frame")) frame.Save(Path.Combine(artifacts, "input-method-bindings.png"));
selection.SelectedIndex = 0; Pump();
if (ConfigurationStore.Parse(File.ReadAllText(configuration)).ShortcutSelection != ShortcutSelectionMode.Fixed) throw new Exception("Fixed mode not persisted");
Drive(routingWindow.ShutdownAsync()); routingWindow.Close();
var login = new FakeAutoStart();
var loginWindow = new MainWindow(() => new LifecyclePlatform(), () => [], () => [], () => null, login); loginWindow.Show(); Pump();
var loginSwitch = loginWindow.GetVisualDescendants().OfType<IosSwitch>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "开机自启动");
if (loginSwitch.IsChecked != false || login.Writes != 0) throw new Exception("Startup was enabled without user action");
loginSwitch.IsChecked = true; loginSwitch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (!login.Enabled || loginSwitch.IsChecked != true) throw new Exception("Enable login item failed");
Drive(loginWindow.ToggleAsync()); Drive(loginWindow.ToggleAsync());
if (!login.Enabled) throw new Exception("Pausing changed auto startup");
Drive(loginWindow.ShutdownAsync()); loginWindow.Close();
var reopened = new MainWindow(() => new LifecyclePlatform(), () => [], () => [], () => null, login); reopened.Show(); Pump();
var reopenedSwitch = reopened.GetVisualDescendants().OfType<IosSwitch>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "开机自启动");
if (reopenedSwitch.IsChecked != true) throw new Exception("Reopened window lost system registration");
login.FailWrite = true; reopenedSwitch.IsChecked = false; reopenedSwitch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (reopenedSwitch.IsChecked != true || !reopened.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("开机自启动设置失败") == true)) throw new Exception("Failed registration did not restore switch");
login.FailWrite = false; reopenedSwitch.IsChecked = false; reopenedSwitch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
if (login.Enabled) throw new Exception("Disable login item failed");
reopened.Width = 900; reopened.Height = 700; Pump();
using (var frame = reopened.CaptureRenderedFrame() ?? throw new Exception("No startup frame")) frame.Save(Path.Combine(artifacts, "autostart-900.png"));
Drive(reopened.ShutdownAsync()); reopened.Close();
CheckAutoStartRegistration();
Console.WriteLine("PASS auto startup opt-in, persistence, pause independence, disable, write failure rollback, native registration roundtrip and 5-tab UI regressions"); return 0;
void Refresh()
{
    refreshApps.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!refreshApps.IsEnabled && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
    Pump(); if (!refreshApps.IsEnabled) throw new TimeoutException("Application list refresh timed out");
}
static void CheckCaptureModel()
{
    var repeat = new ShortcutCapture(false);
    repeat.Press(InputKey.F8, PhysicalKey.F8, Modifiers.None); repeat.Press(InputKey.F8, PhysicalKey.F8, Modifiers.None);
    if (!repeat.Release(InputKey.F8, PhysicalKey.F8)!.SequenceEqual(new[] { Key.F8 })) throw new Exception("Repeat duplicated chord");
    var sequence = new ShortcutCapture(false);
    var reverseOrder = new ShortcutCapture(false);
    reverseOrder.Press(InputKey.A, PhysicalKey.A, Modifiers.None);
    reverseOrder.Press(InputKey.LeftCtrl, PhysicalKey.ControlLeft, Modifiers.Control);
    reverseOrder.Release(InputKey.LeftCtrl, PhysicalKey.ControlLeft);
    if (!reverseOrder.Release(InputKey.A, PhysicalKey.A)!.SequenceEqual(new[] { Key.LCtrl, Key.A })) throw new Exception("Modifiers were not ordered before regular keys");
    sequence.Press(InputKey.LeftCtrl, PhysicalKey.ControlLeft, Modifiers.Control);
    sequence.Press(InputKey.A, PhysicalKey.A, Modifiers.Control); sequence.Release(InputKey.A, PhysicalKey.A);
    Reject(() => sequence.Press(InputKey.B, PhysicalKey.B, Modifiers.Control));
    Reject(() => new ShortcutCapture(false).Press(InputKey.A, PhysicalKey.A, Modifiers.Control));
    Reject(() => new ShortcutCapture(true).Press(InputKey.RightCtrl, PhysicalKey.ControlRight, Modifiers.Control));
    Reject(() => new ShortcutCapture(true).Press(InputKey.F24, PhysicalKey.F24, Modifiers.None));
    var oversized = new ShortcutCapture(false);
    foreach (var key in new[] { PhysicalKey.A, PhysicalKey.B, PhysicalKey.C, PhysicalKey.D, PhysicalKey.E }) oversized.Press(InputKey.None, key, Modifiers.None);
    Reject(() => oversized.Press(InputKey.F, PhysicalKey.F, Modifiers.None));
    if (ShortcutCapture.Map(InputKey.Q, PhysicalKey.A, true) != Key.A || ShortcutCapture.Map(InputKey.Q, PhysicalKey.None, true) != null || ShortcutCapture.Map(InputKey.RightAlt, PhysicalKey.AltRight, false) != Key.RAlt) throw new Exception("Physical/platform mapping mismatch");
    static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid capture was accepted");
    }
}
static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
static void Drive(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
    if (!task.IsCompleted) throw new TimeoutException("UI lifecycle timed out");
    task.GetAwaiter().GetResult(); Pump();
}
static IosSwitch VoiceSwitch(MainWindow window) => window.GetVisualDescendants().OfType<IosSwitch>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "长按说话开关");
static MainWindow CreateWindow(Func<IDesktopPlatform> platform, Func<ApplicationOption[]>? apps = null) => new MainWindow(platform, apps, () => [], () => null, new FakeAutoStart());
static void CheckAutoStartRegistration()
{
    var isolatedKey = @"Software\HoldSpeak\Tests\" + Guid.NewGuid().ToString("N");
    var service = new AutoStart(Environment.ProcessPath!, Path.Combine(Path.GetTempPath(), "左键说 & settings"), runKey: isolatedKey);
    var xml = System.Xml.Linq.XDocument.Parse(service.MacPlist);
    if (!xml.Descendants("array").Single().Elements("string").Select(e => e.Value).SequenceEqual(service.Arguments)) throw new Exception("Mac arguments escaped incorrectly");
    if (AutoStart.QuoteWindowsArgument("a b\\") != "\"a b\\\\\"" || AutoStart.QuoteWindowsArgument("a\"b") != "\"a\\\"b\"") throw new Exception("Windows quoting failed");
    if (!OperatingSystem.IsWindows()) return;
    try
    {
        if (service.IsEnabled()) throw new Exception("Test registration already exists");
        service.SetEnabled(true);
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(isolatedKey))
            if (key?.GetValue("HoldSpeak") as string != service.WindowsCommand) throw new Exception("Startup command not persisted exactly");
        if (!service.IsEnabled() || !new AutoStart(Environment.ProcessPath!, service.Arguments[3], runKey: isolatedKey).IsEnabled()) throw new Exception("System registration not retained");
        service.SetEnabled(false);
        if (service.IsEnabled()) throw new Exception("Registration not removed");
    }
    finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(isolatedKey, false); }
}
sealed class FakeAutoStart : IAutoStart
{
    public bool Enabled, FailWrite;
    public int Writes;
    public bool IsEnabled() => Enabled;
    public void SetEnabled(bool enabled) { if (FailWrite) throw new IOException("fixture"); Enabled = enabled; Writes++; }
}
sealed class LifecyclePlatform : IDesktopPlatform
{
    public bool DetectionEnabled = true;
    public void SetDetectionEnabled(bool enabled) => DetectionEnabled = enabled;
    public int Starts;
    public bool Disposed;
    public bool FailDuringStart;
    public bool IsMac => false;
    public bool PhysicalLeftDown => false;
    public string ForegroundAppId => "test.exe";
    public string PermissionStatus => "test";
    public (double X, double Y) PointerPosition => (0, 0);
    public event Action<PointerEvent>? Pointer { add { } remove { } }
    public event Action<string>? Faulted;
    public event Action<string>? RecoveryRequested;
    public ITargetProbe Probe { get; } = new ProbeStub();
    public void Start() { Starts++; if (FailDuringStart) { Fail(); Thread.Sleep(100); } }
    public void Fail() => Faulted?.Invoke("guardian failed");
    public void Resume() => RecoveryRequested?.Invoke("test desktop returned");
    public IInputSession Begin(VoiceProfile profile, bool releaseMouse) => throw new Exception("No gesture in lifecycle test");
    public void EmergencyRelease() { }
    public void Dispose() => Disposed = true;
    sealed class ProbeStub : ITargetProbe
    {
        public Task<TargetSnapshot> ProbeAsync(double x, double y, CancellationToken token) => Task.FromResult(new TargetSnapshot(Editability.Unknown, "", 0, "", "test"));
    }
}
