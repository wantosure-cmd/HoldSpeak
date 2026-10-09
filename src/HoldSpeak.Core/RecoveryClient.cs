using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HoldSpeak.Core;

public sealed record RecoveryMessage(Key[] Keys);

// Only holds key-release responsibility. It never starts or toggles a voice session.
public sealed class RecoveryClient : IDisposable
{
    private readonly object gate = new();
    private readonly Process worker;
    private readonly Timer heartbeat;
    private Key[] keys = [];
    private bool failed, disposed;
    public event Action? Failed;
    public RecoveryClient()
    {
        var info = ProcessProbe.ChildStartInfo("--recovery-stdio");
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true;
        info.StandardInputEncoding = new UTF8Encoding(false); info.StandardOutputEncoding = Encoding.UTF8;
        worker = Process.Start(info) ?? throw new IOException("无法启动按键恢复进程。");
        try { Exchange([]); }
        catch
        {
            // Failed startup never leaves an unmonitored guardian behind.
            worker.StandardInput.Close(); worker.WaitForExit(3000); worker.Dispose(); throw;
        }
        heartbeat = new Timer(_ => Beat(), null, 300, 300);
    }
    public void Arm(Key[] owned) { lock (gate) { if (failed || disposed) throw new IOException("恢复进程不可用，请重新启用工具。"); Exchange(owned); keys = owned; } }
    public void Clear() { lock (gate) { keys = []; if (!failed && !disposed) Exchange([]); } }
    private void Exchange(Key[] value)
    {
        worker.StandardInput.WriteLine(JsonSerializer.Serialize(new RecoveryMessage(value)));
        worker.StandardInput.Flush();
        var ack = worker.StandardOutput.ReadLineAsync();
        if (!ack.Wait(1000) || ack.Result != "ok") throw new IOException("恢复进程无响应。");
    }
    private void Beat()
    {
        bool notify = false;
        lock (gate)
        {
            if (failed || disposed) return;
            try { Exchange(keys); }
            catch { failed = true; notify = true; }
        }
        if (notify) Failed?.Invoke();
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; heartbeat.Dispose();
            // Outstanding keys must stay in the child's journal if normal release failed.
            try { if (!failed && keys.Length == 0) Exchange([]); } catch (IOException) { }
            worker.StandardInput.Close();
        }
        // Do not kill a guardian before it has had a chance to release its lease.
        worker.WaitForExit(3000); worker.Dispose();
    }
}
