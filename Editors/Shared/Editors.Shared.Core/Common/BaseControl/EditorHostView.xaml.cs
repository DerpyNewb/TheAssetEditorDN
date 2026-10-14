using System.Windows;
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
        public EditorHostView()
        {
            InitializeComponent();
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
