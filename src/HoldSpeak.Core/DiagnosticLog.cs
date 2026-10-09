namespace HoldSpeak.Core;

public sealed record DiagnosticEvent(DateTimeOffset Time, GestureState State, string Code, string? AppId, string? ControlType, string? TargetReason, string? ObservedReason, Editability? ObservedEditability, long? TriggerLatencyMs, long? DispatchLatencyMs);
public sealed record DiagnosticReport(string Version, string OperatingSystem, int HoldMs, int ProbeTimeoutMs, DiagnosticEvent[] Events);

// Status messages and raw window/control identities are deliberately excluded from exported data.
public sealed class DiagnosticLog
{
    private readonly Queue<DiagnosticEvent> events = new();
    public void Add(Status status)
    {
        events.Enqueue(new(DateTimeOffset.Now, status.State, status.Code,
            string.IsNullOrEmpty(status.ObservedTarget?.AppId) ? status.Target?.AppId : status.ObservedTarget.AppId,
            string.IsNullOrEmpty(status.ObservedTarget?.ControlType) ? status.Target?.ControlType : status.ObservedTarget.ControlType,
            status.Target?.Reason, status.ObservedTarget?.Reason, status.ObservedTarget?.Editability, status.TriggerLatencyMs, status.DispatchLatencyMs));
        while (events.Count > 80) events.Dequeue();
    }
    public DiagnosticReport Report(string version, string operatingSystem, Settings settings) => new(version, operatingSystem, settings.HoldMs, settings.ProbeTimeoutMs, events.ToArray());
}
