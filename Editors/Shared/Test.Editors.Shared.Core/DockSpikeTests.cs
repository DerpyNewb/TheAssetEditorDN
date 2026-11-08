using System.Windows;
using System.Windows.Controls;
using AvalonDock;
using AvalonDock.Layout;
using GameWorld.Core.WpfWindow.Internals;

namespace Test.Editors.Shared.Core
{
    // The docking design's first question: does AvalonDock build and lay out on net10.0-windows at all. And the second one it
    // raises for the 3D view: is the nearest TabControl above a document's content AvalonDock's own pane.
    [Apartment(ApartmentState.STA)]
    public class DockSpikeTests
    {
        [Test]
        public void DockingManager_LaysOutOnThisRuntime()
        {
            var content = new ContentControl { Content = new Border() };
            var document = new LayoutDocument { Title = "3D view", Content = content };
            var anchorable = new LayoutAnchorable { Title = "Panel", Content = new TextBlock { Text = "panel" } };
            var dock = new DockingManager
            {
                Layout = new LayoutRoot
                {
                    RootPanel = new LayoutPanel(new LayoutDocumentPane(document)) { Orientation = Orientation.Horizontal },
                },
            };
            dock.Layout.RootPanel.Children.Add(new LayoutAnchorablePane(anchorable));

            var window = new Window { Content = dock, Width = 1600, Height = 900, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.That(content.ActualWidth, Is.GreaterThan(0));

                var tab = LogicalTreeHelperEx.FindParent<TabControl>((Border)content.Content);
                TestContext.Out.WriteLine($"nearest TabControl above the document's content: {tab?.GetType().FullName ?? "none"}");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
