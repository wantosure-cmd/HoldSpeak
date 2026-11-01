using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.Styling;

namespace HoldSpeak.App;

public sealed class DesktopApp : Application
{
    private TrayIcon? tray;
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; Styles.Add(new FluentTheme());
        Resources["SystemAccentColor"] = Color.Parse("#1677FF");
        Resources["SystemAccentColorLight1"] = Color.Parse("#4096FF");
        Resources["SystemAccentColorDark1"] = Color.Parse("#0958D9");
        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Button>()) { Setters = {
            new Avalonia.Styling.Setter(Button.CornerRadiusProperty, new CornerRadius(6)),
            new Avalonia.Styling.Setter(Button.MinHeightProperty, 36d),
            new Avalonia.Styling.Setter(Button.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Center),
            new Avalonia.Styling.Setter(Button.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
            new Avalonia.Styling.Setter(Button.BackgroundProperty, Brushes.White),
            new Avalonia.Styling.Setter(Button.BorderBrushProperty, Brush.Parse("#D0D5DD")),
            new Avalonia.Styling.Setter(Button.BorderThicknessProperty, new Thickness(1)) } });
        Styles.Add(new Style(x => x.OfType<Button>().Class("accent")) { Setters = {
            new Setter(Button.BackgroundProperty, Brush.Parse("#1677FF")),
            new Setter(Button.ForegroundProperty, Brushes.White),
            new Setter(Button.BorderBrushProperty, Brush.Parse("#1677FF")) } });
        Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = {
            new Setter(TextBox.CornerRadiusProperty, new CornerRadius(6)),
            new Setter(TextBox.MinHeightProperty, 36d),
            new Setter(TextBox.BorderBrushProperty, Brush.Parse("#D0D5DD")) } });
        Styles.Add(new Style(x => x.OfType<ComboBox>()) { Setters = {
            new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(6)),
            new Setter(ComboBox.MinHeightProperty, 36d),
            new Setter(ComboBox.PaddingProperty, new Thickness(12, 6)),
            new Setter(ComboBox.BackgroundProperty, Brushes.White),
            new Setter(ComboBox.BorderBrushProperty, Brush.Parse("#D0D5DD")) } });
        Styles.Add(new Style(x => x.OfType<NumericUpDown>()) { Setters = {
            new Setter(NumericUpDown.CornerRadiusProperty, new CornerRadius(6)),
            new Setter(NumericUpDown.MinHeightProperty, 36d),
            new Setter(NumericUpDown.BorderBrushProperty, Brush.Parse("#D0D5DD")) } });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            // Leaving MainWindow unset prevents the lifetime from showing settings on login.
            desktop.MainWindow = Program.AutoStarted ? null : window;
            var menu = new NativeMenu();
            var show = new NativeMenuItem(L.T("打开左键说")); show.Click += (_, _) => { window.Show(); window.Activate(); }; menu.Add(show);
            var pause = new NativeMenuItem(L.T("启用／暂停")); pause.Click += async (_, _) => await window.ToggleAsync(); menu.Add(pause);
            var quit = new NativeMenuItem(L.T("退出")); quit.Click += async (_, _) => { await window.ShutdownAsync(); desktop.Shutdown(); }; menu.Add(quit);
            tray = new TrayIcon { ToolTipText = L.T("左键说 · 按住左键说话"), Menu = menu, Icon = Brand.Icon() };
            tray.Clicked += (_, _) => Dispatcher.UIThread.Post(() => { window.Show(); window.Activate(); });
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            window.Closing += (_, e) => { if (!window.IsShuttingDown) { e.Cancel = true; window.Hide(); } };
            desktop.Exit += (_, _) => { window.ShutdownAsync().GetAwaiter().GetResult(); tray.Dispose(); };
            if (!Program.SmokeTest) Dispatcher.UIThread.Post(async () =>
            {
                if (Program.AutoStarted) await window.RefreshInputMethodsAsync();
                await window.EnableOnStartupAsync();
            });
            if (Program.SmokeTest) Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    if (Program.UiSmokeTest)
                    {
                        await Task.Delay(500);
                        if (!Program.AutoStarted || window.IsVisible) throw new InvalidOperationException("Login startup was not hidden");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Directory, "smoke-result.txt"), "PASS autostart hidden settings, tray initialized, explicit configuration directory; native engine excluded");
                        return;
                    }
                    using var platform = Program.CreatePlatform();
                    await using var engine = new HoldSpeak.Core.EngineHost(platform, new() { Enabled = false });
                    await Task.Run(engine.Start);
                    await Task.Delay(500);
                    if (Program.AutoStarted && window.IsVisible) throw new InvalidOperationException("Autostart showed the settings window");
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Directory, "smoke-result.txt"), "PASS window, tray, native hook, recovery process; engine stayed paused; autostartHidden=" + (Program.AutoStarted && !window.IsVisible));
                }
                catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Directory, "smoke-result.txt"), "FAIL " + ex); }
                finally { await window.ShutdownAsync(); desktop.Shutdown(); }
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
}
