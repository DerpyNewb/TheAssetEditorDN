using System.Windows;
using System.Windows.Controls;

namespace Editors.ImportExport.Blender
{
    /// <summary>A small modal "working" window that closes itself when the task ends; the UI keeps drawing meanwhile.</summary>
    public static class BusyWindow
    {
        public static void ShowUntil(Task work, string message)
        {
            if (work.IsCompleted)
                return;
            var window = new Window
            {
                Title = "AssetEditor",
                Content = new TextBlock { Text = message, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap },
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = 420,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow,
                ShowInTaskbar = false,
            };
            // the app theme's colours (a plain Window is white, under the theme's light text)
            window.SetResourceReference(Control.BackgroundProperty, "Window.Static.Background");
            window.SetResourceReference(Control.ForegroundProperty, "Window.Static.Foreground");
            var finished = false;
            window.Closing += (_, e) => e.Cancel = !finished; // the user cannot close it while Blender runs
            work.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => { finished = true; window.Close(); }));
            window.ShowDialog();
        }
    }
}
