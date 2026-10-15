using System.Windows;
using System.Windows.Controls;
using AssetEditor.ViewModels;

namespace AssetEditor.Views
{
    /// <summary>
    /// Interaction logic for MenuBarView.xaml
    /// </summary>
    public partial class MenuBarView : UserControl
    {
        public MenuBarView()
        {
            InitializeComponent();
        }

        // Only the File menu's own opening: the event bubbles up from every submenu under it.
        void OnFileMenuOpened(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource == sender && DataContext is MainViewModel { MenuBar: { } menuBar })
                menuBar.RefreshRestorableBackups();
        }
    }
}
