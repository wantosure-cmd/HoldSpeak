using Microsoft.Win32;

namespace HoldSpeak.Windows;

internal sealed class WindowsSessionMonitor : IDisposable
{
    private readonly Action<string> interrupt;
    private readonly Action<string>? resume;
    private int disposed;
    internal WindowsSessionMonitor(Action<string> interrupt, Action<string>? resume = null)
    {
        this.interrupt = interrupt;
        this.resume = resume;
        SystemEvents.SessionSwitch += SessionChanged;
        try { SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.SessionEnding += SessionEnding; }
        catch { SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged; throw; }
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs args)
    {
        if (args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Notify("用户会话已锁定或断开，已暂停；返回后将自动恢复");
        else if (args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Resume("用户会话已返回");
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) Notify("系统即将休眠，已暂停；唤醒后将自动恢复");
        else if (args.Mode == PowerModes.Resume) Resume("系统已唤醒");
    }
    private void SessionEnding(object sender, SessionEndingEventArgs args) => Notify("会话即将结束，已暂停并尝试释放快捷键");
    private void Notify(string message) { if (Volatile.Read(ref disposed) == 0) interrupt(message); }
    private void Resume(string message) { if (Volatile.Read(ref disposed) == 0) resume?.Invoke(message); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionEnding -= SessionEnding;
    }
}
