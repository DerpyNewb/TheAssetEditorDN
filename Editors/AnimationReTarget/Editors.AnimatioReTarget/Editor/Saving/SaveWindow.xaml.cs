using System.Windows;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    /// <summary>
    /// Interaction logic for SaveWindow.xaml
    /// </summary>
    public partial class SaveWindow : Window
    {
        SaveManager _saveManager;

        public SaveWindow(SaveSettings viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        public void Initialize(SaveManager saveManager)
        {
            _saveManager = saveManager;
        }

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            _saveManager.SaveAnimation();
            Close();
        }

        private void ExportScaledMeshClick(object sender, RoutedEventArgs e) => _saveManager.ExportScaledMesh();

        private void BatchExportClick(object sender, RoutedEventArgs e)
        {
            Close();
            _saveManager.ShowBatchExport();
        }

        private void BatchExportSetsClick(object sender, RoutedEventArgs e)
        {
            Close();
            _saveManager.ShowBatchExportFromSets();
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
