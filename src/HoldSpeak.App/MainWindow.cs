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
    private readonly Func<InputMethodInfo[]> listInputMethods;
    private readonly Func<InputMethodInfo?> currentInputMethod;
    private InputMethodInfo[] inputMethods = [];
    private string? editingProfileId;
    private bool readingInputMethod;
    private readonly IAutoStart autoStart;
    private readonly IosSwitch autoStartSwitch = new();
    private readonly TextBlock autoStartHint = Label(L.T("登录后在托盘运行，自动启用长按说话。切换后立即生效。"), 12, "#667085");
    private readonly DispatcherTimer inputMethodTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ComboBox shortcutSelection = new() { ItemsSource = new[] { L.T("固定使用一个快捷键"), L.T("跟随当前输入法") }, MinWidth = 200 };
    private readonly TextBlock currentMethodLabel = Label(L.T("固定使用所选快捷键"), 12, "#667085");
    private readonly StackPanel fixedRoute = new() { Spacing = 6 };
    private readonly StackPanel automaticRoute = new() { Spacing = 6 };
    private readonly StackPanel stopShortcut = new() { Spacing = 6 };
    private readonly StackPanel otherStopShortcut = new() { Spacing = 6 };
    private readonly StackPanel profileManagement = new() { Spacing = 8 };
    private string? selectedInputMethodId;
    private readonly ComboBox inputMethodPicker = new() { PlaceholderText = L.T("编辑输入法"), HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<InputMethodInfo>((input, _) => new TextBlock { Text = input == null ? "" : InputMethodName(input), TextTrimming = TextTrimming.CharacterEllipsis }) };
    private readonly Button refreshInputMethods = new() { Content = L.T("刷新输入法") };
    private readonly TextBlock state = Label(L.T("尚未启用"), 17, "#1677FF", FontWeight.SemiBold);
    private readonly TextBlock detail = Label(L.T("全局长按模式：任何位置按住左键即可触发语音。"), 13, "#667085");
    private readonly TextBlock notice = Label("", 13, "#AD6800");
    private readonly IosSwitch toggle = new();
    private readonly TextBlock toggleLabel = Label(L.T("已暂停"), 13, "#667085");
    private readonly ComboBox profile = new() { MinWidth = 240 };
    private readonly Slider hold = new() { Minimum = 300, Maximum = 1500, TickFrequency = 50, IsSnapToTickEnabled = true, Height = 40, MinHeight = 40 };
    private readonly TextBlock holdValue = Label("600 ms", 14, "#E4EBF5");
    private readonly NumericUpDown movement = new() { Minimum = 2, Maximum = 40, Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox releaseMouse = new() { Content = L.T("开始时结束鼠标拖选（推荐）") };
    private readonly TextBox editor = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, monospace"), Height = 240 };
    private readonly TextBox profileName = new() { Watermark = L.T("语音软件名称") };
    private readonly TextBox winKeys = new() { Watermark = "LCtrl, LWin" };
    private readonly TextBox macKeys = new() { Watermark = "Control, Command" };
    private readonly TextBox winStopKeys = new() { Watermark = L.T("仅分别开始／结束模式需要") };
    private readonly TextBox macStopKeys = new() { Watermark = L.T("仅分别开始／结束模式需要") };
    private readonly ComboBox mode = new() { ItemsSource = new[] { L.T("保持：按住开始，松开结束"), L.T("切换：开始和结束各按一次"), L.T("分别发送开始和结束快捷键") }, SelectedIndex = 0 };
    private readonly StackPanel ruleRows = new() { Spacing = 8 };
    private readonly TextBlock ruleCount = Label(L.T("尚未添加黑名单"), 13, "#667085");
    private readonly TextBox ruleApp = new() { Watermark = "Windows: chrome.exe / Mac: com.google.Chrome" };
    private readonly ComboBox runningApps = new() { PlaceholderText = L.T("选择当前运行的应用"), MinWidth = 260 };
    private readonly Button refreshApps = new() { Content = L.T("刷新应用列表") };
    private readonly Button inspectTarget = new() { Content = L.T("3 秒后检查鼠标处输入框") };
    private readonly TextBox diagnostics = new() { AcceptsReturn = true, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
    private readonly Queue<string> logs = new();
    private readonly DiagnosticLog diagnosticLog = new();
    private ShortcutCapture? shortcutCapture;
    private TextBox? captureTarget;
    private readonly DispatcherTimer captureTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    public bool IsShuttingDown { get; private set; }
    public MainWindow() : this(Program.CreatePlatform) { }
    public MainWindow(Func<IDesktopPlatform> createPlatform, Func<ApplicationOption[]>? listApplications = null,
        Func<InputMethodInfo[]>? listInputMethods = null, Func<InputMethodInfo?>? currentInputMethod = null, IAutoStart? autoStart = null)
    {
        this.createPlatform = createPlatform;
        this.listApplications = listApplications ?? Program.ListApplications;
        this.listInputMethods = listInputMethods ?? Program.ListInputMethods;
        this.currentInputMethod = currentInputMethod ?? Program.CurrentInputMethod;
        this.autoStart = autoStart ?? AutoStart.Create();
        Title = L.T("左键说 · 按住左键说话"); Width = 1040; Height = 800; MinWidth = 900; MinHeight = 700;
        Background = Brush.Parse("#F5F7FA"); FontFamily = new FontFamily("Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic, PingFang SC, Apple SD Gothic Neo, sans-serif"); FontSize = 14;
        Icon = Brand.Icon();
        try
        {
            settings = ConfigurationStore.Load(AppPaths.Settings, out bool recovered);
            if (recovered) notice.Text = L.T("主配置损坏，已加载上一次保存的备份。原文件保留；确认设置后再保存。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { notice.Text = L.T("配置加载失败，已使用默认值：") + L.Message(ex.Message); }
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
            bool problem = new[] { "失败", "无效", "无法", "超时", "损坏", "failed", "could not", "invalid", "timed out", "damaged", "失敗", "無効", "できません", "タイムアウト", "破損", "실패", "잘못", "초과", "손상" }.Any(s => notice.Text?.Contains(s, StringComparison.OrdinalIgnoreCase) == true);
            bool success = new[] { "已保存", "已自动保存", "已自动恢复", "saved", "recovered", "保存しました", "復旧しました", "저장했습니다", "복구됨" }.Any(s => notice.Text?.Contains(s, StringComparison.OrdinalIgnoreCase) == true);
            notice.Foreground = Brush.Parse(problem ? "#AD6800" : success ? "#027A48" : "#667085");
        };
        Content = BuildContent(); SyncControls();
        toggle.Click += async (_, _) => await ToggleAsync();
        Avalonia.Automation.AutomationProperties.SetName(autoStartSwitch, L.T("开机自启动"));
        ToolTip.SetTip(autoStartSwitch, L.T("登录电脑后自动启动左键说"));
        autoStartSwitch.Click += (_, _) =>
        {
            try { this.autoStart.SetEnabled(autoStartSwitch.IsChecked == true); notice.Text = autoStartSwitch.IsChecked == true ? L.T("开机自启动已开启，下次登录后在托盘运行。") : L.T("开机自启动已关闭。"); }
            catch (Exception ex) { notice.Text = L.T("开机自启动设置失败：") + L.Message(ex.Message); }
            RefreshAutoStart();
        };
        RefreshAutoStart();
        Activated += (_, _) => RefreshAutoStart();
        profile.SelectionChanged += (_, _) =>
        {
            if (syncingControls || profile.SelectedIndex < 0 || settings.ShortcutSelection != ShortcutSelectionMode.Fixed) return;
            try { Save(settings with { DefaultProfileId = settings.Profiles[profile.SelectedIndex].Id }); }
            catch (Exception ex) { notice.Text = L.T("选择保存失败：") + L.Message(ex.Message); SyncControls(); }
        };
        inputMethodPicker.SelectionChanged += (_, _) =>
        {
            if (syncingControls || settings.ShortcutSelection != ShortcutSelectionMode.Automatic) return;
            selectedInputMethodId = (inputMethodPicker.SelectedItem as InputMethodInfo)?.Id; SyncProfileFields();
        };
        mode.SelectionChanged += (_, _) => UpdateShortcutVisibility();
        shortcutSelection.SelectionChanged += async (_, _) =>
        {
            if (syncingControls || shortcutSelection.SelectedIndex < 0) return;
            try { Save(settings with { ShortcutSelection = (ShortcutSelectionMode)shortcutSelection.SelectedIndex }); await UpdateCurrentInputMethodAsync(); }
            catch (Exception ex) { notice.Text = L.T("模式保存失败：") + L.Message(ex.Message); }
        };
        refreshInputMethods.Click += async (_, _) => await RefreshInputMethodsAsync();
        Opened += async (_, _) => { await RefreshInputMethodsAsync(); inputMethodTimer.Start(); };
        Closed += (_, _) => inputMethodTimer.Stop();
        inputMethodTimer.Tick += async (_, _) => { if (IsVisible) await UpdateCurrentInputMethodAsync(); };
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
                notice.Text = L.F("长按时间 {0} ms 已自动保存并生效。", (int)hold.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { notice.Text = L.T("长按时间保存失败：") + L.Message(ex.Message); }
        };
        captureTimer.Tick += (_, _) => CancelShortcutCapture(L.T("快捷键录入超时，原设置保留。"));
        Deactivated += (_, _) => CancelShortcutCapture(L.T("窗口失去焦点，快捷键录入已取消。"));
    }
    private Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(24, 20) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(Brand.Mark(40));
        var title = new StackPanel { Spacing = 2 }; title.Children.Add(Label(L.T("左键说"), 24, "#101828", FontWeight.Bold)); title.Children.Add(Label(L.T(Brand.Tagline), 13, "#667085")); Grid.SetColumn(title, 1); header.Children.Add(title);
        var badge = new Border { Background = Brush.Parse("#ECFDF3"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5), VerticalAlignment = VerticalAlignment.Center, Child = Label(L.T("●  本地运行"), 12, "#027A48") }; Grid.SetColumn(badge, 2); header.Children.Add(badge); root.Children.Add(header);
        var status = new StackPanel { Spacing = 6 }; status.Children.Add(state); status.Children.Add(detail);
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 }; statusGrid.Children.Add(status);
        var switchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        toggleLabel.VerticalAlignment = VerticalAlignment.Center; switchRow.Children.Add(toggleLabel); switchRow.Children.Add(toggle);
        Grid.SetColumn(switchRow, 1); statusGrid.Children.Add(switchRow);
        var statusCard = Card(statusGrid); statusCard.Padding = new Thickness(18, 14); statusCard.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(statusCard, 1); root.Children.Add(statusCard);
        var hints = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 14) };
        hints.Children.Add(Label(L.T("按住左键  →  开始说话  →  松开结束"), 12, "#667085")); hints.Children.Add(notice); Grid.SetRow(hints, 2); root.Children.Add(hints);
        var basics = new StackPanel { Spacing = 8 };
        basics.Children.Add(Label(L.T("鼠标与启动"), 18, "#101828", FontWeight.SemiBold));
        basics.Children.Add(Label(L.T("界面语言"), 13, "#344054", FontWeight.SemiBold));
        var languagePicker = new ComboBox { Name = "LanguagePicker", ItemsSource = L.Languages,
            SelectedItem = L.Languages.Single(l => l.Code == L.CurrentLanguage), MinWidth = 180 };
        Avalonia.Automation.AutomationProperties.SetName(languagePicker, L.T("界面语言"));
        var languageHint = Label(L.T("语言选择会自动保存，重启软件后生效。"), 12, "#667085");
        languagePicker.SelectionChanged += (_, _) =>
        {
            if (languagePicker.SelectedItem is not LanguageOption language) return;
            try
            {
                L.SavePreference(language.Code);
                languageHint.Text = L.Translate("语言已保存，请完全退出软件后重新打开。", language.Code);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { languageHint.Text = L.T("语言保存失败：") + L.Message(ex.Message); }
        };
        basics.Children.Add(languagePicker); basics.Children.Add(languageHint);
        var startupRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 8) };
        var startupText = new StackPanel { Spacing = 4 }; startupText.Children.Add(Label(L.T("开机自启动"), 13, "#344054", FontWeight.SemiBold)); startupText.Children.Add(autoStartHint); startupRow.Children.Add(startupText);
        Grid.SetColumn(autoStartSwitch, 1); startupRow.Children.Add(autoStartSwitch); basics.Children.Add(startupRow);
        var holdHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        holdHeader.Children.Add(Label(L.T("长按时间"), 13, "#344054", FontWeight.SemiBold));
        var timeBadge = new Border { Background = Brush.Parse("#E6F4FF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3), Child = holdValue }; Grid.SetColumn(timeBadge, 1); holdHeader.Children.Add(timeBadge); basics.Children.Add(holdHeader);
        basics.Children.Add(hold);
        basics.Children.Add(Label(L.T("调整后自动保存。时间越短，触发越快；也更容易误触。"), 12, "#667085"));
        var drag = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        var dragText = new StackPanel { Spacing = 3 }; dragText.Children.Add(Label(L.T("拖动取消距离"), 13, "#344054", FontWeight.SemiBold)); dragText.Children.Add(Label(L.T("按住后移动超过此距离，保留正常拖选。"), 12, "#667085")); drag.Children.Add(dragText); Grid.SetColumn(movement, 1); drag.Children.Add(movement); basics.Children.Add(drag);
        basics.Children.Add(releaseMouse);
        var tip = new StackPanel { Spacing = 4 }; tip.Children.Add(Label(L.T("各应用通用"), 13, "#1677FF", FontWeight.SemiBold)); tip.Children.Add(Label(L.T("文字会填入当前有光标的位置。录音与文字转换由所选语音软件完成。"), 12, "#667085"));
        basics.Children.Add(new Border { Background = Brush.Parse("#F5F9FF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = tip });
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        var save = new Button { Content = L.T("保存设置") }; save.Classes.Add("accent"); save.Click += (_, _) => SaveBasic(); saveRow.Children.Add(save); var saveHint = Label(L.T("拖动设置需点击保存"), 12, "#667085"); saveHint.VerticalAlignment = VerticalAlignment.Center; saveRow.Children.Add(saveHint);
        var regular = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; regular.Children.Add(new ScrollViewer { Content = basics, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        saveRow.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(saveRow, 1); regular.Children.Add(saveRow);
        var profiles = new StackPanel { Spacing = 8 };
        profiles.Children.Add(Label(L.T("语音快捷键"), 20, "#101828", FontWeight.SemiBold));
        var shortcutRoute = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), ColumnSpacing = 16 };
        var selection = new StackPanel { Spacing = 6 }; selection.Children.Add(Label(L.T("使用方式"), 13, "#344054")); shortcutSelection.MinWidth = 0; shortcutSelection.HorizontalAlignment = HorizontalAlignment.Stretch; selection.Children.Add(shortcutSelection); shortcutRoute.Children.Add(selection);
        var routeTarget = new Grid(); routeTarget.Children.Add(fixedRoute); routeTarget.Children.Add(automaticRoute); Grid.SetColumn(routeTarget, 1); shortcutRoute.Children.Add(routeTarget); profiles.Children.Add(shortcutRoute);
        fixedRoute.Children.Add(Label(L.T("使用的语音软件"), 13, "#344054")); profile.MinWidth = 0; profile.HorizontalAlignment = HorizontalAlignment.Stretch; fixedRoute.Children.Add(profile);
        ToolTip.SetTip(profile, L.T("选择后立即生效，下方编辑当前使用的快捷键。"));
        automaticRoute.Children.Add(Label(L.T("编辑输入法的快捷键"), 13, "#344054"));
        var methodRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; methodRow.Children.Add(inputMethodPicker); Grid.SetColumn(refreshInputMethods, 1); methodRow.Children.Add(refreshInputMethods); automaticRoute.Children.Add(methodRow);
        ToolTip.SetTip(inputMethodPicker, L.T("这里只选择要编辑的输入法，不切换系统输入法；保存后单独生效。"));
        profiles.Children.Add(currentMethodLabel);
        profiles.Children.Add(Label(L.T("原语音软件的启动方式"), 13, "#344054")); profiles.Children.Add(mode);
        profiles.Children.Add(Label(L.T("快捷键"), 13, "#344054", FontWeight.SemiBold));
        bool macPlatform = OperatingSystem.IsMacOS();
        profiles.Children.Add(ShortcutRow(macPlatform ? macKeys : winKeys, macPlatform, L.T("录入快捷键")));
        stopShortcut.Children.Add(Label(L.T("结束快捷键"), 13, "#344054")); stopShortcut.Children.Add(ShortcutRow(macPlatform ? macStopKeys : winStopKeys, macPlatform, L.T("录入结束键"))); profiles.Children.Add(stopShortcut);
        profiles.Children.Add(Label(L.T("快捷键需与原语音软件一致。录入会暂停长按，按键松开后保存。"), 12, "#667085"));
        var more = new StackPanel { Spacing = 8 };
        profileManagement.Children.Add(Label(L.T("配置名称"), 13, "#344054")); profileManagement.Children.Add(profileName);
        var managementButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var newProfile = new Button { Content = L.T("另存为新配置") }; newProfile.Click += (_, _) => SaveProfile(true); managementButtons.Children.Add(newProfile);
        var deleteProfile = new Button { Content = L.T("删除当前配置") }; deleteProfile.Click += (_, _) => DeleteProfile(); managementButtons.Children.Add(deleteProfile); profileManagement.Children.Add(managementButtons); more.Children.Add(profileManagement);
        more.Children.Add(Label(macPlatform ? L.T("Windows 平台快捷键") : L.T("Mac 平台快捷键"), 13, "#344054"));
        more.Children.Add(ShortcutRow(macPlatform ? winKeys : macKeys, !macPlatform, macPlatform ? L.T("录入 Windows 开始") : L.T("录入 Mac 开始")));
        otherStopShortcut.Children.Add(Label(L.T("另一平台的结束快捷键"), 13, "#344054")); otherStopShortcut.Children.Add(ShortcutRow(macPlatform ? winStopKeys : macStopKeys, !macPlatform, macPlatform ? L.T("录入 Windows 结束") : L.T("录入 Mac 结束"))); more.Children.Add(otherStopShortcut);
        more.Children.Add(Label(L.T("手动输入用英文逗号分隔，如 LCtrl, LWin。Mac 字母按美式物理键位发送，暂不支持 Fn。"), 12, "#667085"));
        profiles.Children.Add(new Expander { Header = L.T("更多设置"), Content = more, HorizontalAlignment = HorizontalAlignment.Stretch });
        var profileFooter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        var saveProfile = new Button { Content = L.T("保存语音配置") }; saveProfile.Classes.Add("accent"); saveProfile.Click += (_, _) => SaveProfile(false); profileFooter.Children.Add(saveProfile);
        profileFooter.Children.Add(Label(L.T("快捷键修改后点击保存"), 12, "#667085"));
        var shortcutsPage = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        shortcutsPage.Children.Add(new ScrollViewer { Content = profiles, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }); Grid.SetRow(profileFooter, 1); shortcutsPage.Children.Add(profileFooter);
        var rules = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 16 };
        var rulesHeader = new StackPanel { Spacing = 6 };
        rulesHeader.Children.Add(Label(L.T("游戏与应用黑名单"), 20, "#101828", FontWeight.SemiBold));
        rulesHeader.Children.Add(Label(L.T("这些程序在前台时，长按鼠标不会触发语音。移除后立即恢复使用。"), 13, "#667085"));
        rulesHeader.Children.Add(ruleCount); rules.Children.Add(rulesHeader);
        var ruleScroll = new ScrollViewer { Content = ruleRows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(ruleScroll, 1); rules.Children.Add(ruleScroll);
        var addArea = new StackPanel { Spacing = 10 };
        addArea.Children.Add(Label(L.T("添加程序"), 14, "#344054", FontWeight.SemiBold));
        var applicationPicker = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        runningApps.MinWidth = 0; runningApps.HorizontalAlignment = HorizontalAlignment.Stretch;
        applicationPicker.Children.Add(runningApps); Grid.SetColumn(refreshApps, 1); applicationPicker.Children.Add(refreshApps);
        var blacklist = new Button { Content = L.T("加入黑名单") }; blacklist.Classes.Add("accent");
        blacklist.Click += (_, _) =>
        {
            if (runningApps.SelectedItem is not ApplicationOption app) { notice.Text = L.T("请先从运行中的程序列表选择一个应用。"); return; }
            SaveRule(app.AppId);
        };
        Grid.SetColumn(blacklist, 2); applicationPicker.Children.Add(blacklist); addArea.Children.Add(applicationPicker);
        addArea.Children.Add(Label(OperatingSystem.IsWindows() ? L.T("按程序名匹配全部同名实例，升级或换目录后仍有效；程序改名时需重新添加。") : L.T("按应用 Bundle ID 匹配，标识不变时升级后仍有效。"), 12, "#667085"));
        var manual = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        manual.Children.Add(ruleApp); var manualAdd = new Button { Content = L.T("手动加入") }; manualAdd.Click += (_, _) => SaveRule(ruleApp.Text ?? ""); Grid.SetColumn(manualAdd, 1); manual.Children.Add(manualAdd);
        addArea.Children.Add(new Expander { Header = L.T("找不到程序？手动填写程序名"), Content = manual, HorizontalAlignment = HorizontalAlignment.Stretch });
        Grid.SetRow(addArea, 2); rules.Children.Add(addArea);
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(Label(L.T("多软件配置与应用规则"), 18, "#F0F5FC", FontWeight.SemiBold));
        advanced.Children.Add(Label(L.T("在 JSON 中添加配置；Windows 使用进程名，Mac 使用 Bundle ID。\n未配置的平台不会触发，请按原语音软件设置填写快捷键。"), 13, "#9CAABC"));
        advanced.Children.Add(editor);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = L.T("校验并应用") }; apply.Click += (_, _) => ApplyJson(); buttons.Children.Add(apply);
        var import = new Button { Content = L.T("导入") }; import.Click += async (_, _) => await ImportAsync(); buttons.Children.Add(import);
        var export = new Button { Content = L.T("导出") }; export.Click += async (_, _) => await ExportAsync(); buttons.Children.Add(export); advanced.Children.Add(buttons);
        var checks = new StackPanel { Spacing = 12 };
        checks.Children.Add(Label(L.T("本地诊断"), 18, "#F0F5FC", FontWeight.SemiBold));
        checks.Children.Add(Label(L.T("日志只显示状态与控件类型，不记录输入内容。\n这里的“快捷键保持中”表示已发送按键，不代表已确认录音。"), 13, "#9CAABC"));
        checks.Children.Add(diagnostics);
        var exportDiagnostics = new Button { Content = L.T("导出诊断报告") }; exportDiagnostics.Click += async (_, _) => await ExportDiagnosticsAsync(); checks.Children.Add(exportDiagnostics);
        checks.Children.Add(Label(L.T("报告仅包含最近 80 条状态、应用标识及控件探测原因；不含正文、网址或窗口标题。"), 12, "#9CAABC"));
        inspectTarget.Click += async (_, _) => await InspectTargetAsync(); checks.Children.Add(inspectTarget);
        checks.Children.Add(Label(L.T("点击检查后，在 3 秒内点击目标输入框并把鼠标停在里面。\n只查询控件属性，不发送语音快捷键。"), 12, "#9CAABC"));
        var test = new TextBox { Watermark = L.T("可在这里测试点击与拖动；全局模式下，长按也会触发语音"), MinHeight = 70, AcceptsReturn = true }; checks.Children.Add(test);
        var exit = new Button { Content = L.T("完全退出") }; exit.Click += async (_, _) => { await ShutdownAsync(); (Avalonia.Application.Current!.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown(); }; checks.Children.Add(exit);
        if (OperatingSystem.IsMacOS())
        {
            var permissions = new Button { Content = L.T("打开 Mac 辅助功能设置") };
            permissions.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility" }, UseShellExecute = false });
            checks.Children.Add(permissions);
        }
        Control Page(Control content) => new ScrollViewer { Content = Card(content), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Control Nav(string icon, string name) => new TextBlock { Text = $"{icon}    {name}", Width = 136, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(8, 10), Foreground = Brush.Parse("#344054") };
        var tabs = new TabControl { TabStripPlacement = Dock.Left, ItemsSource = new[] { new TabItem { Header = Nav("◉", L.T("常规设置")), Content = Card(regular) }, new TabItem { Header = Nav("♫", L.T("语音快捷键")), Content = Card(shortcutsPage) }, new TabItem { Header = Nav("▦", L.T("应用规则")), Content = Card(rules) }, new TabItem { Header = Nav("≡", L.T("高级配置")), Content = Page(advanced) }, new TabItem { Header = Nav("⌁", L.T("诊断与帮助")), Content = Page(checks) } } };
        tabs.SelectionChanged += async (_, e) =>
        {
            if (!ReferenceEquals(e.Source, tabs)) return;
            CancelShortcutCapture(L.T("切换页面，快捷键录入已取消。"));
            if (tabs.SelectedIndex == 2) await RefreshApplicationsAsync();
        };
        Grid.SetRow(tabs, 3); root.Children.Add(tabs);
        var footer = Label(L.F("左键说 · HoldSpeak {0}     |     © 2026 HoldSpeak contributors · MIT", typeof(MainWindow).Assembly.GetName().Version?.ToString(3)), 11, "#98A2B3"); footer.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(footer, 4); root.Children.Add(footer);
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
    public async Task RefreshInputMethodsAsync()
    {
        if (!refreshInputMethods.IsEnabled || IsShuttingDown) return;
        refreshInputMethods.IsEnabled = false;
        try
        {
            var found = await Task.Run(listInputMethods);
            if (IsShuttingDown) return;
            inputMethods = found.DistinctBy(i => i.Id).ToArray();
            var profiles = settings.Profiles.ToList(); var bindings = settings.InputMethodBindings.ToList();
            foreach (var input in inputMethods)
            {
                if (bindings.Any(b => b.InputMethodId == input.Id) || InputMethodPresets.Create(input) is not { } preset) continue;
                var existing = profiles.FirstOrDefault(p => p.Id == preset.Id || (input.Preset == "wechat" && p.Name.Contains("微信")) || (input.Preset == "sogou" && p.Name.Contains("搜狗")));
                if (existing == null) { existing = preset; profiles.Add(existing); }
                bindings.Add(new() { InputMethodId = input.Id, Name = input.Name, ProfileId = existing.Id });
            }
            if (profiles.Count != settings.Profiles.Length || bindings.Count != settings.InputMethodBindings.Length)
                Save(settings with { Profiles = profiles.ToArray(), InputMethodBindings = bindings.ToArray() });
            else SyncControls();
            await UpdateCurrentInputMethodAsync();
        }
        catch (Exception ex) { notice.Text = L.T("输入法刷新失败：") + L.Message(ex.Message); }
        finally { refreshInputMethods.IsEnabled = true; }
    }
    private async Task UpdateCurrentInputMethodAsync()
    {
        if (readingInputMethod || IsShuttingDown) return;
        if (settings.ShortcutSelection == ShortcutSelectionMode.Fixed) { currentMethodLabel.Text = FixedShortcutSummary(); return; }
        readingInputMethod = true;
        try
        {
            var method = await Task.Run(currentInputMethod);
            if (IsShuttingDown || settings.ShortcutSelection != ShortcutSelectionMode.Automatic) return;
            var binding = settings.InputMethodBindings.FirstOrDefault(b => b.InputMethodId == method?.Id);
            if (method == null) currentMethodLabel.Text = L.T("当前输入法无法确认，本次不会发送快捷键");
            else if (binding == null) currentMethodLabel.Text = L.F("当前：{0} · 请在此页设置该输入法的快捷键", InputMethodName(method));
            else
            {
                var selected = settings.Profiles.First(p => p.Id == binding.ProfileId);
                var keys = OperatingSystem.IsWindows() ? selected.WindowsKeys : selected.MacKeys;
                var protocol = selected.Mode == ActivationMode.Hold ? L.T("长按") : selected.Mode == ActivationMode.Toggle ? L.T("按一下开始，再按一下结束") : L.T("分别开始／结束");
                currentMethodLabel.Text = L.F("当前：{0} → {1} · {2}", InputMethodName(method), string.Join(" + ", keys), protocol);
            }
        }
        catch { currentMethodLabel.Text = L.T("当前输入法读取失败，本次不会发送快捷键"); }
        finally { readingInputMethod = false; }
    }
    private void SyncControls()
    {
        syncingControls = true;
        try
        {
        profile.ItemsSource = settings.Profiles.Select(p => p.Name switch {
            "微信输入法 · Ctrl + Win 长按" => L.T("微信输入法"), "搜狗输入法 · F2 切换" => L.T("搜狗输入法"),
            "微信输入法" when p.Id == "ime-wechat" => L.T("微信输入法"), "搜狗输入法" when p.Id == "ime-sogou" => L.T("搜狗输入法"),
            "Ctrl + Win 长按" => L.T("Ctrl + Win 长按"), "语音入口 · 请配置 Mac 快捷键" => L.T("语音入口 · 请配置 Mac 快捷键"), _ => p.Name }).ToArray(); profile.SelectedIndex = Array.FindIndex(settings.Profiles, p => p.Id == settings.DefaultProfileId);
        shortcutSelection.SelectedIndex = (int)settings.ShortcutSelection;
        profile.IsEnabled = settings.ShortcutSelection == ShortcutSelectionMode.Fixed;
        var methods = inputMethods.Concat(settings.InputMethodBindings.Where(b => inputMethods.All(i => i.Id != b.InputMethodId)).Select(b => new InputMethodInfo(b.InputMethodId, b.Name))).ToArray();
        selectedInputMethodId = methods.FirstOrDefault(i => i.Id == selectedInputMethodId)?.Id ?? methods.FirstOrDefault()?.Id;
        inputMethodPicker.ItemsSource = methods;
        inputMethodPicker.SelectedItem = methods.FirstOrDefault(i => i.Id == selectedInputMethodId);
        if (settings.ShortcutSelection == ShortcutSelectionMode.Fixed) currentMethodLabel.Text = FixedShortcutSummary();
        hold.Value = settings.HoldMs; holdValue.Text = $"{settings.HoldMs} ms"; movement.Value = settings.MovementDip; releaseMouse.IsChecked = settings.ReleaseMouseOnActivate;
        editor.Text = JsonSerializer.Serialize(settings, ConfigurationStore.Json);
        SyncProfileFields();
        RenderRuleRows();
        }
        finally { syncingControls = false; }
    }
    private void RenderRuleRows()
    {
        ruleRows.Children.Clear();
        int blocked = settings.AppRules.Count(r => !r.Enabled);
        ruleCount.Text = L.F("已禁用 {0} 个程序 · 修改后自动保存", blocked);
        if (settings.AppRules.Length == 0)
        {
            ruleRows.Children.Add(new Border { Background = Brush.Parse("#F8FAFC"), CornerRadius = new CornerRadius(10), Padding = new Thickness(24), Child = new StackPanel { Spacing = 8, Children = {
                Label(L.T("还没有添加黑名单"), 16, "#344054", FontWeight.SemiBold), Label(L.T("在下方选择正在运行的游戏或应用，点击“加入黑名单”。"), 13, "#667085") } } });
            return;
        }
        foreach (var rule in settings.AppRules.OrderBy(r => r.Enabled).ThenBy(r => r.AppId, StringComparer.OrdinalIgnoreCase))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            var text = new StackPanel { Spacing = 4 }; text.Children.Add(Label(rule.AppId, 14, "#101828", FontWeight.SemiBold));
            text.Children.Add(Label(rule.Enabled ? L.T("未禁用 · 保留的旧规则") : L.T("已禁用 · 在此程序中不触发语音"), 12, rule.Enabled ? "#667085" : "#027A48")); row.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (rule.Enabled) { var disable = new Button { Content = L.T("禁用"), Tag = rule.AppId }; disable.Click += (_, _) => SaveRule(rule.AppId); actions.Children.Add(disable); }
            var remove = new Button { Content = L.T("移除"), Tag = rule.AppId }; Avalonia.Automation.AutomationProperties.SetName(remove, L.T("移除 ") + rule.AppId);
            remove.Click += (_, _) => DeleteRule(rule.AppId); actions.Children.Add(remove); Grid.SetColumn(actions, 1); row.Children.Add(actions);
            ruleRows.Children.Add(new Border { Tag = rule.AppId, BorderBrush = Brush.Parse("#EAECF0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Child = row });
        }
    }
    private async Task RefreshApplicationsAsync()
    {
        if (IsShuttingDown || !refreshApps.IsEnabled) return;
        refreshApps.IsEnabled = false;
        try
        {
            var selectedId = (runningApps.SelectedItem as ApplicationOption)?.AppId;
            var applications = (await Task.Run(listApplications)).DistinctBy(a => a.AppId, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (IsShuttingDown) return;
            runningApps.ItemsSource = applications;
            runningApps.SelectedIndex = Array.FindIndex(applications, a => a.AppId.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            notice.Text = applications.Length == 0 ? L.T("未找到可选应用，可以手动填写应用标识。") : L.F("已列出 {0} 个应用，选择后点击“加入黑名单”。", applications.Length);
        }
        catch (Exception ex) { if (!IsShuttingDown) notice.Text = L.T("读取应用列表失败：") + L.Message(ex.Message); }
        finally { refreshApps.IsEnabled = true; }
    }
    private void SaveRule(string appId)
    {
        try
        {
            var id = appId.Trim();
            var rule = new AppRule { AppId = id, Enabled = false };
            Save(settings with { AppRules = [..settings.AppRules.Where(r => !r.AppId.Equals(id, StringComparison.OrdinalIgnoreCase)), rule] }); notice.Text = L.F("{0} 已加入黑名单。", id);
            Dispatcher.UIThread.Post(() => ruleRows.Children.OfType<Border>().FirstOrDefault(r => Equals(r.Tag, id))?.BringIntoView());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { notice.Text = L.T("添加失败：") + L.Message(ex.Message); }
    }
    private void DeleteRule(string id)
    {
        if (!settings.AppRules.Any(r => r.AppId.Equals(id, StringComparison.OrdinalIgnoreCase))) { notice.Text = L.T("没有找到该应用规则。"); return; }
        try { Save(settings with { AppRules = settings.AppRules.Where(r => !r.AppId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray() }); notice.Text = L.F("{0} 已移除，可正常使用长按说话。", id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notice.Text = L.T("移除失败：") + L.Message(ex.Message); }
    }
    private async Task InspectTargetAsync()
    {
        autoResumeWanted = false;
        inspectTarget.IsEnabled = false;
        try
        {
            if (running) { settings = settings with { Enabled = false }; host!.Configure(settings); running = false; SyncSwitch(); }
            notice.Text = L.T("请在 3 秒内点击目标输入框，将鼠标留在输入框内。检查期间已暂停长按。");
            await Task.Delay(3000);
            using var inspectPlatform = Program.CreatePlatform(); var p = inspectPlatform.PointerPosition;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await inspectPlatform.Probe.ProbeAsync(p.X, p.Y, timeout.Token);
            diagnosticLog.Add(new(GestureState.Suspended, "inspect", "", result, result));
            notice.Text = L.F("检查结果：{0} · 应用：{1} · 控件：{2} · 原因：{3}", result.Editability, result.AppId, result.ControlType, result.Reason);
            ruleApp.Text = result.AppId;
            logs.Enqueue($"{DateTime.Now:HH:mm:ss}  inspect {result.Editability} {result.ControlType} {result.Reason}");
            while (logs.Count > 80) logs.Dequeue(); diagnostics.Text = string.Join(Environment.NewLine, logs);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) { notice.Text = L.T("检查失败：") + L.Message(ex.Message); }
        finally { inspectTarget.IsEnabled = true; }
    }
    private Control ShortcutRow(TextBox target, bool mac, string caption)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var record = new Button { Content = caption, IsEnabled = mac == OperatingSystem.IsMacOS() };
        record.Click += async (_, _) => await StartShortcutCaptureAsync(target, mac);
        row.Children.Add(target); Grid.SetColumn(record, 1); row.Children.Add(record);
        target.LostFocus += (_, _) => { if (ReferenceEquals(captureTarget, target)) CancelShortcutCapture(L.T("快捷键录入已取消，原设置保留。")); };
        target.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, e) =>
        {
            if (!ReferenceEquals(captureTarget, target) || shortcutCapture == null) return;
            e.Handled = true;
            if (e.Key == Avalonia.Input.Key.Escape || e.PhysicalKey == Avalonia.Input.PhysicalKey.Escape)
            { CancelShortcutCapture(L.T("快捷键录入已取消，原设置保留。")); return; }
            try { shortcutCapture.Press(e.Key, e.PhysicalKey, e.KeyModifiers); }
            catch (InvalidDataException ex) { CancelShortcutCapture(L.Message(ex.Message)); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        target.AddHandler(Avalonia.Input.InputElement.KeyUpEvent, (_, e) =>
        {
            if (!ReferenceEquals(captureTarget, target) || shortcutCapture == null) return;
            e.Handled = true;
            var keys = shortcutCapture.Release(e.Key, e.PhysicalKey);
            if (keys == null) return;
            CancelShortcutCapture(); target.Text = string.Join(", ", keys);
            notice.Text = L.T("快捷键已录入，请点击“保存语音配置”。长按仍处于暂停状态。");
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
        if (!target.Focus()) { CancelShortcutCapture(L.T("无法聚焦按键录入框，请重试。")); return; }
        captureTimer.Start();
        notice.Text = L.T("请先松开所有键，再按一次组合键并全部松开。Esc 取消；10 秒后超时。");
    }
    private void CancelShortcutCapture(string? message = null)
    {
        if (shortcutCapture == null) return;
        shortcutCapture = null; captureTimer.Stop();
        if (captureTarget != null) captureTarget.IsReadOnly = false;
        captureTarget = null;
        if (message != null) notice.Text = message;
    }
    private string FixedShortcutSummary()
    {
        var selected = settings.Profiles.First(p => p.Id == settings.DefaultProfileId);
        return L.F("当前固定使用：{0}", string.Join(" + ", selected.Keys(OperatingSystem.IsMacOS())));
    }
    private void UpdateShortcutVisibility()
    {
        bool automatic = settings.ShortcutSelection == ShortcutSelectionMode.Automatic;
        fixedRoute.IsVisible = !automatic; automaticRoute.IsVisible = automatic; profileManagement.IsVisible = !automatic;
        stopShortcut.IsVisible = otherStopShortcut.IsVisible = mode.SelectedIndex == (int)ActivationMode.Separate;
    }
    private static string InputMethodName(InputMethodInfo input) => input switch
    {
        { Preset: "wechat", Name: "微信输入法" } => L.T("微信输入法"),
        { Preset: "sogou", Name: "搜狗输入法" } => L.T("搜狗输入法"),
        _ => input.Name
    };
    private void SyncProfileFields()
    {
        CancelShortcutCapture();
        bool automatic = settings.ShortcutSelection == ShortcutSelectionMode.Automatic;
        editingProfileId = automatic ? settings.InputMethodBindings.FirstOrDefault(b => b.InputMethodId == selectedInputMethodId)?.ProfileId : settings.DefaultProfileId;
        var p = settings.Profiles.FirstOrDefault(p => p.Id == editingProfileId) ?? new VoiceProfile { Name = (inputMethodPicker.SelectedItem as InputMethodInfo)?.Name ?? "自定义", WindowsKeys = [], MacKeys = [] };
        profileName.Text = p.Name; mode.SelectedIndex = (int)p.Mode;
        winKeys.Text = string.Join(", ", p.WindowsKeys); macKeys.Text = string.Join(", ", p.MacKeys);
        winStopKeys.Text = string.Join(", ", p.WindowsStopKeys); macStopKeys.Text = string.Join(", ", p.MacStopKeys);
        UpdateShortcutVisibility();
    }
    private static Key[] ParseKeys(string? text) => KeyCatalog.Parse(text);
    private void SaveProfile(bool add)
    {
        try
        {
            bool automatic = settings.ShortcutSelection == ShortcutSelectionMode.Automatic;
            if (automatic && string.IsNullOrEmpty(selectedInputMethodId)) throw new InvalidDataException(L.T("请先选择要设置的输入法。"));
            var old = settings.Profiles.FirstOrDefault(p => p.Id == editingProfileId) ?? new VoiceProfile { Id = Guid.NewGuid().ToString("N") };
            bool creating = add || editingProfileId == null || (automatic && settings.InputMethodBindings.Any(b => b.ProfileId == old.Id && b.InputMethodId != selectedInputMethodId));
            var p = old with { Id = creating ? Guid.NewGuid().ToString("N") : old.Id, Name = profileName.Text?.Trim() ?? "", Mode = (ActivationMode)mode.SelectedIndex, WindowsKeys = ParseKeys(winKeys.Text), MacKeys = ParseKeys(macKeys.Text), WindowsStopKeys = ParseKeys(winStopKeys.Text), MacStopKeys = ParseKeys(macStopKeys.Text) };
            var bindings = settings.InputMethodBindings.ToList();
            if (automatic && inputMethodPicker.SelectedItem is InputMethodInfo { Id.Length: > 0 } input)
            {
                bindings.RemoveAll(b => b.InputMethodId == input.Id);
                bindings.Add(new() { InputMethodId = input.Id, Name = input.Name, ProfileId = p.Id });
            }
            editingProfileId = p.Id;
            Save(settings with { Profiles = creating ? [..settings.Profiles, p] : settings.Profiles.Select(v => v.Id == p.Id ? p : v).ToArray(), InputMethodBindings = bindings.ToArray(), DefaultProfileId = automatic ? settings.DefaultProfileId : p.Id }); notice.Text = L.T("语音配置已保存。");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException) { notice.Text = L.T("无法保存：") + L.Message(ex.Message); }
    }
    private void DeleteProfile()
    {
        if (settings.Profiles.Length <= 1) { notice.Text = L.T("至少保留一个语音配置。"); return; }
        var id = settings.DefaultProfileId;
        if (settings.AppRules.Any(r => r.ProfileId == id) || settings.InputMethodBindings.Any(b => b.ProfileId == id)) { notice.Text = L.T("该配置被输入法或应用规则引用，请先解除关联。"); return; }
        try { var remaining = settings.Profiles.Where(p => p.Id != id).ToArray(); Save(settings with { Profiles = remaining, DefaultProfileId = settings.DefaultProfileId == id ? remaining[0].Id : settings.DefaultProfileId }); notice.Text = L.T("语音配置已删除。"); }
        catch (IOException ex) { notice.Text = L.T("删除失败：") + L.Message(ex.Message); }
    }
    private void SaveBasic()
    {
        try
        {
            var next = settings with { HoldMs = (int)hold.Value, MovementDip = (int)(movement.Value ?? 8), ReleaseMouseOnActivate = releaseMouse.IsChecked == true, TriggerAnywhere = true };
            Save(next); notice.Text = L.T("设置已保存；活动会话会先结束。");
        }
        catch (Exception ex) { notice.Text = L.T("保存失败：") + L.Message(ex.Message); }
    }
    private void RefreshAutoStart()
    {
        try { autoStartSwitch.IsChecked = autoStart.IsEnabled(); autoStartSwitch.IsEnabled = true; autoStartHint.Text = L.T("登录后在托盘运行，自动启用长按说话。切换后立即生效。"); }
        catch (Exception ex) { autoStartSwitch.IsChecked = false; autoStartSwitch.IsEnabled = false; autoStartHint.Text = L.T("无法读取开机自启动状态：") + L.Message(ex.Message); }
    }
    private void ApplyJson()
    {
        try { Save(ConfigurationStore.Parse(editor.Text ?? "") with { Enabled = running }); notice.Text = L.T("配置校验通过并已保存。"); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { notice.Text = L.T("配置无效：") + L.Message(ex.Message); }
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
                    await Task.Run(host.Start); notice.Text = L.Message(platform.PermissionStatus);
                }
                if (host.IsFaulted) throw new InvalidOperationException(L.T("平台监听启动期间发生故障，请重新启用。"));
                restartRequired = false; running = true; settings = settings with { Enabled = true }; host.Configure(settings);
            }
            else { autoResumeWanted = false; running = false; settings = settings with { Enabled = false }; host!.Configure(settings); }
            SyncControls();
        }
        catch (Exception ex)
        {
            if (host != null) { host.Changed -= statusHandler; host.RecoveryRequested -= recoveryHandler; await host.DisposeAsync(); host = null; platform = null; }
            running = false; notice.Text = L.T("无法启用：") + L.Message(ex.Message);
        }
        finally { busy = false; toggle.IsEnabled = true; SyncSwitch(); }
    }
    private void SyncSwitch()
    {
        toggle.IsChecked = running;
        toggleLabel.Text = running ? L.T("已启用") : restartRequired ? L.T("待恢复") : L.T("已暂停");
        toggleLabel.Foreground = Brush.Parse(running ? "#027A48" : "#667085");
    }
    public async Task EnableOnStartupAsync()
    {
        if (running || busy || IsShuttingDown) return;
        state.Text = L.T("正在启动全局长按监听…");
        await ToggleAsync();
        if (!running) state.Text = L.T("识别未启用，请查看提示");
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
                notice.Text = L.Message(message) + L.T(" · 正在自动恢复…");
                await ToggleAsync();
                if (running) { autoResumeWanted = false; notice.Text = L.T("监听已自动恢复 · 可以继续长按说话"); return; }
            }
            notice.Text = L.T("自动恢复未成功，请点击重新启用；诊断报告保留了故障原因。");
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
            SyncSwitch(); notice.Text = L.Message(s.Message);
        }
        state.Text = L.Message(s.Message); detail.Text = settings.TriggerAnywhere ? L.T("全局长按 · 不检测输入框 · 松开鼠标结束") : s.Target == null ? (settings.AllowUnconfirmedTargets ? L.T("宽松模式：未知位置允许触发，明确不可输入的位置拒绝。") : L.T("严格模式：只在确认可编辑的输入框内触发。")) : L.F("控件：{0} · {1}", s.Target.ControlType, s.Code);
        if (s.Code is "focus_changed" or "probe_timeout" or "activation_failed" or "shortcut_unavailable") notice.Text = L.Message(s.Message);
        if (s.ObservedTarget != null) detail.Text += L.F(" · 探测：{0}", s.ObservedTarget.Reason);
        if (s.Code is "detecting" or "hover_ready") return;
        diagnosticLog.Add(s);
        logs.Enqueue($"{DateTime.Now:HH:mm:ss}  {s.Code}  {s.State}" + (s.ObservedTarget == null ? "" : $"  probe={s.ObservedTarget.Reason}")); while (logs.Count > 80) logs.Dequeue(); diagnostics.Text = string.Join(Environment.NewLine, logs);
    });
    public string DiagnosticJson() => JsonSerializer.Serialize(diagnosticLog.Report(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown", System.Runtime.InteropServices.RuntimeInformation.OSDescription, settings), ConfigurationStore.Json);
    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = L.T("导出诊断报告"), SuggestedFileName = "holdspeak-diagnostics.json", DefaultExtension = "json" });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) { stream.SetLength(0); stream.Position = 0; }
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(DiagnosticJson()); notice.Text = L.T("诊断报告已导出。");
        }
        catch (Exception ex) { notice.Text = L.T("导出诊断失败：") + L.Message(ex.Message); }
    }
    private async Task ImportAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = L.T("导入配置"), AllowMultiple = false, FileTypeFilter = [new("JSON") { Patterns = ["*.json"] }] });
            if (files.Count == 0) return; await using var stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream);
            editor.Text = await reader.ReadToEndAsync(); ConfigurationStore.Parse(editor.Text); notice.Text = L.T("导入校验通过，点击“校验并应用”保存。");
        }
        catch (Exception ex) { notice.Text = L.T("导入失败：") + L.Message(ex.Message); }
    }
    private async Task ExportAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = L.T("导出配置"), SuggestedFileName = "holdspeak-settings.json", DefaultExtension = "json" });
            if (file == null) return; await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) { stream.SetLength(0); stream.Position = 0; }
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(JsonSerializer.Serialize(settings with { Enabled = false }, ConfigurationStore.Json)); notice.Text = L.T("配置已导出。");
        }
        catch (Exception ex) { notice.Text = L.T("导出失败：") + L.Message(ex.Message); }
    }
    public async Task ShutdownAsync()
    {
        if (IsShuttingDown) return; IsShuttingDown = true; autoResumeWanted = false;
        inputMethodTimer.Stop();
        CancelShortcutCapture();
        if (host != null) { host.Changed -= statusHandler; host.RecoveryRequested -= recoveryHandler; await host.DisposeAsync(); host = null; }
    }
}
