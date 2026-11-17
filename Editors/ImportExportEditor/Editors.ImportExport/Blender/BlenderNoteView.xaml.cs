using System.Windows.Controls;

namespace Editors.ImportExport.Blender
{
    public partial class BlenderNoteView : UserControl
    {
        public BlenderNoteView() => InitializeComponent();
    }

    public static class BlenderResultReport
    {
        /// <summary>Shows the error and warnings of a Blender run, if it has any.</summary>
        public static void Show(string title, BlenderResult result)
        {
            var list = new global::Shared.Core.ErrorHandling.ErrorList();
            if (!result.Ok)
                list.Error(title, result.Error ?? "Blender failed without a message.");
            foreach (var warning in result.Warnings)
                list.Warning(title, warning);
            if (list.HasData)
                CommonControls.BaseDialogs.ErrorListDialog.ErrorListWindow.ShowDialog(title, list, false);
        }
    }
}
