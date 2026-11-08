using System.Windows;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Themes;
using AvalonDock.Themes.VS2013.Themes;
using Editors.Shared.Core.Common.BaseControl.Docking;

namespace Test.Editors.Shared.Core
{
    // The dock's chrome takes the app's colours: every brush VS2013 Dark defines is replaced by one whose colour is a
    // DynamicResource to an AColour key, so a theme switch reaches tabs, title bars and drop targets without a restart.
    [Apartment(ApartmentState.STA)]
    public class DockThemeTests
    {
        static ResourceDictionary Vs2013Dark() => new Vs2013DarkTheme().ThemeResourceDictionary;

        [Test]
        public void EveryVs2013DarkBrush_IsOverridden()
        {
            var vs = Vs2013Dark();
            var ours = AssetEditorDockTheme.Overrides();
            var brushes = vs.Keys.Cast<object>().Where(k => vs[k] is Brush).ToList();
            Assert.That(brushes, Has.Count.GreaterThan(80), "VS2013 Dark defined too few brushes for this to mean anything");
            Assert.That(brushes.Where(k => ours.Contains(k) == false).Select(k => k.ToString()), Is.Empty);
        }

        [Test]
        public void NoOverride_IsALiteralColour()
        {
            var ours = AssetEditorDockTheme.Overrides();
            var literal = ours.Keys.Cast<object>().Where(k => ours[k] is not SolidColorBrush brush
                || brush.ReadLocalValue(SolidColorBrush.ColorProperty).GetType().Name != "ResourceReferenceExpression");
            Assert.That(literal.Select(k => k.ToString()), Is.Empty);
        }

        [Test]
        public void TheThemeKeepsVs2013sStyles()
        {
            var theme = new AssetEditorDockTheme().ThemeResourceDictionary;
            Assert.That(theme.MergedDictionaries.Select(x => x.Source?.OriginalString), Has.Some.EndsWith("Themes/Generic.xaml"));
        }

        // Review focus 5: a theme switch replaces the AColour resources, and the dock's brushes follow.
        [Test]
        public void ABrushFollowsItsColour_WhenTheColourIsReplaced()
        {
            var dock = new DockingManager { Theme = new AssetEditorDockTheme(), Layout = new LayoutRoot() };
            var window = new Window { Content = dock, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 200, Height = 200 };
            window.Resources["AColour.Tone2.Background.Static"] = Colors.Red;
            window.Show();
            try
            {
                var brush = (SolidColorBrush)dock.FindResource(ResourceKeys.Background);
                Assert.That(brush.Color, Is.EqualTo(Colors.Red));
                window.Resources["AColour.Tone2.Background.Static"] = Colors.Blue;
                Assert.That(((SolidColorBrush)dock.FindResource(ResourceKeys.Background)).Color, Is.EqualTo(Colors.Blue));
            }
            finally
            {
                window.Close();
            }
        }
    }
}
