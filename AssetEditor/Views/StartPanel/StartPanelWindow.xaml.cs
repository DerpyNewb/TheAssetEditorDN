using System.Windows;
using AssetEditor.ViewModels;

namespace AssetEditor.Views.StartPanel
{
    public partial class StartPanelWindow : Window
    {
        public StartPanelWindow(StartPanelViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseRequested += Close;
        }
    }
}
