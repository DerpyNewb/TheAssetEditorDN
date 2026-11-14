using Editors.KitbasherEditor.ChildEditors.PinTool;
using WindowHandling;

namespace Editors.KitbasherEditor.ViewModels.PinTool
{
    public partial class PinToolWindow : AssetEditorWindow
    {
        public PinToolWindow(PinToolViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
