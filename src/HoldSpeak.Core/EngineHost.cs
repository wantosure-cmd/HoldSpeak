using System.Threading.Channels;

namespace HoldSpeak.Core;

public sealed class EngineHost : IAsyncDisposable
{
    private readonly IDesktopPlatform platform;
    private readonly GestureEngine engine;
    private readonly Channel<Action> queue = Channel.CreateUnbounded<Action>(new() { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task loop;
    private readonly Task ticker;
    private bool disposed, failed;
    private Settings currentSettings;
    private PointerEvent producerOrigin;
    private bool moveEscaped;
    private int tickQueued;
    public bool IsFaulted => Volatile.Read(ref failed);
    public event Action<Status>? Changed;
    public event Action<string>? RecoveryRequested;
    public EngineHost(IDesktopPlatform platform, Settings settings)
    {
        this.platform = platform; currentSettings = settings; engine = new(platform, settings);
        engine.Changed += status => Changed?.Invoke(status);
        engine.ProbeRequested += (id, pointer, timeout) => _ = Probe(id, pointer, timeout);
        engine.HoverRequested += (id, pointer, timeout) => _ = ProbeHover(id, pointer, timeout);
        engine.ActiveRequested += (id, pointer, timeout) => _ = Probe(id, pointer, timeout, true);
        platform.Pointer += OnPointer;
        platform.Faulted += OnFault;
        platform.RecoveryRequested += OnRecoveryRequested;
        loop = Task.Run(async () =>
        {
            await foreach (var action in queue.Reader.ReadAllAsync())
            {
                try { action(); }
                catch
                {
                    SuspendAfterFault("engine_failed", "执行异常，已暂停；请重新启用工具");
                }
            }
        });
        ticker = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            try
            {
                while (await timer.WaitForNextTickAsync(lifetime.Token))
                    if (Interlocked.CompareExchange(ref tickQueued, 1, 0) == 0)
                        Post(() => { Interlocked.Exchange(ref tickQueued, 0); if (!failed) engine.Tick(Environment.TickCount64); });
            }
            catch (OperationCanceledException) { }
        });
    }
    public void Start() { platform.SetDetectionEnabled(!currentSettings.TriggerAnywhere); platform.Start(); }
    public void Configure(Settings settings) => Post(() => { if (failed) return; Volatile.Write(ref currentSettings, settings); engine.Configure(settings); });
    private void OnFault(string message) => Post(() => SuspendAfterFault("platform_failed", message));
    private void OnRecoveryRequested(string message) => Post(() => RecoveryRequested?.Invoke(message));
    private void SuspendAfterFault(string code, string message)
    {
        if (failed) return;
        Volatile.Write(ref failed, true);
        currentSettings = currentSettings with { Enabled = false };
        try { engine.Configure(currentSettings); } catch { }
        try { platform.EmergencyRelease(); } catch { }
        Changed?.Invoke(new(GestureState.Suspended, code, message));
    }
    public void StopGesture() => Post(() => engine.Handle(new(PointerKind.Cancel, 0, 0, Environment.TickCount64)));
    private void OnPointer(PointerEvent pointer)
    {
        // Mouse events have one native producer. Keep the first threshold crossing,
        // rather than the last position, so moving out and back cannot erase a drag.
        if (pointer.Kind == PointerKind.Down) { producerOrigin = pointer; moveEscaped = false; }
        if (pointer.Kind == PointerKind.Move)
        {
            if (moveEscaped) return;
            double radius = Volatile.Read(ref currentSettings).MovementDip * Math.Max(0.5, producerOrigin.Scale);
            if (Math.Pow(pointer.X - producerOrigin.X, 2) + Math.Pow(pointer.Y - producerOrigin.Y, 2) <= radius * radius) return;
            moveEscaped = true;
        }
        Post(() => { if (!failed) engine.Handle(pointer); });
    }
    private void Post(Action action) => queue.Writer.TryWrite(action);
    private async Task Probe(long id, PointerEvent pointer, int timeout, bool active = false)
    {
        TargetSnapshot result;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(timeout);
        try { result = active ? await platform.Probe.ActiveAsync(pointer.X, pointer.Y, cancellation.Token) : await platform.Probe.ProbeAsync(pointer.X, pointer.Y, cancellation.Token); }
        catch (OperationCanceledException) { result = new(Editability.Unknown, "", 0, "", "probe_timeout"); }
        catch { result = new(Editability.Unknown, "", 0, "", "probe_failed"); }
        Post(() => { if (!failed) engine.CompleteProbe(id, result); });
    }
    private async Task ProbeHover(long id, PointerEvent pointer, int timeout)
    {
        TargetSnapshot result;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(timeout);
        try { result = await platform.Probe.HoverAsync(pointer.X, pointer.Y, cancellation.Token); }
        catch (OperationCanceledException) { result = new(Editability.Unknown, "", 0, "", "hover_timeout"); }
        catch { result = new(Editability.Unknown, "", 0, "", "hover_failed"); }
        Post(() => { if (!failed) engine.CompleteHoverProbe(id, result, Environment.TickCount64); });
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        platform.Pointer -= OnPointer; platform.Faulted -= OnFault; lifetime.Cancel(); await ticker;
        platform.RecoveryRequested -= OnRecoveryRequested;
        Post(engine.Stop); queue.Writer.TryComplete(); await loop;
        platform.Dispose(); lifetime.Dispose();
    }
}
