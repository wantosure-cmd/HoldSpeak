using HoldSpeak.Core;
using System.Diagnostics;

if (args is ["--probe-stdio"])
{
    string? line;
    while ((line = Console.ReadLine()) != null)
    {
        var point = System.Text.Json.JsonSerializer.Deserialize<ProbePoint>(line);
        if (!point.Warmup) Thread.Sleep((int)point.X);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new TargetSnapshot(Editability.Editable, "editor.exe", 1, "Edit", point.Warmup ? "ready" : "editable", Environment.ProcessId.ToString())));
    }
    return 0;
}

if (args is ["--lease-test-worker", var leaseDirectory, var workerMode])
{
    try
    {
        using var lease = ListenerLease.Acquire(leaseDirectory);
        Console.WriteLine("ready"); Console.Out.Flush();
        if (workerMode == "hold") Console.ReadLine();
        return 0;
    }
    catch (IOException) { Console.WriteLine("busy"); return 3; }
}

var tests = new (string Name, Action Run)[]
{
    ("按程序名禁用同名实例，不区分大小写且重启配置后仍生效", () => {
        var saved = ConfigurationStore.Parse(System.Text.Json.JsonSerializer.Serialize(new Settings { TriggerAnywhere = true, AppRules = [new() { AppId = "game.exe", Enabled = false }] }, ConfigurationStore.Json));
        foreach (var app in new[] { "game.exe", "GAME.EXE" })
        {
            var t = new Fixture(saved); t.Platform.App = app; t.Platform.Window = app == "game.exe" ? 10 : 20;
            t.Down(); t.Engine.Tick(600); Equal(0, t.Platform.Begins);
        }
        var allowed = new Fixture(saved); allowed.Down(); allowed.Engine.Tick(600); Equal(1, allowed.Platform.Begins); allowed.Up(800);
    }),
    ("指定模式忽略当前输入法和应用配置覆盖", () => {
        var t = new Fixture(new Settings { Profiles = [new(), new() { Id = "other" }], AppRules = [new() { AppId = "editor.exe", ProfileId = "other" }] });
        t.Platform.Input = new("sogou", "搜狗"); t.Down(); t.Engine.Tick(600);
        Equal("default", t.Platform.LastProfile); Equal(0, t.Platform.InputReads); t.Up(700);
    }),
    ("自动模式按活动输入法切换并锁定当前会话", () => {
        var t = new Fixture(AutomaticSettings()); t.Platform.Input = new("wechat", "微信"); t.Down(); t.Engine.Tick(600);
        Equal("wechat", t.Platform.LastProfile); Equal(ActivationMode.Hold, t.Platform.LastMode);
        t.Platform.Input = new("sogou", "搜狗"); t.Engine.Tick(700); Equal("wechat", t.Platform.LastProfile); t.Up(800); Equal(1, t.Platform.Ends);
        t.Down(); t.Engine.Tick(600); Equal("sogou", t.Platform.LastProfile); Equal(ActivationMode.Toggle, t.Platform.LastMode); t.Up(800); Equal(2, t.Platform.Ends);
    }),
    ("自动模式在阈值前切换输入法时使用最新配置", () => {
        var t = new Fixture(AutomaticSettings()); t.Platform.Input = new("wechat", "微信"); t.Down();
        t.Platform.Input = new("sogou", "搜狗"); t.Engine.Tick(600); Equal("sogou", t.Platform.LastProfile); t.Up(800);
    }),
    ("自动模式未知或未映射时不回退固定快捷键", () => {
        foreach (var input in new InputMethodInfo?[] { null, new("unknown", "未知输入法") }) {
            var t = new Fixture(AutomaticSettings()); t.Platform.Input = input; t.Down(); t.Engine.Tick(600); Equal(0, t.Platform.Begins);
        }
    }),
    ("输入法映射与模式可保存往返，旧配置默认指定模式", () => {
        var roundtrip = ConfigurationStore.Parse(System.Text.Json.JsonSerializer.Serialize(AutomaticSettings(), ConfigurationStore.Json));
        Equal(ShortcutSelectionMode.Automatic, roundtrip.ShortcutSelection); Equal("sogou", roundtrip.InputMethodBindings[1].ProfileId);
        Equal(ShortcutSelectionMode.Fixed, ConfigurationStore.Parse("{}").ShortcutSelection);
        bool rejected = false; try { (roundtrip with { InputMethodBindings = [roundtrip.InputMethodBindings[0], roundtrip.InputMethodBindings[0]] }).Validate(); } catch (InvalidDataException) { rejected = true; } Equal(true, rejected);
        rejected = false; try { (roundtrip with { InputMethodBindings = [new() { InputMethodId = "x", Name = "x", ProfileId = "missing" }] }).Validate(); } catch (InvalidDataException) { rejected = true; } Equal(true, rejected);
    }),
    ("微信与搜狗预设使用独立的保持及切换协议", () => {
        var wechat = InputMethodPresets.Create(new("wechat", "微信", "wechat"))!;
        var sogou = InputMethodPresets.Create(new("sogou", "搜狗", "sogou"))!;
        Equal(ActivationMode.Hold, wechat.Mode); Equal(Key.LWin, wechat.WindowsKeys[1]);
        Equal(ActivationMode.Toggle, sogou.Mode); Equal(Key.F2, sogou.WindowsKeys.Single());
        Equal(true, InputMethodPresets.Create(new("unknown", "未知")) == null);
    }),
    ("300ms 全局阈值立即触发并记录快捷键发送耗时", () => {
        var t = new Fixture(new Settings { HoldMs = 300 }); Status? sent = null;
        t.Engine.Changed += s => { if (s.Code == "shortcut_held") sent = s; };
        t.Down(); t.Engine.Tick(299); Equal(0, t.Platform.Begins);
        t.Engine.Tick(300); Equal(1, t.Platform.Begins);
        if (sent?.TriggerLatencyMs is not >= 300 || sent.DispatchLatencyMs is not >= 0) throw new Exception("Missing trigger timing");
        var log = new DiagnosticLog(); log.Add(sent);
        Equal(sent.TriggerLatencyMs, log.Report("test", "test", new()).Events.Single().TriggerLatencyMs);
        t.Up(350); Equal(1, t.Platform.Ends);
    }),
    ("默认全局长按不进行任何输入框探测，阈值触发，松开结束", () => {
        Equal(true, new Settings().TriggerAnywhere); var t = new Fixture(new Settings()); int probes = 0;
        t.Engine.ProbeRequested += (_, _, _) => probes++; t.Engine.HoverRequested += (_, _, _) => probes++; t.Engine.ActiveRequested += (_, _, _) => probes++;
        t.Engine.Tick(0); t.Down(); t.Engine.Tick(599); Equal(0, t.Platform.Begins);
        t.Engine.Tick(600); Equal(1, t.Platform.Begins); t.Engine.Tick(1200); Equal(0, probes);
        t.Up(1300); Equal(1, t.Platform.Ends);
    }),
    ("全局模式忽略应用窗口及控件判断，保持说话不中断", () => {
        var t = new Fixture(new Settings()); t.Down(); t.Engine.Tick(600);
        t.Platform.App = "Feishu.exe"; t.Platform.Window = 2; t.Engine.Tick(1200);
        Equal(GestureState.Holding, t.Engine.State); Equal(0, t.Platform.Ends); t.Up(1300); Equal(1, t.Platform.Ends);
    }),
    ("全局模式仍保留短点击、拖动取消与最长时间结束", () => {
        var t = new Fixture(new Settings()); t.Down(); t.Up(200); Equal(0, t.Platform.Begins);
        t.Down(); t.Engine.Handle(new(PointerKind.Move, 20, 0, 100)); t.Engine.Tick(600); Equal(0, t.Platform.Begins);
        t.Up(700); t.Down(); t.Engine.Tick(600); t.Engine.Tick(121000); Equal(1, t.Platform.Begins); Equal(1, t.Platform.Ends);
    }),
    ("悬停超时不重启健康探测进程，忙时跳过，迟到响应不串线", () => {
        using var probe = new ProcessProbe();
        using var firstDeadline = new CancellationTokenSource(5000);
        var first = probe.ProbeAsync(0, 0, firstDeadline.Token).GetAwaiter().GetResult();
        using var shortDeadline = new CancellationTokenSource(50); bool cancelled = false;
        try { probe.HoverAsync(300, 0, shortDeadline.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
        Equal(true, cancelled);
        var busy = probe.HoverAsync(0, 0, CancellationToken.None).GetAwaiter().GetResult(); Equal("hover_busy", busy.Reason);
        var next = probe.ProbeAsync(0, 0, firstDeadline.Token).GetAwaiter().GetResult();
        Equal(first.ControlId, next.ControlId); Equal("editable", next.Reason);
    }),
    ("卡死探测进程到硬超时会重建", () => {
        using var probe = new ProcessProbe(); using var deadline = new CancellationTokenSource(5000);
        var first = probe.ProbeAsync(0, 0, deadline.Token).GetAwaiter().GetResult(); bool cancelled = false;
        try { probe.ProbeAsync(2500, 0, deadline.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
        Equal(true, cancelled);
        var next = probe.ProbeAsync(0, 0, deadline.Token).GetAwaiter().GetResult(); Equal(false, first.ControlId == next.ControlId);
    }),
    ("默认宽松模式允许未知容器，松开仍结束", () => {
        Equal(true, new Settings().AllowUnconfirmedTargets);
        var t = new Fixture(new() { TriggerAnywhere = false }); t.Down();
        var unknown = Fixture.Target with { Editability = Editability.Unknown, Reason = "document_container" };
        t.Engine.CompleteProbe(t.Engine.Generation, unknown); t.Engine.Tick(600); t.Engine.CompleteProbe(t.Engine.Generation, unknown); t.Engine.Tick(620);
        Equal(1, t.Platform.Begins); t.Engine.Tick(1200); t.Engine.CompleteProbe(t.Engine.Generation, unknown); Equal(0, t.Platform.Ends);
        t.Up(1300); Equal(1, t.Platform.Ends);
    }),
    ("宽松模式探测超时允许触发，但换窗口立即停止", () => {
        var t = new Fixture(new() { TriggerAnywhere = false }); t.Down(); t.Engine.Tick(251); t.Engine.Tick(350); t.Engine.Tick(600);
        Equal(1, t.Platform.Begins); t.Engine.Tick(1400); t.Engine.Tick(1700); Equal(0, t.Platform.Ends);
        t.Platform.Window = 2; t.Engine.Tick(1720); Equal(1, t.Platform.Ends);
    }),
    ("宽松模式仍拒绝明确不可输入、密码、只读和本工具窗口", () => {
        foreach (var reason in new[] { "password_field", "read_only", "non_input_control", "own_window" }) {
            var t = new Fixture(new() { TriggerAnywhere = false }); t.Down(); t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target with { Editability = Editability.NotEditable, Reason = reason });
            t.Engine.Tick(1000); Equal(0, t.Platform.Begins);
        }
    }),
    ("宽松模式发现密码框仍结束，已知编辑器换控件仍结束", () => {
        foreach (bool secret in new[] { false, true }) {
            var t = new Fixture(new() { TriggerAnywhere = false }); t.Down(); t.Probe(); t.Activate(); t.Engine.Tick(1200);
            t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target with { Editability = secret ? Editability.NotEditable : Editability.Editable, Reason = secret ? "password_field" : "editable", ControlId = "other" });
            Equal(1, t.Platform.Ends);
        }
    }),
    ("未知结果不能跨应用或跨窗口复用", () => {
        var t = new Fixture(new() { TriggerAnywhere = false }); t.Down(); t.Platform.App = "other.exe";
        t.Engine.CompleteProbe(t.Engine.Generation, new(Editability.Unknown, "", 0, "", "probe_timeout")); Equal(0, t.Platform.Begins); Equal(GestureState.AwaitRelease, t.Engine.State);
    }),
    ("活动复核使用焦点检测，布局变化仍保持原编辑器", () => {
        var t = new Fixture(); int initial = 0, active = 0;
        t.Engine.ProbeRequested += (_, _, _) => initial++;
        t.Engine.ActiveRequested += (id, _, _) => { active++; t.Engine.CompleteProbe(id, Fixture.Target); };
        t.Down(); t.Probe(); t.Activate(); int before = initial;
        t.Engine.Tick(1200); t.Engine.Tick(1800);
        Equal(before, initial); Equal(2, active); Equal(0, t.Platform.Ends); Equal(GestureState.Holding, t.Engine.State);
    }),
    ("悬停先识别，按下复用，复核在阈值前完成且到阈值立即启动", () => {
        var t = new Fixture(); long hoverId = 0; int probes = 0;
        t.Engine.HoverRequested += (id, _, _) => hoverId = id;
        t.Engine.ProbeRequested += (_, _, _) => probes++;
        t.Engine.Tick(0); t.Engine.CompleteHoverProbe(hoverId, Fixture.Target, 10);
        t.Platform.Left = true; t.Engine.Handle(new(PointerKind.Down, 0, 0, 20)); Equal(0, probes);
        t.Engine.Tick(540); Equal(1, probes); t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target);
        t.Engine.Tick(619); Equal(0, t.Platform.Begins); t.Engine.Tick(620); Equal(1, t.Platform.Begins);
    }),
    ("悬停缓存失效、位置不同或未知结果不能跳过按下检测", () => {
        foreach (int scenario in new[] { 0, 1, 2 }) {
            var t = new Fixture(); long id = 0; int probes = 0;
            t.Engine.HoverRequested += (value, _, _) => id = value; t.Engine.ProbeRequested += (_, _, _) => probes++;
            t.Engine.Tick(0); t.Engine.CompleteHoverProbe(id, scenario == 2 ? Fixture.Target with { Editability = Editability.Unknown } : Fixture.Target, 10);
            t.Platform.Left = true; t.Engine.Handle(new(PointerKind.Down, scenario == 1 ? 20 : 0, 0, scenario == 0 ? 2000 : 20));
            Equal(1, probes); Equal(0, t.Platform.Begins);
        }
    }),
    ("悬停后焦点输入框改变仍拒绝启动", () => {
        var t = new Fixture(); long id = 0;
        t.Engine.HoverRequested += (value, _, _) => id = value;
        t.Engine.Tick(0); t.Engine.CompleteHoverProbe(id, Fixture.Target, 10);
        t.Platform.Left = true; t.Engine.Handle(new(PointerKind.Down, 0, 0, 20)); t.Engine.Tick(540);
        t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target with { ControlId = "new-editor" });
        t.Engine.Tick(620); Equal(0, t.Platform.Begins); Equal(GestureState.AwaitRelease, t.Engine.State);
    }),
    ("暂停不进行悬停检测，迟到结果不能改变状态", () => {
        var t = new Fixture(); long id = 0; int hovers = 0;
        t.Engine.HoverRequested += (value, _, _) => { id = value; hovers++; };
        t.Engine.Tick(0); t.Engine.Configure(new() { Enabled = false });
        t.Engine.CompleteHoverProbe(id, Fixture.Target, 10); t.Engine.Tick(1000);
        Equal(1, hovers); Equal(GestureState.Suspended, t.Engine.State);
    }),
    ("悬停失败有重试间隔，不反复启动探测且不触发语音", () => {
        var t = new Fixture(); long id = 0; int hovers = 0;
        t.Engine.HoverRequested += (value, _, _) => { id = value; hovers++; };
        t.Engine.Tick(0); t.Engine.CompleteHoverProbe(id, new(Editability.Unknown, "", 0, "", "hover_timeout"), 10);
        t.Engine.Tick(80); t.Engine.Tick(400); Equal(1, hovers);
        t.Engine.Tick(620); Equal(2, hovers); Equal(0, t.Platform.Begins);
    }),
    ("不同配置目录的进程不能同时持有监听锁，正常退出后可重启", () => TestListenerLease(false)),
    ("监听进程被结束后自动释放锁，残留文件不阻止重启", () => TestListenerLease(true)),
    ("结束快捷键拒绝已按下的普通键", () => {
        foreach (bool mac in new[] { false, true })
        foreach (var held in KeyCatalog.ForPlatform(mac))
        {
            bool rejected = false;
            try { ShortcutGuard.EnsureAvailable([held], false, key => key == held, true); }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains("原软件"); }
            Equal(true, rejected);
        }
    }),
    ("任意实体修饰键阻止开始与结束快捷键", () => {
        foreach (bool ending in new[] { false, true })
        {
            bool rejected = false;
            try { ShortcutGuard.EnsureAvailable([Key.F8], true, _ => false, ending); }
            catch (InvalidOperationException) { rejected = true; }
            Equal(true, rejected);
        }
    }),
    ("松开后的快捷键恢复可用，非快捷键不阻止结束", () => {
        var held = new HashSet<Key> { Key.F8 };
        bool rejected = false;
        try { ShortcutGuard.EnsureAvailable([Key.F8], false, held.Contains, true); }
        catch (InvalidOperationException) { rejected = true; }
        Equal(true, rejected);
        held.Clear(); held.Add(Key.A);
        ShortcutGuard.EnsureAvailable([Key.F8], false, held.Contains, true);
        ShortcutGuard.EnsureAvailable([Key.F8], false, held.Contains, false);
    }),
    ("短点击不触发", () => { var t = new Fixture(); t.Down(); t.Up(200); t.Engine.Tick(1000); Equal(0, t.Platform.Begins); }),
    ("阈值前不触发，复核后保持，松开只结束一次", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Engine.Tick(599); Equal(0, t.Platform.Begins); t.Activate(); Equal(1, t.Platform.Begins); t.Engine.Tick(800); t.Up(900); t.Up(901); Equal(1, t.Platform.Ends); }),
    ("移出再移回不恢复手势", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Engine.Handle(new(PointerKind.Move, 20, 0, 100)); t.Engine.Handle(new(PointerKind.Move, 0, 0, 200)); t.Activate(); Equal(0, t.Platform.Begins); }),
    ("高 DPI 使用物理像素容差", () => { var t = new Fixture(); t.Down(2); t.Probe(); t.Engine.Handle(new(PointerKind.Move, 12, 0, 100)); t.Activate(); Equal(1, t.Platform.Begins); }),
    ("双击不进入长按", () => { var t = new Fixture(); t.Platform.Left = true; t.Engine.Handle(new(PointerKind.Down, 0, 0, 0, 1, true)); t.Activate(); Equal(0, t.Platform.Begins); }),
    ("密码或未知区域拒绝触发", () => { foreach (var state in new[] { Editability.NotEditable, Editability.Unknown }) { var t = new Fixture(); t.Down(); t.Engine.CompleteProbe(t.Engine.Generation, new(state, "editor.exe", 1, "Edit", "password_field")); t.Activate(); Equal(0, t.Platform.Begins); } }),
    ("迟到的探测不能触发新会话", () => { var t = new Fixture(); t.Down(); var old = t.Engine.Generation; t.Up(10); t.Down(); t.Engine.CompleteProbe(old, Fixture.Target); t.Engine.Tick(601); Equal(0, t.Platform.Begins); }),
    ("查询卡住会超时，松开仍恢复空闲", () => { var t = new Fixture(); t.Down(); t.Engine.Tick(251); Equal(GestureState.AwaitRelease, t.Engine.State); t.Up(300); Equal(GestureState.Idle, t.Engine.State); }),
    ("复核发现同应用换了输入框会取消", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Engine.Tick(600); t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target with { ControlId = "other" }); t.Engine.Tick(650); Equal(0, t.Platform.Begins); }),
    ("活动会话切到其他应用会释放", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Activate(); t.Platform.App = "other.exe"; t.Engine.Tick(800); Equal(1, t.Platform.Ends); Equal(GestureState.AwaitRelease, t.Engine.State); }),
    ("活动期间同应用切换输入框会释放", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Activate(); t.Engine.Tick(1200); t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target with { ControlId = "other" }); Equal(1, t.Platform.Ends); }),
    ("活动复核失败保留真正的探测原因", () => {
        var t = new Fixture(); Status? failure = null;
        t.Engine.Changed += status => { if (status.Code == "focus_changed") failure = status; };
        t.Down(); t.Probe(); t.Activate(); t.Engine.Tick(1200);
        t.Engine.CompleteProbe(t.Engine.Generation, Fixture.Target with { Editability = Editability.Unknown, Reason = "not_confirmed_editable", ControlId = "" });
        Equal(1, t.Platform.Ends); Equal("not_confirmed_editable", failure!.ObservedTarget!.Reason); Equal("editable", failure.Target!.Reason);
    }),
    ("诊断报告有界且不导出消息、窗口与控件身份", () => {
        var log = new DiagnosticLog();
        var target = Fixture.Target with { WindowId = 987654321, ControlId = "PRIVATE_TYPED_CONTENT" };
        for (int i = 0; i < 100; i++) log.Add(new(GestureState.AwaitRelease, "event_" + i, "PRIVATE_TYPED_CONTENT", target, target with { Reason = "probe_timeout" }));
        var report = log.Report("test", "test OS", new()); Equal(80, report.Events.Length); Equal("event_20", report.Events[0].Code);
        string json = System.Text.Json.JsonSerializer.Serialize(report, ConfigurationStore.Json);
        Equal(false, json.Contains("PRIVATE_TYPED_CONTENT")); Equal(false, json.Contains("987654321")); Equal(true, json.Contains("probe_timeout"));
    }),
    ("活动期间目标查询卡住会释放", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Activate(); t.Engine.Tick(1200); t.Engine.Tick(1500); Equal(1, t.Platform.Ends); }),
    ("配置的输入法浮窗允许继续保持", () => { var settings = new Settings { Profiles = [new() { AllowedOverlayApps = ["voice.exe"] }] }; var t = new Fixture(settings); t.Down(); t.Probe(); t.Activate(); t.Platform.App = "voice.exe"; t.Engine.Tick(800); Equal(0, t.Platform.Ends); t.Up(900); Equal(1, t.Platform.Ends); }),
    ("最长保持时间到期不重复触发", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Activate(); t.Engine.Tick(121000); t.Engine.Tick(122000); Equal(1, t.Platform.Begins); Equal(1, t.Platform.Ends); }),
    ("暂停先结束当前会话", () => { var t = new Fixture(); t.Down(); t.Probe(); t.Activate(); t.Engine.Configure(new() { Enabled = false }); Equal(1, t.Platform.Ends); Equal(GestureState.Suspended, t.Engine.State); }),
    ("禁用应用不会触发", () => { var t = new Fixture(new() { AppRules = [new() { AppId = "editor.exe", Enabled = false }] }); t.Down(); t.Probe(); t.Activate(); Equal(0, t.Platform.Begins); }),
    ("指定模式的旧检测路径也不被应用配置覆盖", () => { var t = new Fixture(new() { Profiles = [new(), new() { Id = "other", Name = "other" }], AppRules = [new() { AppId = "editor.exe", ProfileId = "other" }] }); t.Down(); t.Probe(); t.Activate(); Equal("default", t.Platform.LastProfile); }),
    ("启动过程中松开立刻结束", () => { var t = new Fixture(); t.Platform.ReleaseDuringBegin = true; t.Down(); t.Probe(); t.Activate(); Equal(1, t.Platform.Ends); }),
    ("注入失败本轮不重试", () => { var t = new Fixture(); t.Platform.FailBegin = true; t.Down(); t.Probe(); t.Activate(); t.Engine.Tick(2000); Equal(1, t.Platform.Begins); Equal(GestureState.AwaitRelease, t.Engine.State); }),
    ("配置拒绝重复 ID、空集合、未知字段和跨平台按键", () => {
        foreach (var json in new[] { "{\"profiles\":null}", "{\"profiles\":[]}", "{\"schemaVersion\":2}", "{\"holdMs\":1}", "{\"typo\":true}", "{\"profiles\":[{\"windowsKeys\":[\"command\"]}]}" })
        { bool rejected = false; try { ConfigurationStore.Parse(json); } catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { rejected = true; } Equal(true, rejected); }
    }),
    ("微信输入法预设与通用示例可加载", () => {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        foreach (var name in new[] { "example.json", "wetype-windows.json" }) ConfigurationStore.Parse(File.ReadAllText(Path.Combine(root, "profiles", name)));
    }),
    ("自定义快捷键可往返保存，数字别名不接受枚举编号", () => {
        var settings = new Settings { Profiles = [new() { WindowsKeys = KeyCatalog.Parse("LCtrl,1"), MacKeys = KeyCatalog.Parse("Command,A"), WindowsStopKeys = [Key.F24], MacStopKeys = [Key.F20] }] };
        var loaded = ConfigurationStore.Parse(System.Text.Json.JsonSerializer.Serialize(settings, ConfigurationStore.Json));
        Equal(Key.D1, loaded.Profiles[0].WindowsKeys[1]); Equal(Key.A, loaded.Profiles[0].MacKeys[1]);
        bool rejected = false; try { KeyCatalog.Parse("100"); } catch (InvalidDataException) { rejected = true; } Equal(true, rejected);
        rejected = false; try { (settings with { Profiles = [new() { MacKeys = [Key.F24] }] }).Validate(); } catch (InvalidDataException) { rejected = true; } Equal(true, rejected);
    }),
    ("公开 schema 的快捷键范围与两个平台一致", () => {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas/settings.schema.json")));
        foreach (bool mac in new[] { false, true })
        {
            var declared = schema.RootElement.GetProperty("$defs").GetProperty(mac ? "macKeys" : "windowsKeys").GetProperty("items").GetProperty("enum").EnumerateArray().Select(v => v.GetString()!).Order().ToArray();
            var actual = KeyCatalog.ForPlatform(mac).Select(k => System.Text.Json.JsonSerializer.Serialize(k, ConfigurationStore.Json).Trim('"')).Order().ToArray();
            Equal(string.Join(',', declared), string.Join(',', actual));
        }
    }),
    ("损坏配置从有效备份恢复，重新保存不污染备份", () => {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string directory = Path.Combine(root, "artifacts", "tests", "HoldSpeak-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            ConfigurationStore.Save(path, new() { HoldMs = 700 });
            ConfigurationStore.Save(path, new() { HoldMs = 800 });
            File.WriteAllText(path, "broken");
            Equal(700, ConfigurationStore.Load(path, out bool recovered).HoldMs); Equal(true, recovered);
            Equal("broken", File.ReadAllText(path));
            ConfigurationStore.Save(path, new() { HoldMs = 900 });
            Equal(700, ConfigurationStore.Parse(File.ReadAllText(path + ".bak")).HoldMs);
            Equal(900, ConfigurationStore.Load(path, out recovered).HoldMs); Equal(false, recovered);
            Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }),
    ("1000 次完整手势无残留会话", () => { var t = new Fixture(); for (int i = 0; i < 1000; i++) { t.Down(); t.Probe(); t.Activate(); t.Up(800); } Equal(1000, t.Platform.Begins); Equal(1000, t.Platform.Ends); })
    ,("高频移动不淹没引擎，松开仍能结束", () => StressHost(false).GetAwaiter().GetResult())
    ,("生产端保留越界后移回的取消", () => StressHost(true).GetAwaiter().GetResult())
    ,("平台故障结束活动会话且旧引擎不能再次启用", () => FaultHost().GetAwaiter().GetResult())
};
int failures = 0;
foreach (var (name, run) in tests) { try { run(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + " : " + ex.Message); } }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed"); return failures == 0 ? 0 : 1;
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}"); }
static void TestListenerLease(bool crash)
{
    string directory = Path.Combine(Path.GetTempPath(), "HoldSpeak-lease-test-" + Guid.NewGuid().ToString("N"));
    using var owner = StartWorker("hold", "config-a");
    try
    {
        Equal("ready", owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        using (var contender = StartWorker("attempt", "config-b"))
        {
            try
            {
                Equal("busy", contender.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
                if (!contender.WaitForExit(5000)) throw new Exception("Contender did not exit");
                Equal(3, contender.ExitCode);
            }
            finally { if (!contender.HasExited) { contender.Kill(true); contender.WaitForExit(5000); } }
        }
        bool blocked = false;
        try { using var unexpected = ListenerLease.Acquire(directory); }
        catch (IOException) { blocked = true; }
        Equal(true, blocked);
        if (crash) owner.Kill(true);
        else { owner.StandardInput.WriteLine("exit"); owner.StandardInput.Flush(); }
        if (!owner.WaitForExit(5000)) throw new Exception("Lease owner did not exit");
        Equal(true, File.Exists(Path.Combine(directory, "listener.lock")));
        using var next = ListenerLease.Acquire(directory);
        next.Dispose(); next.Dispose();
        using var afterDispose = ListenerLease.Acquire(directory);
    }
    finally
    {
        if (!owner.HasExited) { owner.Kill(true); owner.WaitForExit(5000); }
        // This generated directory contains only the test lock and no recursive data.
        if (File.Exists(Path.Combine(directory, "listener.lock"))) File.Delete(Path.Combine(directory, "listener.lock"));
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }
    Process StartWorker(string mode, string config)
    {
        var info = ProcessProbe.ChildStartInfo("--lease-test-worker");
        info.ArgumentList.Add(directory); info.ArgumentList.Add(mode);
        info.Environment["HOLDSPEAK_CONFIG_DIR"] = Path.Combine(directory, config);
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true;
        return Process.Start(info) ?? throw new Exception("Could not start lease test worker");
    }
}
static async Task FaultHost()
{
    var platform = new FakePlatform { Left = true };
    await using var host = new EngineHost(platform, new() { HoldMs = 300 });
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    host.Changed += status => { if (status.Code == "shortcut_held") started.TrySetResult(); if (status.Code == "platform_failed") failed.TrySetResult(); };
    platform.Emit(new(PointerKind.Down, 0, 0, Environment.TickCount64));
    await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
    platform.Fail(); await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Equal(1, platform.Ends);
    host.Configure(new() { HoldMs = 300 });
    platform.Emit(new(PointerKind.Up, 0, 0, Environment.TickCount64));
    platform.Emit(new(PointerKind.Down, 0, 0, Environment.TickCount64));
    await Task.Delay(450); Equal(1, platform.Begins);
}
static Settings AutomaticSettings() => new()
{
    ShortcutSelection = ShortcutSelectionMode.Automatic, DefaultProfileId = "wechat",
    Profiles = [new() { Id = "wechat" }, new() { Id = "sogou", Mode = ActivationMode.Toggle, WindowsKeys = [Key.F2] }],
    InputMethodBindings = [new() { InputMethodId = "wechat", Name = "微信", ProfileId = "wechat" }, new() { InputMethodId = "sogou", Name = "搜狗", ProfileId = "sogou" }]
};
static async Task StressHost(bool drag)
{
    var platform = new FakePlatform { Left = true };
    await using var host = new EngineHost(platform, new());
    long time = Environment.TickCount64; platform.Emit(new(PointerKind.Down, 0, 0, time));
    if (drag) platform.Emit(new(PointerKind.Move, 20, 0, time + 1));
    for (int i = 0; i < 100000; i++) platform.Emit(new(PointerKind.Move, i % 4, 0, time + 2));
    await Task.Delay(850); platform.Left = false; platform.Emit(new(PointerKind.Up, 0, 0, Environment.TickCount64));
    await Task.Delay(100); Equal(drag ? 0 : 1, platform.Begins); Equal(drag ? 0 : 1, platform.Ends);
}

sealed class Fixture
{
    internal static readonly TargetSnapshot Target = new(Editability.Editable, "editor.exe", 1, "Edit", "editable", "input-1");
    internal FakePlatform Platform = new();
    internal GestureEngine Engine;
    internal Fixture(Settings? settings = null) => Engine = new(Platform, settings ?? new() { AllowUnconfirmedTargets = false, TriggerAnywhere = false });
    internal void Down(double scale = 1) { Platform.Left = true; Engine.Handle(new(PointerKind.Down, 0, 0, 0, scale)); }
    internal void Up(long time) { Platform.Left = false; Engine.Handle(new(PointerKind.Up, 0, 0, time)); }
    internal void Probe() => Engine.CompleteProbe(Engine.Generation, Target);
    internal void Activate() { Engine.Tick(600); Engine.CompleteProbe(Engine.Generation, Target); Engine.Tick(620); }
}
sealed class FakePlatform : IDesktopPlatform
{
    internal bool Left, ReleaseDuringBegin, FailBegin;
    internal string App = "editor.exe", LastProfile = "";
    internal int Begins, Ends;
    internal InputMethodInfo? Input;
    internal int InputReads;
    internal ActivationMode LastMode;
    public InputMethodInfo? CurrentInputMethod { get { InputReads++; return Input; } }
    public bool IsMac => false;
    public bool PhysicalLeftDown => Left;
    public string ForegroundAppId => App;
    internal long Window = 1;
    public long ForegroundWindowId => Window;
    public string PermissionStatus => "test";
    public (double X, double Y) PointerPosition => (0, 0);
    public ITargetProbe Probe { get; } = new ProbeStub();
    public event Action<PointerEvent>? Pointer;
    public event Action<string>? Faulted;
    internal void Fail() => Faulted?.Invoke("test failure");
    internal void Emit(PointerEvent pointer) => Pointer?.Invoke(pointer);
    public void Start() { }
    public IInputSession Begin(VoiceProfile profile, bool releaseMouse) { Begins++; LastProfile = profile.Id; LastMode = profile.Mode; if (FailBegin) throw new IOException("injected"); if (ReleaseDuringBegin) Left = false; return new Lease(this); }
    public void EmergencyRelease() { }
    public void Dispose() { }
    sealed class ProbeStub : ITargetProbe
    {
        public Task<TargetSnapshot> ProbeAsync(double x, double y, CancellationToken token) => Task.FromResult(Fixture.Target);
    }
    sealed class Lease(FakePlatform owner) : IInputSession
    {
        private bool ended;
        public void End() { if (!ended) { ended = true; owner.Ends++; } }
        public void Dispose() => End();
    }
}
