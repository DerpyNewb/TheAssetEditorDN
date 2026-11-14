using WindowHandling;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    public partial class ReplaceTexturePathsWindow : AssetEditorWindow
    {
        public ReplaceTexturePathsViewModel ViewModel { get; }

        public ReplaceTexturePathsWindow(ReplaceTexturePathsViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;
        }
    }
}
