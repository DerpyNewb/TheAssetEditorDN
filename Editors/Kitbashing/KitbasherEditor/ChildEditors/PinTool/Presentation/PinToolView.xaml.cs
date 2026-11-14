using System.Windows;
using System.Windows.Controls;
using Editors.KitbasherEditor.ChildEditors.PinTool;

namespace Editors.KitbasherEditor.ViewModels.PinTool
{
    // The Pin tool's content, apart from its window so a test can host it: an AssetEditorWindow needs the app's main window.
    public partial class PinToolView : UserControl
    {
        public PinToolView()
        {
            InitializeComponent();
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is PinToolViewModel viewModel && viewModel.Apply())
                Window.GetWindow(this)?.Close();
        }
    }
}
