using System.Diagnostics;
using System.ComponentModel;
using HoldSpeak.Core;

namespace HoldSpeak.Windows;

public static class WindowsApplications
{
    public static ApplicationOption[] List()
    {
        using var current = Process.GetCurrentProcess();
        int session = current.SessionId;
        var applications = new Dictionary<string, ApplicationOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != session || process.MainWindowHandle == 0) continue;
                    string name = process.ProcessName;
                    string id = name + ".exe";
                    applications.TryAdd(id, new(id, name));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException) { }
            }
        }
        return applications.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
