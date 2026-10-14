using System.Windows;
using System.Windows.Media;
using Shared.Ui.Common.ValueConverters;

namespace Test.TestingUtility.TestUtility
{
    /// <summary>The app-level converters, as App.xaml declares them, for a view laid out in a test, which has no App.xaml to give
    /// them. Not the theme: its brushes belong to the thread that loads them, and each STA test runs on a thread of its own.</summary>
    public static class AppResources
    {
        public static void Load()
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            static BoolToVisibilityConverter Vis(Visibility t, Visibility f) => new() { TrueValue = t, FalseValue = f };
            static BoolToColourConverter Brush(Color t, Color f) => new() { TrueValue = new(t), FalseValue = new(f) };
            var (visible, hidden, collapsed) = (Visibility.Visible, Visibility.Hidden, Visibility.Collapsed);
            app.Resources["BoolToCollapsedConverter"] = Vis(visible, collapsed);
            app.Resources["BoolToHiddenConverter"] = Vis(visible, hidden);
            app.Resources["InvBoolToHiddenConverter"] = Vis(hidden, visible);
            app.Resources["NullToVisibilityConverter"] = new NullVisibilityConverter();
            app.Resources["ColourConverter_black_red"] = Brush(Colors.Black, Colors.Red);
            app.Resources["BoolToColour_white_red"] = Brush(Colors.White, Colors.Red);
            app.Resources["BoolToColour_black_gray"] = Brush(Colors.Black, Colors.Gray);
            app.Resources["BoolToColour_white_gray"] = Brush(Colors.White, Colors.Gray);
            app.Resources["InvBoolConverter"] = new InverseBooleanConverter();
            app.Resources["BoolToChangedPrefixStr"] = new BoolToStringConverter { TrueValue = "*" };
        }
    }
}
