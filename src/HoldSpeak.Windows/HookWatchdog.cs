using System.Runtime.InteropServices;

namespace HoldSpeak.Windows;

internal sealed class HookWatchdog : IDisposable
{
    internal static readonly nuint Marker = Native.Marker + 1;
    private readonly Action lost;
    private readonly Timer? timer;
    private long acknowledged;
    private int checking, failed, disposed, misses;
    internal HookWatchdog(Action lost, bool schedule = true)
    {
        this.lost = lost;
        if (schedule) timer = new(_ => _ = CheckOnceAsync(), null, 2000, 2000);
    }
    internal void Observe() => Interlocked.Increment(ref acknowledged);
    internal async Task CheckOnceAsync()
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref failed) != 0 || Interlocked.CompareExchange(ref checking, 1, 0) != 0) return;
        try
        {
            if (!Native.IsInteractiveDesktop()) { misses = 0; return; }
            long before = Volatile.Read(ref acknowledged);
            // No displacement, clicks or keys; the listener consumes its marker.
            if (!Native.GetCursorPos(out var point)) return;
            int left = Native.GetSystemMetrics(76), top = Native.GetSystemMetrics(77);
            int width = Native.GetSystemMetrics(78), height = Native.GetSystemMetrics(79);
            if (width <= 0 || height <= 0) return;
            int x = (int)Math.Clamp(((point.X - left) * 65536L + 32768) / width, 0, 65535);
            int y = (int)Math.Clamp(((point.Y - top) * 65536L + 32768) / height, 0, 65535);
            // Absolute movement to the current pixel is delivered even when a
            // relative (0,0) event would be optimized away by Windows.
            var marker = new Native.Input { Type = 0, Union = new() { Mouse = new() { X = x, Y = y, Flags = 1 | 0x2000 | 0x4000 | 0x8000, Extra = Marker } } };
            // Never block the hook-owning message loop while sending its marker.
            if (await Task.Run(() => Native.SendInput(1, [marker], Marshal.SizeOf<Native.Input>())) != 1) { misses = 0; return; }
            await Task.Delay(500);
            if (Volatile.Read(ref disposed) != 0 || !Native.IsInteractiveDesktop()) return;
            if (Volatile.Read(ref acknowledged) != before) { misses = 0; return; }
            if (++misses >= 2 && Interlocked.Exchange(ref failed, 1) == 0) lost();
        }
        catch { /* Health checks never throw across native/timer callbacks. */ }
        finally { Volatile.Write(ref checking, 0); }
    }
    public void Dispose() { Interlocked.Exchange(ref disposed, 1); timer?.Dispose(); }
}
