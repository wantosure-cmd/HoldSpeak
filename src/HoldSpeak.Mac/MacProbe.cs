using HoldSpeak.Core;

namespace HoldSpeak.Mac;

public static class MacProbe
{
    public static void Warmup()
    {
        _ = MacNative.Foreground(); var system = MacNative.AXUIElementCreateSystemWide(); MacNative.CFRelease(system);
    }
    private static nint Attribute(nint element, string name)
    {
        var key = MacNative.String(name);
        try { return MacNative.AXUIElementCopyAttributeValue(element, key, out var value) == 0 ? value : 0; }
        finally { MacNative.CFRelease(key); }
    }
    private static string Text(nint element, string name)
    {
        var value = Attribute(element, name); if (value == 0) return "";
        try { return MacNative.ReadString(value); } finally { MacNative.CFRelease(value); }
    }
    private static bool Settable(nint element, string name)
    {
        var key = MacNative.String(name);
        try { return MacNative.AXUIElementIsAttributeSettable(element, key, out bool result) == 0 && result; }
        finally { MacNative.CFRelease(key); }
    }
    private static bool EnableElectronAccessibility(nint application)
    {
        // Electron exposes this explicit opt-in to external assistive technology.
        // Native apps that do not advertise it are never sent a setter request.
        const string name = "AXManualAccessibility";
        if (!Settable(application, name)) return false;
        var previous = Attribute(application, name);
        if (previous != 0)
        {
            try { if (MacNative.CFEqual(previous, MacNative.True)) return false; }
            finally { MacNative.CFRelease(previous); }
        }
        var key = MacNative.String(name);
        try { return MacNative.AXUIElementSetAttributeValue(application, key, MacNative.True) == 0; }
        finally { MacNative.CFRelease(key); }
    }
    public static TargetSnapshot Inspect(double x, double y, bool requireFocus = true, bool focusOnly = false)
    {
        var app = MacNative.Foreground();
        TargetSnapshot Reject(string reason, string role = "") => new(Editability.Unknown, app.Id, app.Pid, role, reason);
        if (!MacNative.AXIsProcessTrusted()) return Reject("access_denied");
        if (app.Pid <= 0) return Reject("no_foreground_app");
        if (app.Pid == Environment.ProcessId || app.Id == "org.holdspeak.app") return Reject("own_window");
        var system = MacNative.AXUIElementCreateSystemWide();
        nint pointed = 0, focused = 0, application = 0;
        try
        {
            MacNative.AXUIElementSetMessagingTimeout(system, 0.15f);
            application = MacNative.AXUIElementCreateApplication(app.Pid);
            MacNative.AXUIElementSetMessagingTimeout(application, 0.15f);
            bool enabledTree = EnableElectronAccessibility(application);
            if (focusOnly) pointed = Attribute(application, "AXFocusedUIElement");
            else if (MacNative.AXUIElementCopyElementAtPosition(system, (float)x, (float)y, out pointed) != 0) return Reject(enabledTree ? "accessibility_enabled_retry" : "no_element");
            if (pointed == 0) return Reject("no_focus");
            if (MacNative.AXUIElementGetPid(pointed, out int pid) != 0 || pid != app.Pid) return Reject("focus_mismatch");
            focused = Attribute(application, "AXFocusedUIElement"); if (requireFocus && focused == 0) return Reject(enabledTree ? "accessibility_enabled_retry" : "no_focus");
            if (requireFocus && Text(focused, "AXSubrole") == "AXSecureTextField") return new(Editability.NotEditable, app.Id, app.Pid, Text(focused, "AXRole"), "password_field");
            for (int depth = 0; pointed != 0 && depth < 6; depth++)
            {
                string role = Text(pointed, "AXRole");
                if (Text(pointed, "AXSubrole") == "AXSecureTextField") return new(Editability.NotEditable, app.Id, app.Pid, role, "password_field");
                if (role is "AXButton" or "AXCheckBox" or "AXRadioButton" or "AXMenuItem" or "AXSlider" or "AXScrollBar")
                    return new(Editability.NotEditable, app.Id, app.Pid, role, "non_input_control");
                if ((!requireFocus || MacNative.CFEqual(pointed, focused)) && role is "AXTextField" or "AXTextArea" or "AXComboBox")
                {
                    var enabled = Attribute(pointed, "AXEnabled");
                    bool yes = enabled != 0 && MacNative.CFBooleanGetValue(enabled); if (enabled != 0) MacNative.CFRelease(enabled);
                    if (enabled != 0 && !yes) return new(Editability.NotEditable, app.Id, app.Pid, role, "disabled");
                    if (yes && (Settable(pointed, "AXValue") || Settable(pointed, "AXSelectedText")))
                        return new(Editability.Editable, app.Id, app.Pid, role, "editable_ax", MacNative.CFHash(pointed).ToString());
                }
                var parent = Attribute(pointed, "AXParent"); MacNative.CFRelease(pointed); pointed = parent;
            }
            return Reject(enabledTree ? "accessibility_enabled_retry" : "not_confirmed_editable");
        }
        finally
        {
            if (pointed != 0) MacNative.CFRelease(pointed); if (focused != 0) MacNative.CFRelease(focused);
            if (application != 0) MacNative.CFRelease(application); MacNative.CFRelease(system);
        }
    }
}
