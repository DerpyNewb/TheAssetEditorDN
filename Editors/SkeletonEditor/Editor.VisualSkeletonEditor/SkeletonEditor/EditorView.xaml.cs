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

        // A bone picked in the view is shown in the list, scrolled to.
        void OnBoneSelected(object sender, System.Windows.RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem item)
                item.BringIntoView();
        }
    }
}
