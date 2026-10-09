using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HoldSpeak.Core;

namespace HoldSpeak.App;

public sealed class MainWindow : Window
{
    private Settings settings = new();
    private EngineHost? host;
    private Action<Status>? statusHandler;
    private Action<string>? recoveryHandler;
    private bool autoResumeWanted, autoRecoveryPending;
    private IDesktopPlatform? platform;
    private bool running, busy, restartRequired, syncingControls;
    private readonly Func<IDesktopPlatform> createPlatform;
    private readonly Func<ApplicationOption[]> listApplications;
    private readonly TextBlock state = Label("尚未启用", 17, "#1677FF", FontWeight.SemiBold);
    private readonly TextBlock detail = Label("全局长按模式：任何位置按住左键即可触发语音。", 13, "#667085");
    private readonly TextBlock notice = Label("", 13, "#AD6800");
    private readonly IosSwitch toggle = new();
    private readonly TextBlock toggleLabel = Label("已暂停", 13, "#667085");
    private readonly ComboBox profile = new() { MinWidth = 240 };
    private readonly Slider hold = new() { Minimum = 300, Maximum = 1500, TickFrequency = 50, IsSnapToTickEnabled = true, Height = 40, MinHeight = 40 };
    private readonly TextBlock holdValue = Label("600 ms", 14, "#E4EBF5");
    private readonly NumericUpDown movement = new() { Minimum = 2, Maximum = 40, Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox releaseMouse = new() { Content = "开始时结束鼠标拖选（推荐）" };
    private readonly TextBox editor = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, monospace"), Height = 240 };
    private readonly TextBox profileName = new() { Watermark = "语音软件名称" };
    private readonly TextBox winKeys = new() { Watermark = "LCtrl, LWin" };
    private readonly TextBox macKeys = new() { Watermark = "Control, Command" };
    private readonly TextBox winStopKeys = new() { Watermark = "仅分别开始／结束模式需要" };
    private readonly TextBox macStopKeys = new() { Watermark = "仅分别开始／结束模式需要" };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "保持：按住开始，松开结束", "切换：开始和结束各按一次", "分别发送开始和结束快捷键" }, SelectedIndex = 0 };
    private readonly ComboBox ruleList = new() { MinWidth = 240, PlaceholderText = "选择已有规则" };
    private readonly TextBox ruleApp = new() { Watermark = "Windows: chrome.exe / Mac: com.google.Chrome" };
    private readonly ComboBox runningApps = new() { PlaceholderText = "选择当前运行的应用", MinWidth = 260 };
    private readonly Button refreshApps = new() { Content = "刷新应用列表" };
    private readonly CheckBox ruleEnabled = new() { Content = "在该应用中启用长按说话", IsChecked = true };
    private readonly ComboBox ruleProfile = new() { MinWidth = 240 };
    private readonly Button inspectTarget = new() { Content = "3 秒后检查鼠标处输入框" };
    private readonly TextBox diagnostics = new() { AcceptsReturn = true, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
    private readonly Queue<string> logs = new();
    private readonly DiagnosticLog diagnosticLog = new();
    private ShortcutCapture? shortcutCapture;
    private TextBox? captureTarget;
    private readonly DispatcherTimer captureTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    public bool IsShuttingDown { get; private set; }
    public MainWindow() : this(Program.CreatePlatform) { }
    public MainWindow(Func<IDesktopPlatform> createPlatform, Func<ApplicationOption[]>? listApplications = null)
    {
        this.createPlatform = createPlatform;
        this.listApplications = listApplications ?? Program.ListApplications;
        Title = "左键说 · 按住左键说话"; Width = 1040; Height = 800; MinWidth = 900; MinHeight = 700;
        Background = Brush.Parse("#F5F7FA"); FontFamily = new FontFamily("Microsoft YaHei UI, PingFang SC, sans-serif"); FontSize = 14;
        Icon = Brand.Icon();
        try
        {
            settings = ConfigurationStore.Load(AppPaths.Settings, out bool recovered);
            if (recovered) notice.Text = "主配置损坏，已加载上一次保存的备份。原文件保留；确认设置后再保存。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { notice.Text = "配置加载失败，已使用默认值：" + ex.Message; }
        if (!File.Exists(AppPaths.Settings) && OperatingSystem.IsWindows() && WeTypeInstalled())
            settings = settings with { Profiles = [new VoiceProfile { Name = "微信输入法 · Ctrl + Win 长按" }] };
        if (!File.Exists(AppPaths.Settings) && OperatingSystem.IsMacOS())
            settings = settings with { Profiles = [new VoiceProfile { Name = "语音入口 · 请配置 Mac 快捷键" }] };
        settings = settings with { Enabled = false };
        notice.IsVisible = !string.IsNullOrEmpty(notice.Text);
        notice.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBlock.TextProperty) return;
            notice.IsVisible = !string.IsNullOrEmpty(notice.Text);
            bool problem = new[] { "失败", "无效", "无法", "超时", "损坏" }.Any(s => notice.Text?.Contains(s) == true);
            bool success = notice.Text?.Contains("已保存") == true || notice.Text?.Contains("已自动保存") == true || notice.Text?.Contains("已自动恢复") == true;
            notice.Foreground = Brush.Parse(problem ? "#AD6800" : success ? "#027A48" : "#667085");
        };
        Content = BuildContent(); SyncControls();
        toggle.Click += async (_, _) => await ToggleAsync();
        profile.SelectionChanged += (_, _) => SyncProfileFields();
        ruleList.SelectionChanged += (_, _) => SyncRuleFields();
        runningApps.SelectionChanged += (_, _) => { if (runningApps.SelectedItem is ApplicationOption app) ruleApp.Text = app.AppId; };
        refreshApps.Click += async (_, _) => await RefreshApplicationsAsync();
        hold.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            holdValue.Text = $"{(int)hold.Value} ms";
            if (syncingControls || (int)hold.Value == settings.HoldMs) return;
            try
            {
                var next = settings with { HoldMs = (int)hold.Value };
                ConfigurationStore.Save(AppPaths.Settings, next);
                settings = next; host?.Configure(next);
                editor.Text = JsonSerializer.Serialize(settings, ConfigurationStore.Json);
                notice.Text = $"长按时间 {(int)hold.Value} ms 已自动保存并生效。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { notice.Text = "长按时间保存失败：" + ex.Message; }
        };
        captureTimer.Tick += (_, _) => CancelShortcutCapture("快捷键录入超时，原设置保留。");
        Deactivated += (_, _) => CancelShortcutCapture("窗口失去焦点，快捷键录入已取消。");
    }
    private Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(24, 20) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(Brand.Mark(40));
        var title = new StackPanel { Spacing = 2 }; title.Children.Add(Label("左键说", 24, "#101828", FontWeight.Bold)); title.Children.Add(Label(Brand.Tagline, 13, "#667085")); Grid.SetColumn(title, 1); header.Children.Add(title);
        var badge = new Border { Background = Brush.Parse("#ECFDF3"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5), VerticalAlignment = VerticalAlignment.Center, Child = Label("●  本地运行", 12, "#027A48") }; Grid.SetColumn(badge, 2); header.Children.Add(badge); root.Children.Add(header);
        var status = new StackPanel { Spacing = 6 }; status.Children.Add(state); status.Children.Add(detail);
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 }; statusGrid.Children.Add(status);
        var switchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        toggleLabel.VerticalAlignment = VerticalAlignment.Center; switchRow.Children.Add(toggleLabel); switchRow.Children.Add(toggle);
        Grid.SetColumn(switchRow, 1); statusGrid.Children.Add(switchRow);
        var statusCard = Card(statusGrid); statusCard.Padding = new Thickness(18, 14); statusCard.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(statusCard, 1); root.Children.Add(statusCard);
        var hints = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 14) };
        hints.Children.Add(Label("按住左键  →  开始说话  →  松开结束", 12, "#667085")); hints.Children.Add(notice); Grid.SetRow(hints, 2); root.Children.Add(hints);
        var basics = new StackPanel { Spacing = 8 };
        basics.Children.Add(Label("说话方式", 18, "#101828", FontWeight.SemiBold));
        var entry = new StackPanel { Spacing = 6 };
        entry.Children.Add(Label("语音软件", 13, "#344054")); profile.HorizontalAlignment = HorizontalAlignment.Left; profile.Width = 360; entry.Children.Add(profile); basics.Children.Add(entry);
        var holdHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        holdHeader.Children.Add(Label("长按时间", 13, "#344054", FontWeight.SemiBold));
        var timeBadge = new Border { Background = Brush.Parse("#E6F4FF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3), Child = holdValue }; Grid.SetColumn(timeBadge, 1); holdHeader.Children.Add(timeBadge); basics.Children.Add(holdHeader);
        basics.Children.Add(hold);
        basics.Children.Add(Label("调整后自动保存。时间越短，触发越快；也更容易误触。", 12, "#667085"));
        var drag = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        var dragText = new StackPanel { Spacing = 3 }; dragText.Children.Add(Label("拖动取消距离", 13, "#344054", FontWeight.SemiBold)); dragText.Children.Add(Label("按住后移动超过此距离，保留正常拖选。", 12, "#667085")); drag.Children.Add(dragText); Grid.SetColumn(movement, 1); drag.Children.Add(movement); basics.Children.Add(drag);
        basics.Children.Add(releaseMouse);
        var tip = new StackPanel { Spacing = 4 }; tip.Children.Add(Label("各应用通用", 13, "#1677FF", FontWeight.SemiBold)); tip.Children.Add(Label("文字会填入当前有光标的位置。录音与文字转换由所选语音软件完成。", 12, "#667085"));
        basics.Children.Add(new Border { Background = Brush.Parse("#F5F9FF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = tip });
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        var save = new Button { Content = "保存设置" }; save.Classes.Add("accent"); save.Click += (_, _) => SaveBasic(); saveRow.Children.Add(save); var saveHint = Label("语音软件与拖动设置需点击保存", 12, "#667085"); saveHint.VerticalAlignment = VerticalAlignment.Center; saveRow.Children.Add(saveHint); basics.Children.Add(saveRow);
        var profiles = new StackPanel { Spacing = 10 };
        profiles.Children.Add(Label("编辑当前语音配置", 18, "#F0F5FC", FontWeight.SemiBold)); profiles.Children.Add(profileName); profiles.Children.Add(mode);
        profiles.Children.Add(Label("Windows 按键（用英文逗号分隔）", 13, "#9CAABC")); profiles.Children.Add(ShortcutRow(winKeys, false, "录入 Windows 开始"));
        profiles.Children.Add(Label("Mac 按键（需要与原软件设置一致）", 13, "#9CAABC")); profiles.Children.Add(ShortcutRow(macKeys, true, "录入 Mac 开始"));
        profiles.Children.Add(Label("单独结束按键：Windows / Mac", 13, "#9CAABC")); profiles.Children.Add(ShortcutRow(winStopKeys, false, "录入 Windows 结束")); profiles.Children.Add(ShortcutRow(macStopKeys, true, "录入 Mac 结束"));
        profiles.Children.Add(Label("仅能录入当前平台的按键。点击录入后先松开所有键，再按组合键并全部松开；Esc 取消，录入后点击保存。\n录入会暂停鼠标长按；原语音软件仍可能响应它自己的全局快捷键。", 12, "#9CAABC"));
        profiles.Children.Add(Label("支持字母 A–Z、数字 0–9、Space、Enter、Tab、方向键等；Windows F1–F24，Mac F1–F20。\nMac 字母按美式键位映射，Fn 入口暂不支持。切换协议可能与手动操作冲突。", 12, "#9CAABC"));
        var profileButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var saveProfile = new Button { Content = "保存语音配置" }; saveProfile.Click += (_, _) => SaveProfile(false); profileButtons.Children.Add(saveProfile);
        var newProfile = new Button { Content = "另存为新配置" }; newProfile.Click += (_, _) => SaveProfile(true); profileButtons.Children.Add(newProfile);
        var deleteProfile = new Button { Content = "删除当前配置" }; deleteProfile.Click += (_, _) => DeleteProfile(); profileButtons.Children.Add(deleteProfile); profiles.Children.Add(profileButtons);
        var rules = new StackPanel { Spacing = 12 };
        rules.Children.Add(Label("按应用设置", 18, "#F0F5FC", FontWeight.SemiBold)); rules.Children.Add(ruleList);
        var applicationPicker = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        applicationPicker.Children.Add(runningApps); Grid.SetColumn(refreshApps, 1); applicationPicker.Children.Add(refreshApps); rules.Children.Add(applicationPicker);
        rules.Children.Add(Label("刷新后选择应用，自动填写下方标识；未列出的应用仍可手动填写或通过诊断获取。", 12, "#9CAABC"));
        rules.Children.Add(ruleApp); rules.Children.Add(ruleEnabled);
        rules.Children.Add(Label("语音配置（“跟随全局”会使用常规页选择的配置）", 13, "#9CAABC")); rules.Children.Add(ruleProfile);
        rules.Children.Add(Label("可以关闭某些应用中的长按功能，或为它选择其他语音软件。\n全局模式下不会检查输入框类型。", 13, "#9CAABC"));
        var ruleButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var saveRule = new Button { Content = "保存应用规则" }; saveRule.Click += (_, _) => SaveRule(); ruleButtons.Children.Add(saveRule);
        var deleteRule = new Button { Content = "删除应用规则" }; deleteRule.Click += (_, _) => DeleteRule(); ruleButtons.Children.Add(deleteRule); rules.Children.Add(ruleButtons);
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(Label("多软件配置与应用规则", 18, "#F0F5FC", FontWeight.SemiBold));
        advanced.Children.Add(Label("在 JSON 中添加配置；Windows 使用进程名，Mac 使用 Bundle ID。\n未配置的平台不会触发，请按原语音软件设置填写快捷键。", 13, "#9CAABC"));
        advanced.Children.Add(editor);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "校验并应用" }; apply.Click += (_, _) => ApplyJson(); buttons.Children.Add(apply);
        var import = new Button { Content = "导入" }; import.Click += async (_, _) => await ImportAsync(); buttons.Children.Add(import);
        var export = new Button { Content = "导出" }; export.Click += async (_, _) => await ExportAsync(); buttons.Children.Add(export); advanced.Children.Add(buttons);
        var checks = new StackPanel { Spacing = 12 };
        checks.Children.Add(Label("本地诊断", 18, "#F0F5FC", FontWeight.SemiBold));
        checks.Children.Add(Label("日志只显示状态与控件类型，不记录输入内容。\n这里的“快捷键保持中”表示已发送按键，不代表已确认录音。", 13, "#9CAABC"));
        checks.Children.Add(diagnostics);
        var exportDiagnostics = new Button { Content = "导出诊断报告" }; exportDiagnostics.Click += async (_, _) => await ExportDiagnosticsAsync(); checks.Children.Add(exportDiagnostics);
        checks.Children.Add(Label("报告仅包含最近 80 条状态、应用标识及控件探测原因；不含正文、网址或窗口标题。", 12, "#9CAABC"));
        inspectTarget.Click += async (_, _) => await InspectTargetAsync(); checks.Children.Add(inspectTarget);
        checks.Children.Add(Label("点击检查后，在 3 秒内点击目标输入框并把鼠标停在里面。\n只查询控件属性，不发送语音快捷键。", 12, "#9CAABC"));
        var test = new TextBox { Watermark = "可在这里测试点击与拖动；全局模式下，长按也会触发语音", MinHeight = 70, AcceptsReturn = true }; checks.Children.Add(test);
        var exit = new Button { Content = "完全退出" }; exit.Click += async (_, _) => { await ShutdownAsync(); (Avalonia.Application.Current!.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown(); }; checks.Children.Add(exit);
        if (OperatingSystem.IsMacOS())
        {
            var permissions = new Button { Content = "打开 Mac 辅助功能设置" };
            permissions.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility" }, UseShellExecute = false });
            checks.Children.Add(permissions);
        }
        Control Page(Control content) => new ScrollViewer { Content = Card(content), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Control Nav(string icon, string name) => new TextBlock { Text = $"{icon}    {name}", Width = 128, FontSize = 13, Margin = new Thickness(8, 10), Foreground = Brush.Parse("#344054") };
        var tabs = new TabControl { TabStripPlacement = Dock.Left, ItemsSource = new[] { new TabItem { Header = Nav("◉", "常规设置"), Content = Page(basics) }, new TabItem { Header = Nav("♫", "语音软件"), Content = Page(profiles) }, new TabItem { Header = Nav("▦", "应用规则"), Content = Page(rules) }, new TabItem { Header = Nav("≡", "高级配置"), Content = Page(advanced) }, new TabItem { Header = Nav("⌁", "诊断与帮助"), Content = Page(checks) } } };
        tabs.SelectionChanged += (_, _) => CancelShortcutCapture("切换页面，快捷键录入已取消。");
        Grid.SetRow(tabs, 3); root.Children.Add(tabs);
        var footer = Label($"左键说 · HoldSpeak {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}     |     © 2026 HoldSpeak contributors · MIT", 11, "#98A2B3"); footer.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(footer, 4); root.Children.Add(footer);
        return root;
    }
    private static TextBlock Label(string text, double size, string color, FontWeight? weight = null)
    {
        color = color switch { "#F0F5FC" or "#E4EBF5" => "#101828", "#C9D5E3" => "#344054", "#9CAABC" => "#667085", _ => color };
        return new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
    }
    private static bool WeTypeInstalled()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("wetype_server");
        try { return processes.Length > 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
    private static Border Card(Control child) => new() { Background = Brushes.White, BorderBrush = Brush.Parse("#EAECF0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Child = child };
    private void SyncControls()
    {
        syncingControls = true;
        try
        {
        profile.ItemsSource = settings.Profiles.Select(p => p.Name).ToArray(); profile.SelectedIndex = Array.FindIndex(settings.Profiles, p => p.Id == settings.DefaultProfileId);
        hold.Value = settings.HoldMs; holdValue.Text = $"{settings.HoldMs} ms"; movement.Value = settings.MovementDip; releaseMouse.IsChecked = settings.ReleaseMouseOnActivate;
        editor.Text = JsonSerializer.Serialize(settings, ConfigurationStore.Json);
        SyncProfileFields();
        ruleList.ItemsSource = settings.AppRules.Select(r => r.AppId).ToArray();
        ruleProfile.ItemsSource = new[] { "跟随全局" }.Concat(settings.Profiles.Select(p => p.Name)).ToArray(); ruleProfile.SelectedIndex = 0;
        }
        finally { syncingControls = false; }
    }
    private void SyncRuleFields()
    {
        if (ruleList.SelectedIndex < 0 || ruleList.SelectedIndex >= settings.AppRules.Length) return;
        var rule = settings.AppRules[ruleList.SelectedIndex]; ruleApp.Text = rule.AppId; ruleEnabled.IsChecked = rule.Enabled;
        ruleProfile.SelectedIndex = rule.ProfileId == null ? 0 : Array.FindIndex(settings.Profiles, p => p.Id == rule.ProfileId) + 1;
    }
    private async Task RefreshApplicationsAsync()
    {
        if (IsShuttingDown || !refreshApps.IsEnabled) return;
        refreshApps.IsEnabled = false;
        try
        {
            var applications = await Task.Run(listApplications);
            if (IsShuttingDown) return;
            runningApps.ItemsSource = applications;
            notice.Text = applications.Length == 0 ? "未找到可选应用，可以手动填写应用标识。" : $"已列出 {applications.Length} 个应用，请选择后保存规则。";
        }
        catch (Exception ex) { if (!IsShuttingDown) notice.Text = "读取应用列表失败：" + ex.Message; }
        finally { refreshApps.IsEnabled = true; }
    }
    private void SaveRule()
    {
        try
        {
            var id = ruleApp.Text?.Trim() ?? ""; int selected = ruleProfile.SelectedIndex;
            var rule = new AppRule { AppId = id, Enabled = ruleEnabled.IsChecked == true, ProfileId = selected <= 0 ? null : settings.Profiles[selected - 1].Id };
            Save(settings with { AppRules = [..settings.AppRules.Where(r => !r.AppId.Equals(id, StringComparison.OrdinalIgnoreCase)), rule] }); notice.Text = "应用规则已保存。";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { notice.Text = "规则无效：" + ex.Message; }
    }
    private void DeleteRule()
    {
        var id = ruleApp.Text?.Trim() ?? "";
        if (!settings.AppRules.Any(r => r.AppId.Equals(id, StringComparison.OrdinalIgnoreCase))) { notice.Text = "没有找到该应用规则。"; return; }
        try { Save(settings with { AppRules = settings.AppRules.Where(r => !r.AppId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray() }); notice.Text = "应用规则已删除。"; }
        catch (IOException ex) { notice.Text = "删除失败：" + ex.Message; }
    }
    private async Task InspectTargetAsync()
    {
        autoResumeWanted = false;
        inspectTarget.IsEnabled = false;
        try
        {
            if (running) { settings = settings with { Enabled = false }; host!.Configure(settings); running = false; SyncSwitch(); }
            notice.Text = "请在 3 秒内点击目标输入框，将鼠标留在输入框内。检查期间已暂停长按。";
            await Task.Delay(3000);
            using var inspectPlatform = Program.CreatePlatform(); var p = inspectPlatform.PointerPosition;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await inspectPlatform.Probe.ProbeAsync(p.X, p.Y, timeout.Token);
            diagnosticLog.Add(new(GestureState.Suspended, "inspect", "", result, result));
            notice.Text = $"检查结果：{result.Editability} · 应用：{result.AppId} · 控件：{result.ControlType} · 原因：{result.Reason}";
            ruleApp.Text = result.AppId;
            logs.Enqueue($"{DateTime.Now:HH:mm:ss}  inspect {result.Editability} {result.ControlType} {result.Reason}");
            while (logs.Count > 80) logs.Dequeue(); diagnostics.Text = string.Join(Environment.NewLine, logs);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) { notice.Text = "检查失败：" + ex.Message; }
        finally { inspectTarget.IsEnabled = true; }
    }
    private Control ShortcutRow(TextBox target, bool mac, string caption)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var record = new Button { Content = caption, IsEnabled = mac == OperatingSystem.IsMacOS() };
        record.Click += async (_, _) => await StartShortcutCaptureAsync(target, mac);
        row.Children.Add(target); Grid.SetColumn(record, 1); row.Children.Add(record);
        target.LostFocus += (_, _) => { if (ReferenceEquals(captureTarget, target)) CancelShortcutCapture("快捷键录入已取消，原设置保留。"); };
        target.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, e) =>
        {
            if (!ReferenceEquals(captureTarget, target) || shortcutCapture == null) return;
            e.Handled = true;
            if (e.Key == Avalonia.Input.Key.Escape || e.PhysicalKey == Avalonia.Input.PhysicalKey.Escape)
            { CancelShortcutCapture("快捷键录入已取消，原设置保留。"); return; }
            try { shortcutCapture.Press(e.Key, e.PhysicalKey, e.KeyModifiers); }
            catch (InvalidDataException ex) { CancelShortcutCapture(ex.Message); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        target.AddHandler(Avalonia.Input.InputElement.KeyUpEvent, (_, e) =>
        {
            if (!ReferenceEquals(captureTarget, target) || shortcutCapture == null) return;
            e.Handled = true;
            var keys = shortcutCapture.Release(e.Key, e.PhysicalKey);
            if (keys == null) return;
            CancelShortcutCapture(); target.Text = string.Join(", ", keys);
            notice.Text = "快捷键已录入，请点击“保存语音配置”。长按仍处于暂停状态。";
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return row;
    }
    private async Task StartShortcutCaptureAsync(TextBox target, bool mac)
    {
        if (busy || IsShuttingDown) return;
        autoResumeWanted = false;
        CancelShortcutCapture();
        if (running) await ToggleAsync();
        if (busy || running || IsShuttingDown) return;
        shortcutCapture = new(mac); captureTarget = target; target.IsReadOnly = true;
        if (!target.Focus()) { CancelShortcutCapture("无法聚焦按键录入框，请重试。"); return; }
        captureTimer.Start();
        notice.Text = "请先松开所有键，再按一次组合键并全部松开。Esc 取消；10 秒后超时。";
    }
    private void CancelShortcutCapture(string? message = null)
    {
        if (shortcutCapture == null) return;
        shortcutCapture = null; captureTimer.Stop();
        if (captureTarget != null) captureTarget.IsReadOnly = false;
        captureTarget = null;
        if (message != null) notice.Text = message;
    }
    private void SyncProfileFields()
    {
        CancelShortcutCapture();
        if (profile.SelectedIndex < 0 || profile.SelectedIndex >= settings.Profiles.Length) return;
        var p = settings.Profiles[profile.SelectedIndex]; profileName.Text = p.Name; mode.SelectedIndex = (int)p.Mode;
        winKeys.Text = string.Join(", ", p.WindowsKeys); macKeys.Text = string.Join(", ", p.MacKeys);
        winStopKeys.Text = string.Join(", ", p.WindowsStopKeys); macStopKeys.Text = string.Join(", ", p.MacStopKeys);
    }
    private static Key[] ParseKeys(string? text) => KeyCatalog.Parse(text);
    private void SaveProfile(bool add)
    {
        try
        {
            var old = settings.Profiles[Math.Max(0, profile.SelectedIndex)];
            var p = old with { Id = add ? Guid.NewGuid().ToString("N") : old.Id, Name = profileName.Text?.Trim() ?? "", Mode = (ActivationMode)mode.SelectedIndex, WindowsKeys = ParseKeys(winKeys.Text), MacKeys = ParseKeys(macKeys.Text), WindowsStopKeys = ParseKeys(winStopKeys.Text), MacStopKeys = ParseKeys(macStopKeys.Text) };
            Save(settings with { Profiles = add ? [..settings.Profiles, p] : settings.Profiles.Select(v => v.Id == p.Id ? p : v).ToArray(), DefaultProfileId = p.Id }); notice.Text = "语音配置已保存。";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException) { notice.Text = "无法保存：" + ex.Message; }
    }
    private void DeleteProfile()
    {
        if (settings.Profiles.Length <= 1) { notice.Text = "至少保留一个语音配置。"; return; }
        var id = settings.Profiles[Math.Max(0, profile.SelectedIndex)].Id;
        if (settings.AppRules.Any(r => r.ProfileId == id)) { notice.Text = "该配置被应用规则引用，请先修改规则。"; return; }
        try { var remaining = settings.Profiles.Where(p => p.Id != id).ToArray(); Save(settings with { Profiles = remaining, DefaultProfileId = remaining[0].Id }); notice.Text = "语音配置已删除。"; }
        catch (IOException ex) { notice.Text = "删除失败：" + ex.Message; }
    }
    private void SaveBasic()
    {
        try
        {
            var next = settings with { HoldMs = (int)hold.Value, MovementDip = (int)(movement.Value ?? 8), ReleaseMouseOnActivate = releaseMouse.IsChecked == true, TriggerAnywhere = true, DefaultProfileId = settings.Profiles[Math.Max(0, profile.SelectedIndex)].Id };
            Save(next); notice.Text = "设置已保存；活动会话会先结束。";
        }
        catch (Exception ex) { notice.Text = "保存失败：" + ex.Message; }
    }
    private void ApplyJson()
    {
        try { Save(ConfigurationStore.Parse(editor.Text ?? "") with { Enabled = running }); notice.Text = "配置校验通过并已保存。"; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { notice.Text = "配置无效：" + ex.Message; }
    }
    private void Save(Settings next) { ConfigurationStore.Save(AppPaths.Settings, next); settings = next; host?.Configure(next); SyncControls(); }
    public async Task ToggleAsync()
    {
        if (busy || IsShuttingDown) return; busy = true; toggle.IsEnabled = false;
        CancelShortcutCapture();
        try
        {
            if (!running)
            {
                if (restartRequired && host != null)
                {
                    host.Changed -= statusHandler;
                    host.RecoveryRequested -= recoveryHandler;
                    var oldHost = host; await Task.Run(async () => await oldHost.DisposeAsync()); host = null; platform = null;
                }
                if (host == null)
                {
                    platform = createPlatform(); host = new EngineHost(platform, settings with { Enabled = false });
                    var startedHost = host;
                    statusHandler = s => OnStatus(startedHost, s);
                    recoveryHandler = message => OnRecoveryRequested(startedHost, message);
                    host.Changed += statusHandler;
                    host.RecoveryRequested += recoveryHandler;
                    await Task.Run(host.Start); notice.Text = platform.PermissionStatus;
                }
                if (host.IsFaulted) throw new InvalidOperationException("平台监听启动期间发生故障，请重新启用。");
                restartRequired = false; running = true; settings = settings with { Enabled = true }; host.Configure(settings);
            }
            else { autoResumeWanted = false; running = false; settings = settings with { Enabled = false }; host!.Configure(settings); }
            SyncControls();
        }
        catch (Exception ex)
        {
            if (host != null) { host.Changed -= statusHandler; host.RecoveryRequested -= recoveryHandler; await host.DisposeAsync(); host = null; platform = null; }
            running = false; notice.Text = "无法启用：" + ex.Message;
        }
        finally { busy = false; toggle.IsEnabled = true; SyncSwitch(); }
    }
    private void SyncSwitch()
    {
        toggle.IsChecked = running;
        toggleLabel.Text = running ? "已启用" : restartRequired ? "待恢复" : "已暂停";
        toggleLabel.Foreground = Brush.Parse(running ? "#027A48" : "#667085");
    }
    public async Task EnableOnStartupAsync()
    {
        if (running || busy || IsShuttingDown) return;
        state.Text = "正在启动全局长按监听…";
        await ToggleAsync();
        if (!running) state.Text = "识别未启用，请查看提示";
    }
    private void OnRecoveryRequested(EngineHost source, string message) => Dispatcher.UIThread.Post(async () =>
    {
        if (IsShuttingDown || !ReferenceEquals(host, source) || !autoResumeWanted || autoRecoveryPending) return;
        autoRecoveryPending = true;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(500 * (attempt + 1));
                if (IsShuttingDown || !autoResumeWanted || running) return;
                if (busy) continue;
                notice.Text = message + " · 正在自动恢复…";
                await ToggleAsync();
                if (running) { autoResumeWanted = false; notice.Text = "监听已自动恢复 · 可以继续长按说话"; return; }
            }
            notice.Text = "自动恢复未成功，请点击重新启用；诊断报告保留了故障原因。";
        }
        finally { autoRecoveryPending = false; }
    });
    private void OnStatus(EngineHost source, Status s) => Dispatcher.UIThread.Post(() =>
    {
        if (IsShuttingDown || !ReferenceEquals(host, source)) return;
        if (s.Code is "engine_failed" or "platform_failed")
        {
            autoResumeWanted = autoResumeWanted || running;
            restartRequired = true; running = false; settings = settings with { Enabled = false };
            SyncSwitch(); notice.Text = s.Message;
        }
        state.Text = s.Message; detail.Text = settings.TriggerAnywhere ? "全局长按 · 不检测输入框 · 松开鼠标结束" : s.Target == null ? (settings.AllowUnconfirmedTargets ? "宽松模式：未知位置允许触发，明确不可输入的位置拒绝。" : "严格模式：只在确认可编辑的输入框内触发。") : $"控件：{s.Target.ControlType} · {s.Code}";
        if (s.Code is "focus_changed" or "probe_timeout" or "activation_failed") notice.Text = s.Message;
        if (s.ObservedTarget != null) detail.Text += $" · 探测：{s.ObservedTarget.Reason}";
        if (s.Code is "detecting" or "hover_ready") return;
        diagnosticLog.Add(s);
        logs.Enqueue($"{DateTime.Now:HH:mm:ss}  {s.Code}  {s.State}" + (s.ObservedTarget == null ? "" : $"  probe={s.ObservedTarget.Reason}")); while (logs.Count > 80) logs.Dequeue(); diagnostics.Text = string.Join(Environment.NewLine, logs);
    });
    public string DiagnosticJson() => JsonSerializer.Serialize(diagnosticLog.Report(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown", System.Runtime.InteropServices.RuntimeInformation.OSDescription, settings), ConfigurationStore.Json);
    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "导出诊断报告", SuggestedFileName = "holdspeak-diagnostics.json", DefaultExtension = "json" });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) { stream.SetLength(0); stream.Position = 0; }
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(DiagnosticJson()); notice.Text = "诊断报告已导出。";
        }
        catch (Exception ex) { notice.Text = "导出诊断失败：" + ex.Message; }
    }
    private async Task ImportAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "导入配置", AllowMultiple = false, FileTypeFilter = [new("JSON") { Patterns = ["*.json"] }] });
            if (files.Count == 0) return; await using var stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream);
            editor.Text = await reader.ReadToEndAsync(); ConfigurationStore.Parse(editor.Text); notice.Text = "导入校验通过，点击“校验并应用”保存。";
        }
        catch (Exception ex) { notice.Text = "导入失败：" + ex.Message; }
    }
    private async Task ExportAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "导出配置", SuggestedFileName = "holdspeak-settings.json", DefaultExtension = "json" });
            if (file == null) return; await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) { stream.SetLength(0); stream.Position = 0; }
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(JsonSerializer.Serialize(settings with { Enabled = false }, ConfigurationStore.Json)); notice.Text = "配置已导出。";
        }
        catch (Exception ex) { notice.Text = "导出失败：" + ex.Message; }
    }
    public async Task ShutdownAsync()
    {
        if (IsShuttingDown) return; IsShuttingDown = true; autoResumeWanted = false;
        CancelShortcutCapture();
        if (host != null) { host.Changed -= statusHandler; host.RecoveryRequested -= recoveryHandler; await host.DisposeAsync(); host = null; }
    }
}
