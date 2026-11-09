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

        // Typing in Fragment or Slot filters the list; open it so the matches show. Opening an editable box selects all its
        // text, which the next key would replace, so the caret goes back where it was.
        void OnFilterBoxKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape or System.Windows.Input.Key.Tab
                or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)
                return;
            if (sender is not ComboBox box || box.IsDropDownOpen || box.Template.FindName("PART_EditableTextBox", box) is not TextBox text)
                return;

            var caret = text.CaretIndex;
            box.IsDropDownOpen = true;
            text.SelectionLength = 0;
            text.CaretIndex = caret;
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
