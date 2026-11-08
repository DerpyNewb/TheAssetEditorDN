using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AnimationEditor.Common.BaseControl;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Editors.UnitSimulator;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.UnitSimulator
{
    // The 3D editors' panels dock around the 3D view: scene content and the editor's own panel on the right, the animation
    // player under the view. The default is the layout the editors had before docking.
    public class HostLayoutTests
    {
        internal static UnitSimulatorViewModel Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            return UnitSimulatorAppTests.Open(runner);
        }

        internal static IEnumerable<DependencyObject> Visuals(DependencyObject root) =>
            Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root)).Select(i => VisualTreeHelper.GetChild(root, i)).SelectMany(x => Visuals(x).Prepend(x));

        static T Above<T>(DependencyObject child) where T : DependencyObject
        {
            for (var x = VisualTreeHelper.GetParent(child); x != null; x = VisualTreeHelper.GetParent(x))
                if (x is T found)
                    return found;
            throw new InvalidOperationException($"no {typeof(T).Name} above {child}");
        }

        // A window off screen, because the dock builds its panes when it is loaded.
        internal sealed class Shown(EditorHostView view, Window window) : IDisposable
        {
            public EditorHostView View { get; } = view;
            public DockingManager Dock => (DockingManager)View.FindName("Dock");
            public LayoutAnchorable Pane(string id) => Dock.Layout.Descendents().OfType<LayoutAnchorable>().Single(x => x.ContentId == id);
            public LayoutDocument Document => Dock.Layout.Descendents().OfType<LayoutDocument>().Single();
            // The dock fixes its pane sizes in work it queues, so the queue is run before and after laying out.
            public void Lay()
            {
                Idle();
                window.UpdateLayout();
                Idle();
                window.UpdateLayout();
            }

            static void Idle() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            public void Dispose() => window.Close();
        }

        internal static Shown Show(object editor, EditorHostView? view = null)
        {
            AppResources.Load();
            view ??= new EditorHostView { Layouts = new(TempFolder()) }; // never the real layouts folder
            view.DataContext = editor;
            var window = new Window { Content = view, Width = 1600, Height = 900, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = 0 };
            window.Show();
            var shown = new Shown(view, window);
            Assert.That(shown.Dock, Is.Not.Null, "no DockingManager named Dock in the host view");
            shown.Lay();
            return shown;
        }

        static Rect Bounds(FrameworkElement element, Visual relativeTo) =>
            element.TransformToAncestor(relativeTo).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        internal static Rect PaneBounds(Shown shown, LayoutContent content) =>
            Bounds(content is LayoutDocument ? Above<LayoutDocumentPaneControl>((DependencyObject)content.Content) : Above<LayoutAnchorablePaneControl>((DependencyObject)content.Content), shown.Dock);

        internal static void AssertDefaultLayout(Shown shown)
        {
            var w = shown.Dock.GridSplitterWidth;
            var view = PaneBounds(shown, shown.Document);
            var scene = PaneBounds(shown, shown.Pane("SceneContent"));
            var panel = PaneBounds(shown, shown.Pane("EditorPanel"));
            var player = PaneBounds(shown, shown.Pane("AnimationPlayer"));
            var width = shown.Dock.ActualWidth;

            Assert.Multiple(() =>
            {
                // A quarter of a 1600-wide editor, as the column was. Fixed, not a share: the dock turns a side column's star width
                // into pixels, and a star it has not measured yet into its 25 px minimum.
                Assert.That(scene.Width, Is.EqualTo(400).Within(2), "the right column's width");
                Assert.That(view.Width + w + scene.Width, Is.EqualTo(width).Within(2), "the 3D view takes the rest");
                Assert.That(panel.Width, Is.EqualTo(scene.Width).Within(1));
                Assert.That(scene.Left, Is.GreaterThanOrEqualTo(view.Right), "scene content right of the 3D view");
                Assert.That(panel.Top, Is.GreaterThanOrEqualTo(scene.Bottom), "the editor's panel under the scene content");
                Assert.That(scene.Height, Is.EqualTo((scene.Height + panel.Height) * 2 / 5).Within(2), "scene content 2 to the panel's 3");
                Assert.That(player.Top, Is.GreaterThanOrEqualTo(view.Bottom), "the player under the 3D view");
                Assert.That(player.Left, Is.EqualTo(view.Left).Within(2));
                Assert.That(player.Width, Is.EqualTo(view.Width).Within(2));
            });
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheDefaultLayout_IsTodaysLayout()
        {
            using var shown = Show(Open());
            AssertDefaultLayout(shown);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheGameView_CannotCloseOrFloat()
        {
            using var shown = Show(Open());
            var document = shown.Document;
            Assert.That((document.ContentId, document.CanClose, document.CanFloat), Is.EqualTo(("GameView", false, false)));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheGameView_StillTakesDrops()
        {
            using var shown = Show(Open());
            var game = shown.View.FindName("GameView") as ContentControl;
            Assert.That(game, Is.Not.Null);
            Assert.That(game!.AllowDrop, Is.True);
            Assert.That(shown.Document.Content, Is.SameAs(game));
        }

        // D3D11Host hooks the nearest TabControl above the 3D view to know when its app tab is hidden. The dock's panes are
        // TabControls too; the lookup must still land on the app's, before and after a pane has moved.
        [Test, Apartment(ApartmentState.STA)]
        public void TheGameViewsTabControl_IsTheAppsNotTheDocks()
        {
            AppResources.Load();
            var view = new EditorHostView { Layouts = new(TempFolder()) };
            var appTabs = new TabControl();
            appTabs.Items.Add(new TabItem { Header = "editor", Content = view });
            appTabs.SelectedIndex = 0;
            var window = new Window { Content = appTabs, Width = 1600, Height = 900, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = 0 };
            view.DataContext = Open();
            window.Show();
            using var shown = new Shown(view, window);
            shown.Lay();
            var game = (DependencyObject)shown.View.FindName("GameView");
            Assert.That(GameWorld.Core.WpfWindow.Internals.LogicalTreeHelperEx.FindParent<TabControl>(game), Is.SameAs(appTabs));

            var panel = shown.Pane("EditorPanel");
            var scenePane = (LayoutAnchorablePane)shown.Pane("SceneContent").Parent;
            ((ILayoutContainer)panel.Parent).RemoveChild(panel);
            scenePane.Children.Add(panel);
            shown.Lay();
            Assert.That(GameWorld.Core.WpfWindow.Internals.LogicalTreeHelperEx.FindParent<TabControl>(game), Is.SameAs(appTabs), "after a pane moved");
        }

        internal static string TempFolder() => Directory.CreateTempSubdirectory("docklayout").FullName;

        static void MoveEditorPanelIntoSceneContent(Shown shown)
        {
            var panel = shown.Pane("EditorPanel");
            ((ILayoutContainer)panel.Parent).RemoveChild(panel);
            ((LayoutAnchorablePane)shown.Pane("SceneContent").Parent).Children.Add(panel);
            shown.Lay();
        }

        [Test, Apartment(ApartmentState.STA)]
        public void ClosingTheEditor_SavesItsLayout_AndOpeningAnotherLoadsIt()
        {
            var folder = TempFolder();
            var sim = Open();
            using (var shown = Show(sim, new EditorHostView { Layouts = new(folder) }))
            {
                MoveEditorPanelIntoSceneContent(shown);
                sim.Close();
            }
            Assert.That(File.Exists(Path.Combine(folder, "UnitSimulator_Editor.xml")), Is.True, "closing the editor saved nothing");

            using var again = Show(Open(), new EditorHostView { Layouts = new(folder) });
            Assert.That(again.Pane("EditorPanel").Parent, Is.SameAs(again.Pane("SceneContent").Parent));
            Assert.That(again.Pane("EditorPanel").Content, Is.InstanceOf<ScrollViewer>(), "the live panel, not an empty pane");
        }

        [Test, Apartment(ApartmentState.STA)]
        public void ResetLayout_DeletesTheFile_AndRestoresTheDefault()
        {
            var folder = TempFolder();
            var sim = Open();
            using var shown = Show(sim, new EditorHostView { Layouts = new(folder) });
            MoveEditorPanelIntoSceneContent(shown);
            shown.View.Layouts.Save(shown.Dock, sim.LayoutKey);

            shown.View.ResetLayout();
            shown.Lay();
            Assert.That(File.Exists(Path.Combine(folder, "UnitSimulator_Editor.xml")), Is.False);
            AssertDefaultLayout(shown);
        }

        // The Mount Tool shares the view with a host that is not an EditorHostBase: the default layout, nothing kept.
        [Test, Apartment(ApartmentState.STA)]
        public void TheMountTool_OpensWithTheDefaultLayout_AndSavesNothing()
        {
            var folder = TempFolder();
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var mount = (Shared.Core.ToolCreation.IEditorInterface)runner.CommandFactory.Create<Shared.Core.Commands.OpenEditorCommand>().Execute(Shared.Core.ToolCreation.EditorEnums.MountTool_Editor);
            using (var shown = Show(mount, new EditorHostView { Layouts = new(folder) }))
            {
                Assert.That(shown.Dock.Layout.Descendents().OfType<LayoutAnchorable>().Count(), Is.EqualTo(3));
                mount.Close();
            }
            Assert.That(Directory.EnumerateFiles(folder), Is.Empty);
        }

        static MenuItem MenuNamed(Shown shown, string name) => (MenuItem)shown.View.FindName(name) ?? throw new AssertionException($"no menu item {name}");

        // General > Panels lists the panes as they are when it opens, a pane closed with its X unticked; ticking it brings it back.
        [Test, Apartment(ApartmentState.STA)]
        public void ThePanelsMenu_ReopensAClosedPane()
        {
            using var shown = Show(Open());
            shown.Pane("SceneContent").Hide();
            shown.Lay();
            var panels = MenuNamed(shown, "PanelsMenu");
            panels.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            var item = panels.Items.OfType<MenuItem>().Single(x => (string)x.Header == shown.Pane("SceneContent").Title);
            Assert.That(item.IsChecked, Is.False, "a closed pane shows ticked");
            Assert.That(panels.Items.OfType<MenuItem>().Count(x => x.IsChecked), Is.EqualTo(2), "the other two panes show ticked");

            item.IsChecked = true;
            Assert.That(shown.Pane("SceneContent").IsVisible, Is.True);
            item.IsChecked = false;
            Assert.That(shown.Pane("SceneContent").IsHidden, Is.True);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void ResetLayout_MenuItem_RunsReset()
        {
            using var shown = Show(Open());
            Assert.That(MenuNamed(shown, "GeneralMenu").IsEnabled, Is.True, "General is still disabled");
            MoveEditorPanelIntoSceneContent(shown);
            MenuNamed(shown, "ResetLayoutMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            shown.Lay();
            AssertDefaultLayout(shown);
        }

        // A part is taller than a small share; scrolled by whole parts, the list could only show its top. By pixel, its end is reached.
        [Test, Apartment(ApartmentState.STA)]
        public void APartTallerThanTheSceneContentsShare_ScrollsToItsEnd()
        {
            using var shown = Show(Open());
            ((LayoutAnchorablePane)shown.Pane("SceneContent").Parent).DockHeight = new GridLength(150);
            shown.Lay();
            var list = Visuals((DependencyObject)shown.Pane("SceneContent").Content).OfType<ScrollViewer>().First();
            var last = Visuals(list).OfType<ListViewItem>().Last();
            Assert.That(last.ActualHeight, Is.GreaterThan(list.ViewportHeight), "no part taller than the share, so this proves nothing");

            list.ScrollToEnd();
            shown.Lay();
            var bottom = last.TransformToAncestor(list).Transform(new Point(0, last.ActualHeight)).Y;
            Assert.That(bottom, Is.LessThanOrEqualTo(list.ViewportHeight + 1));
        }
    }
}
