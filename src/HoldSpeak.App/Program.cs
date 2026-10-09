using System.Text;
using System.Text.Json;
using Avalonia;
using HoldSpeak.Core;
using HoldSpeak.Mac;
#if WINDOWS
using HoldSpeak.Windows;
#endif

namespace HoldSpeak.App;

internal static class Program
{
    internal static bool SmokeTest;
    [STAThread]
    public static int Main(string[] args)
    {
        SmokeTest = args.Contains("--smoke-test");
        if (args.Contains("--benchmark-startup"))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var probe = new ProcessProbe(); using var timeout = new CancellationTokenSource(5000);
            var recoveryTask = Task.Run(() => new RecoveryClient());
            bool detection = args.Contains("--with-detection");
            var probeTask = detection ? probe.WarmupAsync(timeout.Token) : Task.CompletedTask;
            try
            {
                Task.WhenAll(recoveryTask, probeTask).GetAwaiter().GetResult();
                using var guardian = recoveryTask.Result;
                Console.WriteLine(JsonSerializer.Serialize(new { readyMs = watch.ElapsedMilliseconds, scope = "background prerequisites ready; excludes UI rendering", detectionEnabled = detection }));
                return 0;
            }
            catch { if (recoveryTask.IsCompletedSuccessfully) recoveryTask.Result.Dispose(); throw; }
        }
        if (args.Contains("--list-applications"))
        {
            Console.WriteLine(JsonSerializer.Serialize(ListApplications(), ConfigurationStore.Json)); return 0;
        }
#if WINDOWS
        if (args.Contains("--inspect-desktop"))
        {
            string? report = null; Exception? error = null;
            string? onlyApp = args.SkipWhile(a => a != "--only-app").Skip(1).FirstOrDefault();
            var inspect = new Thread(() => { try { report = JsonSerializer.Serialize(DesktopInspector.InspectKnownApplications(onlyApp), ConfigurationStore.Json); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            inspect.SetApartmentState(ApartmentState.MTA); inspect.Start();
            if (!inspect.Join(20000)) return 3;
            if (error != null) return 4;
            Console.WriteLine(report); return 0;
        }
#endif
        if (args.Contains("--recovery-stdio")) return RecoveryWorker();
        if (args.Contains("--probe-stdio"))
        {
            if (OperatingSystem.IsMacOS()) { ProbeWorker(); return 0; }
            var thread = new Thread(ProbeWorker) { IsBackground = false };
            if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.MTA);
            thread.Start(); thread.Join(); return 0;
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) { Console.Error.WriteLine("HoldSpeak supports Windows and macOS."); return 1; }
        // A second engine could duplicate global shortcuts. Lock only this user's configuration directory.
        Directory.CreateDirectory(AppPaths.Directory);
        FileStream instance;
        try { instance = new(Path.Combine(AppPaths.Directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 2; }
        using (instance) BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesktopApp>().UsePlatformDetect();
    private static int RecoveryWorker()
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        Key[] keys = [];
        try
        {
            while (true)
            {
                // Console.In is a synchronized reader; ReadLineAsync can block synchronously.
                // Keep its one outstanding read on a background worker so the watchdog deadline runs.
                var pending = Task.Run(Console.ReadLine);
                if (!pending.Wait(2000)) break;
                var line = pending.Result; if (line == null) break;
                var message = JsonSerializer.Deserialize<RecoveryMessage>(line) ?? throw new InvalidDataException();
                if (message.Keys.Length > 5 || message.Keys.Any(k => !Enum.IsDefined(k))) throw new InvalidDataException();
                keys = message.Keys; Console.WriteLine("ok");
            }
        }
        catch (IOException) { }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { }
        finally
        {
            if (keys.Length > 0)
            {
                try
                {
#if WINDOWS
                    WindowsPlatform.RecoverKeys(keys);
#else
                    MacPlatform.RecoverKeys(keys);
#endif
                }
                catch { /* Best effort: protected desktops and revoked permissions can reject release. */ }
            }
        }
        return 0;
    }
    private static void ProbeWorker()
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            TargetSnapshot result;
            try
            {
                var point = JsonSerializer.Deserialize<ProbePoint>(line);
#if WINDOWS
                if (point.Warmup) { WindowsProbe.Warmup(); result = new(Editability.Unknown, "", 0, "", "ready"); }
                else result = WindowsProbe.Inspect(point.X, point.Y, point.RequireFocus, point.FocusOnly);
#else
                if (point.Warmup) { MacProbe.Warmup(); result = new(Editability.Unknown, "", 0, "", "ready"); }
                else result = MacProbe.Inspect(point.X, point.Y, point.RequireFocus, point.FocusOnly);
#endif
            }
            catch { result = new(Editability.Unknown, "", 0, "", "probe_failed"); }
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
    internal static IDesktopPlatform CreatePlatform()
    {
#if WINDOWS
        return new WindowsPlatform();
#else
        return new MacPlatform();
#endif
    }
    internal static ApplicationOption[] ListApplications()
    {
#if WINDOWS
        return WindowsApplications.List();
#else
        return MacApplications.List();
#endif
    }
}

internal static class AppPaths
{
    internal static readonly string Directory = Path.GetFullPath(Environment.GetEnvironmentVariable("HOLDSPEAK_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldSpeak"));
    internal static string Settings => Path.Combine(Directory, "settings.json");
}
