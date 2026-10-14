using System.Windows;
using System.Windows.Controls;
using Editors.Shared.Core.Common.ReferenceModel;
using Shared.Ui.Common;

namespace AnimationEditor.Common.ReferenceModel
{
    /// <summary>
    /// Interaction logic for ReferenceModelView.xaml
    /// </summary>
    public partial class SceneObjectView : UserControl
    {
        public SceneObjectView()
        {
            InitializeComponent();
        }

        // A model dragged from the pack tree goes in as Browse puts it; anything else is refused here rather than opened in a tab.
        void OnModelDragOver(object sender, DragEventArgs e)
        {
            if (DroppedFiles.PackFile(e.Data) is not { } file)
                return;
            e.Effects = SceneObjectViewModel.IsModel(file) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        void OnModelDrop(object sender, DragEventArgs e) => e.Handled = DataContext is SceneObjectViewModel part && TakeModel(e.Data, part);

        /// <summary>True for any pack tree file, which goes no further; a model goes into the part.</summary>
        public static bool TakeModel(IDataObject data, SceneObjectViewModel part)
        {
            if (DroppedFiles.PackFile(data) is not { } file)
                return false;
            if (SceneObjectViewModel.IsModel(file))
                part.SetMesh(file);
            return true;
        }
    }
}
