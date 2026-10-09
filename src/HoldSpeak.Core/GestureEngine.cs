namespace HoldSpeak.Core;

// All methods are owned by one event-loop thread. Probe completions must be queued there.
public sealed class GestureEngine
{
    private readonly IDesktopPlatform platform;
    private Settings settings;
    private PointerEvent origin;
    private TargetSnapshot? target;
    private VoiceProfile? profile;
    private IInputSession? session;
    private long activeSince;
    private bool probeComplete;
    private bool verifying, verified;
    private long probeStarted;
    private long lastActiveProbe;
    private bool activeProbePending;
    private PointerEvent hoverPoint;
    private TargetSnapshot? hoverTarget;
    private long hoverGeneration, hoverStarted, hoverCompleted, lastHoverPoll = -1000;
    private bool hoverPending;
    private TargetSnapshot? requestContext;
    public long Generation { get; private set; }
    public GestureState State { get; private set; }
    public event Action<Status>? Changed;
    public event Action<long, PointerEvent, int>? ProbeRequested;
    public event Action<long, PointerEvent, int>? HoverRequested;
    public event Action<long, PointerEvent, int>? ActiveRequested;
    public GestureEngine(IDesktopPlatform platform, Settings settings)
    {
        settings.Validate(); this.platform = platform; this.settings = settings;
        State = settings.Enabled ? GestureState.Idle : GestureState.Suspended;
    }
    public void Configure(Settings next)
    {
        next.Validate(); ClearHover(); Finish("configuration_changed", "设置已更新"); settings = next;
        if (!next.Enabled) Set(GestureState.Suspended, "paused", "已暂停");
        else Set(platform.PhysicalLeftDown ? GestureState.AwaitRelease : GestureState.Idle, "ready", next.TriggerAnywhere ? "全局长按已启用 · 按住左键说话" : "等待输入框内长按");
    }
    public void Handle(PointerEvent e)
    {
        if (e.Kind == PointerKind.Up)
        {
            Finish("released", "快捷键已释放");
            Set(settings.Enabled ? GestureState.Idle : GestureState.Suspended, "ready", settings.Enabled ? settings.TriggerAnywhere ? "全局长按已启用 · 按住左键说话" : "等待输入框内长按" : "已暂停");
            return;
        }
        if (e.Kind == PointerKind.Cancel) { Finish("cancelled", "本次手势已取消"); return; }
        if (State == GestureState.Suspended) return;
        if (e.Kind == PointerKind.Down && State == GestureState.Idle)
        {
            if (e.MultiClick) { Set(GestureState.AwaitRelease, "multi_click", "保留双击／多击操作"); return; }
            if (settings.TriggerAnywhere)
            {
                origin = e; target = new(Editability.Unknown, platform.ForegroundAppId, platform.ForegroundWindowId, "", "global_hold");
                profile = settings.Resolve(target.AppId); Generation++;
                if (profile == null) { Finish("app_disabled", "当前应用已禁用"); return; }
                Set(GestureState.Candidate, "hold_pending", "全局长按 · 按住左键开始说话");
                return;
            }
            bool cached = hoverTarget != null && (Allowed(hoverTarget) || hoverTarget.Editability == Editability.NotEditable) && e.X == hoverPoint.X && e.Y == hoverPoint.Y &&
                e.TimeMs >= hoverCompleted && e.TimeMs - hoverCompleted <= 1000 && SameApp(hoverTarget.AppId, platform.ForegroundAppId) && hoverTarget.WindowId == platform.ForegroundWindowId;
            requestContext = new(Editability.Unknown, platform.ForegroundAppId, platform.ForegroundWindowId, "", "probe_timeout");
            origin = e; target = cached ? hoverTarget : null; profile = cached ? settings.Resolve(hoverTarget!.AppId) : null;
            probeComplete = cached; verifying = false; verified = false; probeStarted = e.TimeMs; Generation++;
            hoverGeneration++; hoverPending = false;
            Set(GestureState.Candidate, "detecting", "正在确认输入框");
            if (cached && !Allowed(hoverTarget!)) Finish(hoverTarget!.Reason, "当前位置明确不可输入");
            else if (cached && profile == null) Finish("app_disabled", "当前应用已禁用");
            else if (!cached) ProbeRequested?.Invoke(Generation, origin, settings.ProbeTimeoutMs);
        }
        else if (e.Kind == PointerKind.Move && State == GestureState.Candidate)
        {
            double radius = settings.MovementDip * Math.Max(0.5, origin.Scale);
            if (Math.Pow(e.X - origin.X, 2) + Math.Pow(e.Y - origin.Y, 2) > radius * radius)
                Finish("gesture_moved", "保留拖动选字操作");
        }
    }
    public void CompleteProbe(long generation, TargetSnapshot result)
    {
        if (generation != Generation) return;
        result = WithContext(result);
        if (State == GestureState.Holding)
        {
            activeProbePending = false;
            if (!Allowed(result) || target == null || IdentityChanged(target, result))
                Finish("focus_changed", $"说话已结束：{InterruptionReason(result)}", result);
            else if (result.Editability == Editability.Editable) target = result;
            return;
        }
        if (State != GestureState.Candidate) return;
        if (verifying && target != null && IdentityChanged(target, result))
        { Finish("focus_changed", "输入目标已改变", result); return; }
        probeComplete = true; target = result;
        if (!Allowed(result)) { Finish(result.Reason, result.Editability == Editability.NotEditable ? "当前位置明确不可输入" : "未确认可编辑输入框"); return; }
        profile = settings.Resolve(result.AppId);
        if (profile == null) Finish("app_disabled", "当前应用已禁用");
        if (verifying) verified = true;
    }
    public void Tick(long nowMs)
    {
        if (settings.TriggerAnywhere)
        {
            if (State == GestureState.Candidate)
            {
                if (!platform.PhysicalLeftDown) { Finish("released", "鼠标已松开"); return; }
                if (nowMs - origin.TimeMs >= settings.HoldMs) Activate(nowMs);
            }
            else if (State == GestureState.Holding)
            {
                if (!platform.PhysicalLeftDown) Finish("released", "快捷键已释放");
                else if (nowMs - activeSince >= profile!.MaxHoldSeconds * 1000L) Finish("max_hold", "已达到最长保持时间");
            }
            return;
        }
        if (State == GestureState.Idle && settings.Enabled) HoverTick(nowMs);
        if (State == GestureState.Candidate)
        {
            if (!platform.PhysicalLeftDown) { Finish("released", "鼠标已松开"); return; }
            if (!probeComplete && nowMs - probeStarted >= settings.ProbeTimeoutMs)
            {
                if (!settings.AllowUnconfirmedTargets) { Finish("probe_timeout", "输入框探测超时"); return; }
                CompleteProbe(Generation, new(Editability.Unknown, "", 0, "", "probe_timeout"));
                if (State != GestureState.Candidate) return;
            }
            if (target != null && !SameApp(target.AppId, platform.ForegroundAppId)) { Finish("focus_changed", "前台应用已改变"); return; }
            if (target != null && target.WindowId != platform.ForegroundWindowId) { Finish("focus_changed", "前台窗口已改变"); return; }
            if (probeComplete && profile != null && !verified && !verifying && nowMs - origin.TimeMs >= Math.Max(0, settings.HoldMs - Math.Max(80, settings.ProbeTimeoutMs)))
            {
                verifying = true; probeComplete = false; probeStarted = nowMs; Generation++;
                ProbeRequested?.Invoke(Generation, origin, settings.ProbeTimeoutMs);
            }
            if (probeComplete && profile != null && nowMs - origin.TimeMs >= settings.HoldMs)
            {
                if (!verified)
                {
                    if (!verifying) { verifying = true; probeComplete = false; probeStarted = nowMs; Generation++; ProbeRequested?.Invoke(Generation, origin, settings.ProbeTimeoutMs); }
                    return;
                }
                Set(GestureState.Activating, "activating", "正在保持快捷键");
                try
                {
                    session = platform.Begin(profile, settings.ReleaseMouseOnActivate);
                    if (!platform.PhysicalLeftDown) { Finish("released", "鼠标已松开"); return; }
                    activeSince = nowMs; lastActiveProbe = nowMs; activeProbePending = false; Set(GestureState.Holding, "shortcut_held", target!.Editability == Editability.Unknown ? "快捷键保持中 · 未确认输入框，按宽松模式触发" : "快捷键保持中 · 松开鼠标结束");
                }
                catch (Exception ex) { Finish("activation_failed", "无法开始：" + ex.Message); }
            }
        }
        else if (State == GestureState.Holding)
        {
            if (!platform.PhysicalLeftDown) Finish("released", "快捷键已释放");
            else if (nowMs - activeSince >= profile!.MaxHoldSeconds * 1000L) Finish("max_hold", "已达到最长保持时间");
            else if (!SameApp(target!.AppId, platform.ForegroundAppId) && !profile!.AllowedOverlayApps.Any(a => SameApp(a, platform.ForegroundAppId)))
                Finish("focus_changed", "切换应用，已结束快捷键");
            else if (SameApp(target!.AppId, platform.ForegroundAppId) && target.WindowId != platform.ForegroundWindowId)
                Finish("focus_changed", "切换窗口，已结束快捷键");
            else if (SameApp(target!.AppId, platform.ForegroundAppId))
            {
                if (activeProbePending && nowMs - lastActiveProbe >= settings.ProbeTimeoutMs)
                {
                    if (settings.AllowUnconfirmedTargets) { activeProbePending = false; Generation++; }
                    else Finish("probe_timeout", "无法继续确认输入目标，已结束快捷键");
                }
                else if (!activeProbePending && nowMs - lastActiveProbe >= 500)
                {
                    lastActiveProbe = nowMs; activeProbePending = true; Generation++;
                    ActiveRequested?.Invoke(Generation, origin, settings.ProbeTimeoutMs);
                }
            }
        }
    }
    private void HoverTick(long nowMs)
    {
        if (nowMs - lastHoverPoll < 80) return;
        lastHoverPoll = nowMs;
        (double X, double Y) point;
        try { point = platform.PointerPosition; } catch { ClearHover(); return; }
        if (point.X != hoverPoint.X || point.Y != hoverPoint.Y || (hoverTarget != null && !SameApp(hoverTarget.AppId, platform.ForegroundAppId))) ClearHover();
        if (hoverPending)
        {
            if (nowMs - hoverStarted < Math.Min(120, settings.ProbeTimeoutMs)) return;
            hoverPending = false; hoverGeneration++;
        }
        if (hoverTarget != null && nowMs - hoverCompleted < 600) return;
        hoverPoint = new(PointerKind.Move, point.X, point.Y, nowMs);
        hoverStarted = nowMs; hoverPending = true; hoverGeneration++;
        HoverRequested?.Invoke(hoverGeneration, hoverPoint, Math.Min(120, settings.ProbeTimeoutMs));
    }
    public void CompleteHoverProbe(long generation, TargetSnapshot result, long nowMs)
    {
        if (generation != hoverGeneration || State != GestureState.Idle || !settings.Enabled) return;
        hoverPending = false;
        if (string.IsNullOrEmpty(result.AppId)) result = result with { AppId = platform.ForegroundAppId, WindowId = platform.ForegroundWindowId };
        if (!SameApp(result.AppId, platform.ForegroundAppId)) return;
        bool changed = hoverTarget == null || hoverTarget.ControlId != result.ControlId || hoverTarget.Reason != result.Reason || hoverTarget.Editability != result.Editability;
        hoverTarget = result; hoverCompleted = nowMs;
        if (changed) Changed?.Invoke(new(State, "hover_ready", result.Editability == Editability.Editable ? "鼠标处是输入框 · 长按即可说话" : Allowed(result) ? "未确认输入框 · 允许长按说话" : "当前位置不可触发语音", result));
    }
    private void ClearHover() { hoverGeneration++; hoverPending = false; hoverTarget = null; }
    private void Activate(long nowMs)
    {
        Set(GestureState.Activating, "activating", "正在保持快捷键");
        try
        {
            long dispatchStart = Environment.TickCount64;
            session = platform.Begin(profile!, settings.ReleaseMouseOnActivate);
            long dispatchMs = Environment.TickCount64 - dispatchStart;
            if (!platform.PhysicalLeftDown) { Finish("released", "鼠标已松开"); return; }
            activeSince = nowMs;
            State = GestureState.Holding;
            long triggerMs = Math.Max(0, nowMs - origin.TimeMs) + dispatchMs;
            Changed?.Invoke(new(State, "shortcut_held", $"快捷键已发送 · {triggerMs} ms · 松开鼠标结束", target, TriggerLatencyMs: triggerMs, DispatchLatencyMs: dispatchMs));
        }
        catch (Exception ex) { Finish("activation_failed", "无法开始：" + ex.Message); }
    }
    public void Stop() { ClearHover(); Finish("stopped", "工具已停止"); platform.EmergencyRelease(); }
    private static bool SameApp(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private bool Allowed(TargetSnapshot value) => value.Editability == Editability.Editable ||
        (settings.AllowUnconfirmedTargets && value.Editability == Editability.Unknown && !string.IsNullOrEmpty(value.AppId) &&
        !SameApp(value.AppId, "HoldSpeak.App.exe") && !SameApp(value.AppId, "org.holdspeak.app") &&
        value.Reason is not ("own_window" or "focus_mismatch" or "no_foreground_app" or "password_field" or "disabled" or "read_only"));
    private bool IdentityChanged(TargetSnapshot previous, TargetSnapshot next) => !SameApp(previous.AppId, next.AppId) ||
        previous.WindowId != next.WindowId || (previous.Editability == Editability.Editable && next.Editability == Editability.Editable && previous.ControlId != next.ControlId);
    private TargetSnapshot WithContext(TargetSnapshot value)
    {
        if (!string.IsNullOrEmpty(value.AppId) || requestContext == null) return value;
        if (!SameApp(requestContext.AppId, platform.ForegroundAppId) || requestContext.WindowId != platform.ForegroundWindowId)
            return value with { Editability = Editability.NotEditable, Reason = "focus_mismatch" };
        return value with { AppId = requestContext.AppId, WindowId = requestContext.WindowId };
    }
    private string InterruptionReason(TargetSnapshot result)
    {
        if (result.Reason == "password_field") return "焦点进入密码框";
        if (result.Reason == "probe_timeout") return "输入框响应超时";
        if (target != null && !SameApp(result.AppId, target.AppId)) return "前台应用已改变";
        if (result.Editability != Editability.Editable) return $"无法确认当前输入框（{result.Reason}）";
        return "焦点已切换到其他输入框";
    }
    private void Finish(string code, string message, TargetSnapshot? observed = null)
    {
        Generation++;
        try { session?.End(); }
        catch (Exception ex) { code = "release_failed"; message = "结束失败：" + ex.Message; platform.EmergencyRelease(); }
        finally { session?.Dispose(); session = null; }
        Set(settings.Enabled ? (platform.PhysicalLeftDown ? GestureState.AwaitRelease : GestureState.Idle) : GestureState.Suspended, code, message, observed);
    }
    private void Set(GestureState state, string code, string message, TargetSnapshot? observed = null)
    {
        State = state; Changed?.Invoke(new(state, code, message, target, observed));
    }
}
