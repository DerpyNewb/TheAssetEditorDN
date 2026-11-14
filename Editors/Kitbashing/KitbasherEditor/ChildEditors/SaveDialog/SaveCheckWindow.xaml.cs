using WindowHandling;

namespace Editors.KitbasherEditor.ChildEditors.SaveDialog
{
    // Modal only while a save asks whether to save anyway. Check model shows it modeless, one per Kitbash tab.
    public partial class SaveCheckWindow : AssetEditorWindow
    {
        public SaveCheckViewModel ViewModel { get; }

        public SaveCheckWindow(SaveCheckViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
            ViewModel.CloseRequested += Close;
            Closed += (_, _) => ViewModel.CloseRequested -= Close;
        }
    }
}
