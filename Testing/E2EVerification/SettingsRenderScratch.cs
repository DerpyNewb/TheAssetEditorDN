using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Views.Settings;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification
{
    // Scratch, not committed: draws the Settings window's three tabs to PNGs and prints what each tab needs.
    internal class SettingsRenderScratch
    {
        class ScratchApp : Application, IAssetEditorMain
        {
            public IServiceProvider ServiceProvider { get; init; } = null!;
        }

        [Test, Apartment(ApartmentState.STA), Explicit]
        public void Render()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var runner = new AssetEditorTestRunner();
            var loc = (LocalizationManager)runner.ServiceProvider.GetService(typeof(LocalizationManager))!;
            Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "Language_en.json")), "no Language_en.json beside the tests");
            loc.LoadLanguage(Environment.GetEnvironmentVariable("SCRATCH_LANG") ?? "en");

            var app = new ScratchApp { ServiceProvider = runner.ServiceProvider };
            foreach (var dict in new[] { $"Themes/ColourDictionaries/{Environment.GetEnvironmentVariable("SCRATCH_THEME") ?? "DarkTheme"}.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));

            var settings = runner.ServiceProvider.GetService(typeof(ApplicationSettingsService))!;
            var vmType = typeof(AssetEditor.ViewModels.MainViewModel).Assembly.GetType("AssetEditor.ViewModels.SettingsViewModel")!;
            var vm = Activator.CreateInstance(vmType, settings, loc, runner.ServiceProvider.GetService(typeof(Shared.Ui.Common.MenuSystem.HotkeyService)));
            // SCRATCH_CLASH: Kitbash's Redo moved onto Undo's Ctrl+Z and the gizmo bigger cleared, to show the warning and an empty box.
            if (Environment.GetEnvironmentVariable("SCRATCH_CLASH") is { Length: > 0 })
            {
                var rows = ((List<Shared.Ui.Common.MenuSystem.HotkeyGroup>)vmType.GetProperty("HotkeyGroups")!.GetValue(vm)!).SelectMany(x => x.Rows).ToList();
                rows.Single(x => x.Definition.Id == "Kitbash.RedoCommand").Capture(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control);
                rows.Single(x => x.Definition.Id == "Kitbash.ScaleGizmoUpCommand").ClearCommand.Execute(null);
            }

            var window = new SettingsWindow { DataContext = vm, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowActivated = false };
            window.Show();
            var tabs = Find<TabControl>(window)!;
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT") ?? Path.GetTempPath();
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var content = (FrameworkElement)((TabItem)tabs.Items[i]).Content;
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                TestContext.Out.WriteLine($"tab {i}: needs {content.DesiredSize.Width:0}x{content.DesiredSize.Height:0}, has {content.ActualWidth:0}x{content.ActualHeight:0}");

                var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(window);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using var file = File.Create(Path.Combine(outDir, $"settings_tab{i}.png"));
                png.Save(file);
            }
            window.Close();
        }

        [Test, Apartment(ApartmentState.STA), Explicit]
        public void RenderStartPanel()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var runner = new AssetEditorTestRunner();
            var loc = (LocalizationManager)runner.ServiceProvider.GetService(typeof(LocalizationManager))!;
            loc.LoadLanguage(Environment.GetEnvironmentVariable("SCRATCH_LANG") ?? "en");
            var app = new ScratchApp { ServiceProvider = runner.ServiceProvider };
            foreach (var dict in new[] { $"Themes/ColourDictionaries/{Environment.GetEnvironmentVariable("SCRATCH_THEME") ?? "DarkTheme"}.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));

            var settings = (ApplicationSettingsService)runner.ServiceProvider.GetService(typeof(ApplicationSettingsService))!;
            var recentCount = int.Parse(Environment.GetEnvironmentVariable("SCRATCH_RECENT") ?? "0");
            var samples = new (string, Shared.Core.PackFiles.Models.PackFileContainerType, bool)[]
            {
                (@"G:\Modding for resources\Modding Files\Modpacks\derpy_units_vraznak.pack", Shared.Core.PackFiles.Models.PackFileContainerType.Normal, false),
                (@"F:\SteamLibrary\steamapps\workshop\content\1142710\3312345678\derpy_units_vraznak.pack", Shared.Core.PackFiles.Models.PackFileContainerType.Normal, true),
                (@"G:\Modding for resources\Modding Files\projects\hashut_lords", Shared.Core.PackFiles.Models.PackFileContainerType.SystemFolder, false),
                (@"F:\SteamLibrary\steamapps\common\Total War WARHAMMER III\data\!aa_derpy_hashut_legendary_lords.pack", Shared.Core.PackFiles.Models.PackFileContainerType.Normal, false),
            };
            for (var i = 0; i < recentCount; i++)
                settings.CurrentSettings.RecentPackFiles.Add(new RecentPackFileInfo(samples[i % samples.Length].Item1 + (i < samples.Length ? "" : $"_{i}"), samples[i % samples.Length].Item2, samples[i % samples.Length].Item3));
            var window = new AssetEditor.Views.StartPanel.StartPanelWindow(new AssetEditor.ViewModels.StartPanelViewModel(settings, runner.CommandFactory))
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowActivated = false };
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            TestContext.Out.WriteLine($"panel: {window.ActualWidth:0}x{window.ActualHeight:0}");
            var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT") ?? Path.GetTempPath();
            using (var file = File.Create(Path.Combine(outDir, "start_panel.png")))
                png.Save(file);
            window.Close();
        }

        // The theme switched while the window is open, as Settings > Save now does.
        [Test, Apartment(ApartmentState.STA), Explicit]
        public void RenderThemeSwitch()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var runner = new AssetEditorTestRunner();
            var loc = (LocalizationManager)runner.ServiceProvider.GetService(typeof(LocalizationManager))!;
            loc.LoadLanguage("en");
            var app = new ScratchApp { ServiceProvider = runner.ServiceProvider, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var dict in new[] { "Themes/ColourDictionaries/DarkTheme.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));
            var settings = runner.ServiceProvider.GetService(typeof(ApplicationSettingsService))!;
            var vmType = typeof(AssetEditor.ViewModels.MainViewModel).Assembly.GetType("AssetEditor.ViewModels.SettingsViewModel")!;
            var window = new SettingsWindow { DataContext = Activator.CreateInstance(vmType, settings, loc), WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowActivated = false };
            window.Show();
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT") ?? Path.GetTempPath();
            foreach (var theme in new[] { ThemeType.LightTheme, ThemeType.AmoledTheme, ThemeType.DarkTheme })
            {
                ThemesController.SetTheme(theme);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(window);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using (var file = File.Create(Path.Combine(outDir, $"switch_{theme}.png")))
                    png.Save(file);
            }
            window.Close();
        }

        [Test, Apartment(ApartmentState.STA), Explicit]
        public void RenderTextEditor()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var runner = new AssetEditorTestRunner();
            var app = new ScratchApp { ServiceProvider = runner.ServiceProvider, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var dict in new[] { "Themes/ColourDictionaries/DarkTheme.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT") ?? Path.GetTempPath();

            var samples = new Dictionary<string, string>
            {
                ["XML"] = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- a comment -->\n<VARIANT_MESH>\n  <SLOT name=\"head\" attach_point=\"root\">\n    <VARIANT_MESH model=\"variantmeshes\\wh_variantmodels\\hu1\\emp\\head.rigid_model_v2\" />\n  </SLOT>\n  <DATA><![CDATA[raw]]></DATA> &amp; &broken;\n</VARIANT_MESH>",
                ["HTML"] = "<!DOCTYPE html>\n<!-- a comment -->\n<html>\n<head><style>body { color: red; }</style>\n<script>var x = 42; // note\n</script></head>\n<body class=\"main\" onclick=\"go()\">Text &amp; more</body>\n</html>",
            };
            foreach (var background in new[] { BackgroundColour.DarkGrey, BackgroundColour.Black, BackgroundColour.White, BackgroundColour.ChromaBlue })
            {
                global::Shared.Ui.Editors.TextEditor.TextEditorTheme.Apply(background);
                foreach (var (syntax, text) in samples)
                {
                    var view = new CommonControls.Editors.TextEditor.TextEditorView { DataContext = new global::Shared.Ui.Editors.TextEditor.SimpleTextEditorViewModel { Text = text } };
                    var window = new Window { Content = view, Width = 640, Height = 260, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowActivated = false };
                    window.Show();
                    view.SetSyntaxHighlighting(syntax);
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(window);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bmp));
                    using (var file = File.Create(Path.Combine(outDir, $"texteditor_{syntax}_{background}.png")))
                        png.Save(file);
                    window.Close();
                }
            }
        }

        static T? Find<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t)
                    return t;
                if (Find<T>(child) is { } found)
                    return found;
            }
            return null;
        }
    }
}
