using HoldSpeak.Core;

namespace HoldSpeak.Mac;

public static class MacApplications
{
    public static ApplicationOption[] List()
    {
        MacNative.EnsureAppKit();
        var pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            var workspace = MacNative.objc_msgSend(MacNative.objc_getClass("NSWorkspace"), MacNative.sel_registerName("sharedWorkspace"));
            var array = MacNative.objc_msgSend(workspace, MacNative.sel_registerName("runningApplications"));
            nuint count = MacNative.objc_msgSendCount(array, MacNative.sel_registerName("count"));
            var applications = new Dictionary<string, ApplicationOption>(StringComparer.OrdinalIgnoreCase);
            for (nuint index = 0; index < Math.Min(count, (nuint)1024); index++)
            {
                var app = MacNative.objc_msgSendIndex(array, MacNative.sel_registerName("objectAtIndex:"), index);
                if (MacNative.objc_msgSendInt(app, MacNative.sel_registerName("processIdentifier")) == Environment.ProcessId) continue;
                if (MacNative.objc_msgSendCount(app, MacNative.sel_registerName("activationPolicy")) > 1) continue;
                string id = MacNative.ReadString(MacNative.objc_msgSend(app, MacNative.sel_registerName("bundleIdentifier")));
                if (string.IsNullOrWhiteSpace(id)) continue;
                string name = MacNative.ReadString(MacNative.objc_msgSend(app, MacNative.sel_registerName("localizedName")));
                applications.TryAdd(id, new(id, string.IsNullOrWhiteSpace(name) ? id : name));
            }
            return applications.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { MacNative.objc_autoreleasePoolPop(pool); }
    }
}
