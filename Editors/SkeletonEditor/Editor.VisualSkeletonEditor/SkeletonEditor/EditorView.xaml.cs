using System.Windows.Controls;
using Shared.Ui.Common;

namespace Editor.VisualSkeletonEditor.SkeletonEditor
{
    /// <summary>
    /// Interaction logic for EditorView.xaml
    /// </summary>
    public partial class EditorView : UserControl
    {
        public EditorView()
        {
            InitializeComponent();
            UndoRedoKeys.Attach(this, () => (DataContext as SkeletonEditorViewModel)?.UndoAction(), () => (DataContext as SkeletonEditorViewModel)?.RedoAction());
        }
    }
}
