using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimationEditor.Common.BaseControl;
using AvalonDock.Layout;

namespace Test.UnitSimulator
{
    // Not a test: draws the docked Unit Simulator in the app's own theme for a person to look at.
    public class DockRenderScratch
    {
        [TestCase("DarkTheme"), TestCase("AmoledTheme"), TestCase("LightTheme"), Explicit, Apartment(ApartmentState.STA)]
        public void Render(string theme)
        {
            var view = new EditorHostView { Layouts = new(HostLayoutTests.TempFolder()) };
            foreach (var d in new[] { $"Themes/ColourDictionaries/{theme}.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AssetEditor;component/{d}", UriKind.Relative) });
            using var shown = HostLayoutTests.Show(HostLayoutTests.Open(), view);
            shown.Pane("AnimationPlayer").ToggleAutoHide();
            shown.Lay();
            var bmp = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
            bmp.Render((Visual)VisualTreeHelper.GetParent(shown.View) ?? shown.View);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create($@"G:\Modding for resources\docs\sessions\docking_20261108\dock_{theme}.png");
            enc.Save(f);
        }
    }
}
