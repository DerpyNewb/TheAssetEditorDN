using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Editors.AnimationMeta.MetaEditor.Check;
using Editors.AnimationMeta.MetaEditor.Examples;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Commands;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Ui.Common;
using Test.TestingUtility.Shared;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Test.E2EVerification
{
    // Scratch, not committed: SuperView on a vanilla unit with a tag picked, its 3D frame and its panels drawn offscreen to PNGs.
    //   CA_CACHE_DB  VMD_PATH  SV_BIN  SV_SLOT  SV_TAG (tag name to pick)  SV_TIME (seconds)  SCRATCH_OUT  EYE / AT
    internal class GuideRenderScratch
    {
        class ScratchApp : Application, IAssetEditorMain
        {
            public IServiceProvider ServiceProvider { get; init; } = null!;
        }

        static XnaVector3 V(string s) { var p = s.Split(',').Select(float.Parse).ToArray(); return new XnaVector3(p[0], p[1], p[2]); }

        static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        static void Save(Window window, string path)
        {
            window.UpdateLayout();
            Pump(300);
            FrameworkElement what = window;
            var bmp = new RenderTargetBitmap((int)Math.Max(what.ActualWidth, 1), (int)Math.Max(what.ActualHeight, 1), 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)), null, new Rect(0, 0, what.ActualWidth, what.ActualHeight));
            bmp.Render(bg);
            bmp.Render(what);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(path);
            png.Save(file);
        }

        // The app's custom window style lays out nothing offscreen, so a styled window's content is drawn in a plain one.
        static Window Rehost(Window styled)
        {
            var content = styled.Content;
            styled.Content = null;
            var plain = Offscreen(content, styled.Width, styled.Height);
            plain.DataContext = styled.DataContext;
            return plain;
        }

        static Window Offscreen(object content, double w, double h) =>
            new() { Content = content, Width = w, Height = h, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };

        [Test, Apartment(ApartmentState.STA), Explicit]
        public void Render()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT")!;
            Directory.CreateDirectory(outDir);
            var runner = new AssetEditorTestRunner();
            var loc = (LocalizationManager)runner.ServiceProvider.GetService(typeof(LocalizationManager))!;
            loc.LoadLanguage("en");
            var app = new ScratchApp { ServiceProvider = runner.ServiceProvider, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var dict in new[] { "Themes/ColourDictionaries/DarkTheme.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));
            // App.xaml's own keys, which the three dictionaries above do not carry.
            const string appKeys = """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:valueconverters="clr-namespace:Shared.Ui.Common.ValueConverters;assembly=Shared.Ui" xmlns:datatemplates="clr-namespace:Shared.Ui.Common.DataTemplates;assembly=Shared.Ui">
                    <valueconverters:BoolToVisibilityConverter x:Key="BoolToCollapsedConverter" TrueValue="Visible" FalseValue="Collapsed" />
                    <valueconverters:BoolToVisibilityConverter x:Key="BoolToHiddenConverter" TrueValue="Visible" FalseValue="Hidden" />
                    <valueconverters:BoolToVisibilityConverter x:Key="InvBoolToHiddenConverter" TrueValue="Hidden" FalseValue="Visible" />
                    <valueconverters:NullVisibilityConverter x:Key="NullToVisibilityConverter"/>
                    <valueconverters:BoolToColourConverter x:Key="ColourConverter_black_red" TrueValue="Black" FalseValue="Red"/>
                    <valueconverters:BoolToColourConverter x:Key="BoolToColour_white_red" TrueValue="White" FalseValue="Red"/>
                    <valueconverters:BoolToColourConverter x:Key="BoolToColour_black_gray" TrueValue="Black" FalseValue="Gray"/>
                    <valueconverters:BoolToColourConverter x:Key="BoolToColour_white_gray" TrueValue="White" FalseValue="Gray"/>
                    <valueconverters:InverseBooleanConverter x:Key="InvBoolConverter"/>
                    <valueconverters:BoolToStringConverter x:Key="BoolToChangedPrefixStr" TrueValue="*"/>
                    <datatemplates:ViewTemplateDataSelector x:Key="ViewTemplateDataSelector" />
                </ResourceDictionary>
                """;
            app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(appKeys));

            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);
            runner.CreateOutputPack();

            T Current<T>() where T : notnull => runner.ScopeRepository.GetRequiredService<T>(runner.ScopeRepository.GetEditorHandles().Last());
            var file = runner.PackFileService.FindFile(Environment.GetEnvironmentVariable("VMD_PATH")!)!;
            var superView = (Editors.AnimationMeta.SuperView.SuperViewViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(file, EditorEnums.SuperView_Editor)!;
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath.Contains(Environment.GetEnvironmentVariable("SV_BIN")!));
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName.Contains(Environment.GetEnvironmentVariable("SV_SLOT")!));
            var editor = superView.MetaEditor;
            editor.SelectedTag = editor.Tags.First(x => x._input.Name == Environment.GetEnvironmentVariable("SV_TAG"));
            TestContext.Out.WriteLine("picked " + editor.SelectedTag.DisplayName);

            // The 3D frame, the player held at SV_TIME.
            var seconds = float.Parse(Environment.GetEnvironmentVariable("SV_TIME") ?? "1");
            var player = superView.SceneObjects[0].Data.Player;
            player.Pause();
            var camera = Current<ArcBallCamera>();
            var components = Current<IEnumerable<Microsoft.Xna.Framework.IGameComponent>>().ToList();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            using (var target = new RenderTarget2D(device, 1024, 768, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents))
            {
                var time = new Microsoft.Xna.Framework.GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
                var (eye, at) = (V(Environment.GetEnvironmentVariable("EYE") ?? "1.5,1.2,3"), V(Environment.GetEnvironmentVariable("AT") ?? "0,0.8,0"));
                for (var f = 0; f < 20; f++)
                {
                    var toEye = eye - at;
                    camera.ViewMatrixOverride = null;
                    (camera.LookAt, camera.Zoom) = (at, toEye.Length());
                    toEye.Normalize();
                    (camera.Yaw, camera.Pitch) = (MathF.Atan2(toEye.X, toEye.Z), -MathF.Asin(toEye.Y));
                    device.SetRenderTarget(target);
                    player.SetTime((long)(seconds * 1_000_000));
                    foreach (var component in components.OfType<Microsoft.Xna.Framework.IUpdateable>().OrderBy(x => x.UpdateOrder))
                        component.Update(time);
                    player.SetTime((long)(seconds * 1_000_000));
                    player.Refresh();
                    foreach (var item in superView.SceneObjects)
                        item.Data.Update(time);
                    foreach (var component in components.OfType<Microsoft.Xna.Framework.IDrawable>().OrderBy(x => x.DrawOrder))
                        component.Draw(time);
                    device.SetRenderTarget(null);
                }
                using var stream = File.Create(Path.Combine(outDir, "viewport.png"));
                target.SaveAsPng(stream, target.Width, target.Height);
            }

            // SuperView's panel: the timeline, the tag list with its marks and buttons, the picked tag's fields.
            superView.SelectedTabControllerIndex = 1;
            superView.ScrubTo(seconds * 1_000_000 / player.GetAnimationLengthUs());
            var panel =Offscreen(new Editors.AnimationMeta.SuperView.EditorView { DataContext = superView }, 560, 1250);
            panel.Show();
            Save(panel, Path.Combine(outDir, "panel.png"));
            panel.Close();

            // Check meta's window.
            var check = runner.ServiceProvider.GetRequiredService<Shared.Core.Events.IUiCommandFactory>().Create<CheckMetaCommand>(x => x.Configure(editor));
            var checkWindow = new MetaCheckWindow { DataContext = new MetaCheckViewModel(editor, () => check.Check(editor)), Width = 760, Height = 420, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
            var checkHost = Rehost(checkWindow);
            checkHost.Show();
            Save(checkHost, Path.Combine(outDir, "check.png"));
            checkHost.Close();

            // The vanilla examples window, after the scan.
            var examplesCommand = runner.ServiceProvider.GetRequiredService<Shared.Core.Events.IUiCommandFactory>().Create<OpenVanillaExamplesCommand>(x => x.Configure(editor));
            var examples = examplesCommand.Follow(editor, out var stop);
            var scan = Current<VanillaMetaExamples>().Scan();
            for (var i = 0; i < 600 && scan is { IsCompleted: false }; i++)
                Pump(100);
            Pump(500);
            var examplesWindow = new VanillaExamplesWindow { DataContext = examples, Width = 640, Height = 520, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
            var examplesHost = Rehost(examplesWindow);
            examplesHost.Show();
            Save(examplesHost, Path.Combine(outDir, "examples.png"));
            examplesHost.Close();
            stop();
        }
    }
}
