using KitbasherEditor.ViewModels.BmiEditor;
using WindowHandling;

namespace KitbasherEditor.Views.EditorViews
{
    public partial class BmiWindow : AssetEditorWindow
    {
        public BmiViewModel ViewModel { get; }

        public BmiWindow(BmiViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }
    }
}
