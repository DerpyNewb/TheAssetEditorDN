using Editors.KitbasherEditor.ChildEditors.PinTool;
using WindowHandling;

namespace Editors.KitbasherEditor.ViewModels.PinTool
{
    public partial class PinToolWindow : AssetEditorWindow
    {
        public PinToolViewModel ViewModel { get; }

        public PinToolWindow(PinToolViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }
    }
}
