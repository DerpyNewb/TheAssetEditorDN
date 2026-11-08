using System.Windows;
using AvalonDock.Layout;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common;

namespace AnimationEditor.Common.BaseControl
{
    /// <summary>
    /// Interaction logic for MainPropCreatorView.xaml
    /// </summary>
    public partial class EditorHostView : UserControl
    {
        /// <summary>Where this editor's layout is kept; a test points it at a folder of its own.</summary>
        public DockLayoutStore Layouts { get; init; } = new(DockLayoutStore.DefaultFolder);

        string? _defaultLayout;
        EditorHostBase? _savesTo;

        public EditorHostView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            IsVisibleChanged += OnVisibleChanged;
        }

        // The app collapses an editor whose tab is not selected, and a floating pane is a window of its own: it goes and comes
        // back with its editor instead of staying over another editor's tab.
        void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            foreach (var window in Dock.FloatingWindows)
            {
                if ((bool)e.NewValue)
                    window.Show();
                else
                    window.Hide();
            }
        }

        // The layout the XAML gives is the default, kept before a saved one replaces it. A host that is not an EditorHostBase
        // (the Mount Tool) keeps the default and saves nothing.
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_defaultLayout != null)
                return;
            if (DataContext is EditorHostBase { EditorPanes.Count: > 0 } withPanes)
                ShowEditorPanes(withPanes.EditorPanes);
            _defaultLayout = DockLayoutStore.Serialize(Dock);
            if (DataContext is not EditorHostBase host)
                return;

            Layouts.TryLoad(Dock, host.LayoutKey);
            _savesTo = host;
            host.Closing += SaveLayout;
            if (App is { } app)
                app.Exit += SaveLayoutOnExit;
        }

        // The editor's panes go where the single panel is, as tabs; the dock shows the first once the panel is out. They go in first:
        // a pane left empty is removed from the layout.
        void ShowEditorPanes(IReadOnlyList<EditorPane> panes)
        {
            var single = Dock.Layout.Descendents().OfType<LayoutAnchorable>().Single(x => x.ContentId == "EditorPanel");
            var pane = (LayoutAnchorablePane)single.Parent;
            foreach (var editorPane in panes)
            {
                var view = (FrameworkElement)Activator.CreateInstance(editorPane.ViewType)!;
                view.DataContext = editorPane.DataContext;
                pane.Children.Add(new LayoutAnchorable { ContentId = editorPane.ContentId, Title = editorPane.Title, Content = view });
            }
            pane.RemoveChild(single);
        }

        // The app, when this thread owns it: a test can make the Application on another thread than the one it lays a view out on.
        static Application? App => Application.Current is { } app && app.Dispatcher.CheckAccess() ? app : null;

        void SaveLayoutOnExit(object? sender, ExitEventArgs e) => SaveLayout(sender, e);

        // Once: when the editor closes, or when the app exits with it open.
        void SaveLayout(object? sender, EventArgs e)
        {
            if (_savesTo is not { } host)
                return;
            _savesTo = null;
            host.Closing -= SaveLayout;
            if (App is { } app)
                app.Exit -= SaveLayoutOnExit;
            Layouts.Save(Dock, host.LayoutKey);
        }

        // The panes as they are now: the layout is replaced on load and reset, so the list is made each time it opens.
        void OnPanelsMenuOpened(object sender, RoutedEventArgs e)
        {
            PanelsMenu.Items.Clear();
            foreach (var pane in Dock.Layout.Descendents().OfType<LayoutAnchorable>().ToList())
            {
                var item = new MenuItem { Header = pane.Title, IsCheckable = true, IsChecked = pane.IsHidden == false };
                item.Checked += (_, _) => pane.Show();
                item.Unchecked += (_, _) => pane.Hide();
                PanelsMenu.Items.Add(item);
            }
        }

        void OnResetLayout(object sender, RoutedEventArgs e) => ResetLayout();

        /// <summary>The default layout back, and this editor type's saved one deleted.</summary>
        public void ResetLayout()
        {
            if (_defaultLayout == null)
                return;
            DockLayoutStore.Deserialize(Dock, _defaultLayout);
            if (DataContext is EditorHostBase host)
                Layouts.Delete(host.LayoutKey);
        }

        void OnViewDragOver(object sender, DragEventArgs e)
        {
            if (DroppedFiles.PackFile(e.Data) is not { } file)
                return;
            e.Effects = SceneObjectViewModel.IsModel(file) && DataContext is EditorHostBase { ModelDropTargets.Count: > 0 } ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        // A model dropped on the 3D view goes into the one part there is, or the part picked from a menu where it was dropped.
        void OnViewDrop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.PackFile(e.Data) is not { } file || DataContext is not EditorHostBase host)
                return;
            e.Handled = true;
            if (SceneObjectViewModel.IsModel(file) == false)
                return;

            var menu = ModelDropMenu(host.ModelDropTargets, file);
            if (menu == null)
                return;
            menu.PlacementTarget = (UIElement)sender;
            menu.Placement = PlacementMode.MousePoint;
            // After the drag's own loop has let go of the mouse, or the menu closes as it opens.
            Dispatcher.BeginInvoke(() => menu.IsOpen = true, DispatcherPriority.Input);
        }

        /// <summary>Null when the model went straight into the only part.</summary>
        public static ContextMenu? ModelDropMenu(IReadOnlyList<SceneObjectViewModel> parts, PackFile file)
        {
            if (parts.Count == 1)
                parts[0].SetMesh(file);
            if (parts.Count < 2)
                return null;

            var menu = new ContextMenu();
            foreach (var part in parts)
            {
                var item = new MenuItem { Header = part.HeaderName.TrimEnd(':') }; // the panel label is "Mount:"
                item.Click += (_, _) => part.SetMesh(file);
                menu.Items.Add(item);
            }
            return menu;
        }
    }
}
