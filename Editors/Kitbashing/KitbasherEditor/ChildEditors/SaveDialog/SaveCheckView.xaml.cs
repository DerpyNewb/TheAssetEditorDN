using System.Windows.Controls;

namespace Editors.KitbasherEditor.ChildEditors.SaveDialog
{
    // The check's content, apart from its window so a test can host it: an AssetEditorWindow needs the app's main window.
    public partial class SaveCheckView : UserControl
    {
        public SaveCheckView() => InitializeComponent();
    }
}
