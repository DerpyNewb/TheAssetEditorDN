using System.Windows.Controls;
using System.Windows.Input;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SlotGridView : UserControl
    {
        public SlotGridView()
        {
            InitializeComponent();
        }

        // A slot picked in the Scene panel picks its row, which is brought into view.
        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Grid.SelectedItem != null)
                Grid.ScrollIntoView(Grid.SelectedItem);
        }

        // Ctrl+Z and Ctrl+Y take the grid's edits back and forward, except inside a cell being typed in, which keeps its own.
        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not SlotGridViewModel grid || Keyboard.Modifiers != ModifierKeys.Control || e.OriginalSource is TextBox)
                return;
            if (e.Key == Key.Z)
                grid.UndoCommand.Execute(null);
            else if (e.Key == Key.Y)
                grid.RedoCommand.Execute(null);
            else
                return;
            e.Handled = true;
        }
    }
}
