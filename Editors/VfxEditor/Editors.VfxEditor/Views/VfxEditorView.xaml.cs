using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Editors.VfxEditor.ViewModels;

namespace Editors.VfxEditor.Views
{
    public partial class VfxEditorView : UserControl
    {
        public VfxEditorView() => InitializeComponent();

        void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is VfxEditorViewModel viewModel)
                viewModel.SelectedNode = e.NewValue as VfxTreeNode;
        }

        // A key row is picked when one of its boxes takes focus, and stays picked when the focus moves on to a button.
        void KeyRow_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ((ListBoxItem)sender).IsSelected = true;
    }
}
