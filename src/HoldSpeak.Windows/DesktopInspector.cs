using System.Diagnostics;
using System.Windows.Automation;

namespace HoldSpeak.Windows;

public sealed record EditorEvidence(string Type, bool Focused, bool Password, bool? ValueReadOnly, bool? TextReadOnly, string ControlId, string ClassName, bool Focusable, double X, double Y, double Width, double Height, string FrameworkId, int ProcessId, string ParentType, string ParentClass, bool WebDocumentContainer);
public sealed record WindowEvidence(string AppId, int ProcessId, long WindowId, int MatchingControls, bool Truncated, EditorEvidence[] Editors);

public static class DesktopInspector
{
    // No Name, Value, text ranges, titles, or URLs are collected.
    public static WindowEvidence[] InspectKnownApplications(string? onlyApp = null)
    {
        string[] names = onlyApp == null ? ["antigravity", "codex", "chatgpt", "chrome", "msedge", "wechat", "weixin", "wechatappex", "feishu", "lark", "wetype_renderer", "wetype_server"] : [onlyApp.ToLowerInvariant()];
        var pids = new Dictionary<int, string>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try { if (names.Contains(p.ProcessName.ToLowerInvariant())) pids[p.Id] = p.ProcessName + ".exe"; }
                catch (InvalidOperationException) { }
            }
        }
        List<WindowEvidence> result = [];
        var budget = Stopwatch.StartNew();
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement window in windows)
        {
            try
            {
                if (!pids.TryGetValue(window.Current.ProcessId, out string? app)) continue;
                // Filtering in UIA avoids dropping a footer editor behind a large message list.
                var candidates = window.FindAll(TreeScope.Descendants, new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)));
                int inspected = 0; List<EditorEvidence> editors = [];
                foreach (AutomationElement node in candidates)
                {
                    if (inspected >= 128 || budget.Elapsed >= TimeSpan.FromSeconds(15)) break;
                    inspected++; var current = node.Current;
                    {
                        bool? valueReadonly = null, textReadonly = null;
                        if (!current.IsPassword)
                        {
                            if (node.TryGetCurrentPattern(ValuePattern.Pattern, out var v) && v is ValuePattern value) valueReadonly = value.Current.IsReadOnly;
                            if (node.TryGetCurrentPattern(TextPattern.Pattern, out var t) && t is TextPattern text && text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is bool readOnly) textReadonly = readOnly;
                        }
                        var bounds = current.BoundingRectangle;
                        var parent = TreeWalker.ControlViewWalker.GetParent(node);
                        editors.Add(new(current.ControlType.ProgrammaticName, current.HasKeyboardFocus, current.IsPassword, valueReadonly, textReadonly, string.Join(".", node.GetRuntimeId()), current.ClassName, current.IsKeyboardFocusable, bounds.X, bounds.Y, bounds.Width, bounds.Height, current.FrameworkId, current.ProcessId, parent?.Current.ControlType.ProgrammaticName ?? "", parent?.Current.ClassName ?? "", WindowsProbe.IsWebDocumentContainer(node)));
                    }
                }
                result.Add(new(app, window.Current.ProcessId, window.Current.NativeWindowHandle, candidates.Count, inspected < candidates.Count, editors.ToArray()));
            }
            catch (ElementNotAvailableException) { }
        }
        return result.ToArray();
    }
}
