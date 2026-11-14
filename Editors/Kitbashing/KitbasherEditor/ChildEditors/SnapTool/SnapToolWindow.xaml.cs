using GameWorld.Core.WpfWindow;
using WindowHandling;

namespace Editors.KitbasherEditor.ChildEditors.SnapTool
{
    public partial class SnapToolWindow : AssetEditorWindow
    {
        private readonly SnapToolViewModel _viewModel;
        private readonly IWpfGame _game;

        public SnapToolWindow(SnapToolViewModel viewModel, IWpfGame game)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            _game = game;

            // A component, so the target and the picked bone draw in the 3D view while the window is open.
            _game.AddComponent(_viewModel);
        }

        public void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => _game.RemoveComponent(_viewModel);
    }
}
