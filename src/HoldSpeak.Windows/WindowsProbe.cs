using System.Windows;
using System.Windows.Automation;
using HoldSpeak.Core;

namespace HoldSpeak.Windows;

public static class WindowsProbe
{
    public static void Warmup() => _ = AutomationElement.RootElement;
    internal static bool IsWebDocumentContainer(AutomationElement node)
    {
        if (node.Current.ControlType != ControlType.Document || node.Current.FrameworkId != "Chrome") return false;
        var parent = TreeWalker.ControlViewWalker.GetParent(node);
        if (parent?.Current.ControlType != ControlType.Pane) return false;
        // Feishu's CEF MultiWebView reports the whole root document read-only,
        // even when its chat composer can accept keyboard input.
        return parent.Current.ClassName is "MultiWebView" or "Chrome_RenderWidgetHostHWND";
    }
    public static TargetSnapshot Inspect(double x, double y, bool requireFocus = true, bool focusOnly = false)
    {
        var window = Native.GetForegroundWindow(); string app = WindowsPlatform.AppId(window);
        TargetSnapshot Reject(string reason, string type = "") => new(Editability.Unknown, app, window.ToInt64(), type, reason);
        if (app.Equals("HoldSpeak.App.exe", StringComparison.OrdinalIgnoreCase)) return new(Editability.NotEditable, app, window.ToInt64(), "", "own_window");
        try
        {
            var focused = AutomationElement.FocusedElement;
            var pointed = focusOnly ? focused : AutomationElement.FromPoint(new Point(x, y));
            var leafType = pointed?.Current.ControlType;
            if (pointed == null || (requireFocus && focused == null)) return Reject("no_element");
            if (requireFocus && focused!.Current.IsPassword) return new(Editability.NotEditable, app, window.ToInt64(), focused.Current.ControlType.ProgrammaticName, "password_field");
            Native.GetWindowThreadProcessId(window, out uint pid);
            if ((requireFocus && focused!.Current.ProcessId != pid) || pointed.Current.ProcessId != pid) return Reject("focus_mismatch");
            if (IsNonInput(leafType)) return new(Editability.NotEditable, app, window.ToInt64(), leafType!.ProgrammaticName, "non_input_control");
            var walker = TreeWalker.ControlViewWalker;
            // Chromium's suggestion popup places a TopBackgroundView over the location bar.
            // Hit-testing then returns that popup even though the same Omnibox still has focus.
            if (focused != null && (app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) || app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)) &&
                focused.Current.ControlType == ControlType.Edit && focused.Current.ClassName == "OmniboxViewViews" &&
                focused.Current.IsEnabled && focused.Current.BoundingRectangle.Contains(new Point(x, y)) &&
                pointed.Current.ControlType == ControlType.Pane &&
                (pointed.Current.ClassName == "TopBackgroundView" || (app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) && pointed.Current.ClassName == "View")))
            {
                var parent = walker.GetParent(focused);
                bool popup = false;
                var node = pointed;
                for (int depth = 0; node != null && depth < 5; node = walker.GetParent(node), depth++)
                    if (node.Current.ClassName == "RoundedOmniboxResultsFrame") { popup = true; break; }
                var overlayBounds = pointed.Current.BoundingRectangle;
                var locationBounds = parent?.Current.BoundingRectangle ?? Rect.Empty;
                bool aligned = !locationBounds.IsEmpty && overlayBounds.Contains(focused.Current.BoundingRectangle) &&
                    Math.Abs(overlayBounds.Left - locationBounds.Left) <= 1 && Math.Abs(overlayBounds.Width - locationBounds.Width) <= 1 &&
                    overlayBounds.Height <= locationBounds.Height * 1.5;
                if (popup && parent?.Current.ClassName == "LocationBarView" && aligned &&
                    focused.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern writable && !writable.Current.IsReadOnly)
                    return new(Editability.Editable, app, window.ToInt64(), focused.Current.ControlType.ProgrammaticName, "editable_omnibox_overlay", string.Join(".", focused.GetRuntimeId()));
            }
            for (int i = 0; pointed != null && i < 6; i++, pointed = walker.GetParent(pointed))
            {
                var current = pointed.Current; string type = current.ControlType.ProgrammaticName;
                if (current.IsPassword) return new(Editability.NotEditable, app, window.ToInt64(), type, "password_field");
                if (IsNonInput(current.ControlType)) return new(Editability.NotEditable, app, window.ToInt64(), type, "non_input_control");
                if (!current.IsEnabled) return Reject("disabled", type);
                if (!focusOnly && !current.BoundingRectangle.Contains(new Point(x, y))) continue;
                // The pointed editor must contain the actual focused element, not merely coexist with one.
                var f = focused; bool matched = !requireFocus;
                for (int depth = 0; requireFocus && f != null && depth < 6; depth++, f = walker.GetParent(f))
                    if (Automation.Compare(pointed, f)) { matched = true; break; }
                if (!matched) continue;
                if (pointed.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) && raw is ValuePattern value)
                {
                    if (value.Current.IsReadOnly)
                    {
                        // A Chromium webview's root Document can expose a read-only
                        // ValuePattern without exposing its descendant editors.
                        if (current.ControlType == ControlType.Document && (IsWebDocumentContainer(pointed) || !pointed.TryGetCurrentPattern(TextPattern.Pattern, out _)))
                            return new(Editability.Unknown, app, window.ToInt64(), type, "document_container", string.Join(".", pointed.GetRuntimeId()));
                        return new(Editability.NotEditable, app, window.ToInt64(), type, "read_only");
                    }
                    if (current.ControlType == ControlType.Edit || current.ControlType == ControlType.Document)
                        return new(Editability.Editable, app, window.ToInt64(), type, "editable_value", string.Join(".", pointed.GetRuntimeId()));
                }
                if ((current.ControlType == ControlType.Edit || current.ControlType == ControlType.Document) && pointed.TryGetCurrentPattern(TextPattern.Pattern, out var text) && text is TextPattern pattern)
                {
                    var readOnly = pattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                    if (readOnly is false) return new(Editability.Editable, app, window.ToInt64(), type, "editable_text", string.Join(".", pointed.GetRuntimeId()));
                    if (readOnly is true)
                        return IsWebDocumentContainer(pointed) ? new(Editability.Unknown, app, window.ToInt64(), type, "document_container", string.Join(".", pointed.GetRuntimeId())) : new(Editability.NotEditable, app, window.ToInt64(), type, "read_only");
                }
            }
            return Reject("not_confirmed_editable");
        }
        catch (ElementNotAvailableException) { return Reject("element_unavailable"); }
        catch (UnauthorizedAccessException) { return Reject("access_denied"); }
        catch (System.Runtime.InteropServices.COMException) { return Reject("provider_failed"); }
    }
    private static bool IsNonInput(ControlType? type) => type == ControlType.Button || type == ControlType.CheckBox || type == ControlType.RadioButton ||
        type == ControlType.Hyperlink || type == ControlType.MenuItem || type == ControlType.Slider || type == ControlType.ScrollBar;
}
