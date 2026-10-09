using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HoldSpeak.Core;

public readonly record struct ProbePoint(double X, double Y, bool Warmup = false, bool RequireFocus = true, bool FocusOnly = false);
// Blocking accessibility providers are isolated behind a replaceable child process.
public sealed class ProcessProbe : ITargetProbe, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? worker;
    private bool disposed;
    public static ProcessStartInfo ChildStartInfo(string argument)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位程序路径。");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
        info.ArgumentList.Add(argument); return info;
    }
    public async Task<TargetSnapshot> ProbeAsync(double x, double y, CancellationToken cancellationToken)
        => await RequestAsync(new(x, y), cancellationToken);
    public async Task<TargetSnapshot> HoverAsync(double x, double y, CancellationToken cancellationToken)
        => await RequestAsync(new(x, y, RequireFocus: false), cancellationToken);
    public async Task<TargetSnapshot> ActiveAsync(double x, double y, CancellationToken cancellationToken)
        => await RequestAsync(new(x, y, FocusOnly: true), cancellationToken);
    public async Task WarmupAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(new(0, 0, true), cancellationToken);
        if (result.Reason != "ready") throw new IOException("辅助功能接口预热失败。");
    }
    private async Task<TargetSnapshot> RequestAsync(ProbePoint point, CancellationToken cancellationToken)
    {
        // Hover never queues behind a foreground confirmation. A caller timing out
        // must not repeatedly kill a slow but healthy UIA worker.
        if (!point.RequireFocus && !point.Warmup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!gate.Wait(0)) return new(Editability.Unknown, "", 0, "", "hover_busy");
        }
        else await gate.WaitAsync(cancellationToken);
        return await ExchangeAsync(point).WaitAsync(cancellationToken);
    }
    private async Task<TargetSnapshot> ExchangeAsync(ProbePoint point)
    {
        using var deadline = new CancellationTokenSource(point.Warmup ? 5000 : 1000);
        var cancellationToken = deadline.Token;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker == null || worker.HasExited)
            {
                Reset(); var info = ChildStartInfo("--probe-stdio");
                info.RedirectStandardInput = true; info.RedirectStandardOutput = true;
                info.StandardInputEncoding = new UTF8Encoding(false); info.StandardOutputEncoding = Encoding.UTF8;
                worker = Process.Start(info) ?? throw new IOException("无法启动输入框探测进程。");
            }
            await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(point).AsMemory(), cancellationToken);
            await worker.StandardInput.FlushAsync(cancellationToken);
            var line = await worker.StandardOutput.ReadLineAsync(cancellationToken) ?? throw new IOException("探测进程已退出。");
            return JsonSerializer.Deserialize<TargetSnapshot>(line) ?? throw new IOException("无效探测结果。");
        }
        catch { Reset(); throw; }
        finally { gate.Release(); }
    }
    private void Reset()
    {
        if (worker != null)
        {
            try { if (!worker.HasExited) worker.Kill(true); } catch (InvalidOperationException) { }
            worker.Dispose(); worker = null;
        }
    }
    public void Dispose() { gate.Wait(); try { disposed = true; Reset(); } finally { gate.Release(); } }
}
