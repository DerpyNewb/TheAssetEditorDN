using System.Windows;
using System.Windows.Controls;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    public partial class ReplaceTexturePathsView : UserControl
    {
        public ReplaceTexturePathsView() => InitializeComponent();

        private void ReplaceButtonClick(object sender, RoutedEventArgs e)
        {
            if (((ReplaceTexturePathsViewModel)DataContext).Apply())
                Window.GetWindow(this)?.Close();
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
    }
}
