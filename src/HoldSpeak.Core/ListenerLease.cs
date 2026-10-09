namespace HoldSpeak.Core;

// Independent of the portable configuration folder. The OS releases this handle
// after process termination; the persistent file must never be deleted as cleanup.
public sealed class ListenerLease : IDisposable
{
    private FileStream? handle;
    private ListenerLease(FileStream handle) => this.handle = handle;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoldSpeak", "runtime");

    public static ListenerLease Acquire(string? directory = null)
    {
        Directory.CreateDirectory(directory ?? DefaultDirectory);
        try
        {
            return new(new FileStream(Path.Combine(directory ?? DefaultDirectory, "listener.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex)
        {
            throw new IOException("另一个 HoldSpeak 实例正在占用输入监听，或监听锁无法打开。请完全退出旧实例后重试。", ex);
        }
    }

    public void Dispose() => Interlocked.Exchange(ref handle, null)?.Dispose();
}
