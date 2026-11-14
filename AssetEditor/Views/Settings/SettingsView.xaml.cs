using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shared.Ui.Common.MenuSystem;

namespace AssetEditor.Views.Settings
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is HotkeyRow row && row.Capture(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers))
                e.Handled = true;
        }
    }
}
