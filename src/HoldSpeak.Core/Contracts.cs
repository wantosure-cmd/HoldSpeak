namespace HoldSpeak.Core;

public enum Editability { Editable, NotEditable, Unknown }
public enum GestureState { Idle, Candidate, Activating, Holding, AwaitRelease, Suspended }
public enum PointerKind { Down, Up, Move, Cancel }
public readonly record struct PointerEvent(PointerKind Kind, double X, double Y, long TimeMs, double Scale = 1, bool MultiClick = false);
public sealed record TargetSnapshot(Editability Editability, string AppId, long WindowId, string ControlType, string Reason, string ControlId = "");
public sealed record Status(GestureState State, string Code, string Message, TargetSnapshot? Target = null, TargetSnapshot? ObservedTarget = null, long? TriggerLatencyMs = null, long? DispatchLatencyMs = null);
public sealed record ApplicationOption(string AppId, string Name)
{
    public override string ToString() => Name.Equals(AppId, StringComparison.OrdinalIgnoreCase) ? AppId : $"{Name} · {AppId}";
}
public interface ITargetProbe
{
    Task<TargetSnapshot> ProbeAsync(double x, double y, CancellationToken cancellationToken);
    Task<TargetSnapshot> HoverAsync(double x, double y, CancellationToken cancellationToken) => ProbeAsync(x, y, cancellationToken);
    Task<TargetSnapshot> ActiveAsync(double x, double y, CancellationToken cancellationToken) => ProbeAsync(x, y, cancellationToken);
}
public interface IInputSession : IDisposable
{
    void End();
}
public interface IDesktopPlatform : IDisposable
{
    bool IsMac { get; }
    bool PhysicalLeftDown { get; }
    string ForegroundAppId { get; }
    long ForegroundWindowId => 0;
    string PermissionStatus { get; }
    (double X, double Y) PointerPosition { get; }
    event Action<PointerEvent>? Pointer;
    event Action<string>? Faulted;
    event Action<string>? RecoveryRequested { add { } remove { } }
    ITargetProbe Probe { get; }
    void Start();
    void SetDetectionEnabled(bool enabled) { }
    IInputSession Begin(VoiceProfile profile, bool releaseMouse);
    void EmergencyRelease();
}
