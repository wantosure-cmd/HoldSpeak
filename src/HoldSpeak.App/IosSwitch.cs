using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace HoldSpeak.App;

// Retains ToggleButton keyboard, focus and accessibility behavior.
public sealed class IosSwitch : ToggleButton
{
    private readonly Border thumb = new()
    {
        Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
        Background = Brushes.White, Margin = new Thickness(2),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Border track = new() { CornerRadius = new CornerRadius(16) };
    public IosSwitch()
    {
        Width = MinWidth = 52; Height = MinHeight = 32;
        Padding = new Thickness(0); BorderThickness = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, "长按说话开关");
        ToolTip.SetTip(this, "开启或暂停长按说话");
        track.Child = thumb;
        Template = new FuncControlTemplate<IosSwitch>((_, _) => track);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsCheckedProperty || e.Property == IsEnabledProperty) UpdateVisual();
        };
        UpdateVisual();
    }
    private void UpdateVisual()
    {
        track.Background = Brush.Parse(IsChecked == true ? "#34C759" : "#E5E5EA");
        thumb.HorizontalAlignment = IsChecked == true ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        track.Opacity = IsEnabled ? 1 : 0.5;
    }
}
