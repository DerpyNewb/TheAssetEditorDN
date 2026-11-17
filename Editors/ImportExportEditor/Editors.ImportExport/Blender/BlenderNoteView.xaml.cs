using System.Windows.Controls;

namespace Editors.ImportExport.Blender
{
    public partial class BlenderNoteView : UserControl
    {
        public BlenderNoteView() => InitializeComponent();
    }

    public static class BlenderResultReport
    {
        /// <summary>One line per file written with its LOD count, then the error and every warning.</summary>
        public static global::Shared.Core.ErrorHandling.ErrorList Build(string title, BlenderResult result)
        {
            var list = new global::Shared.Core.ErrorHandling.ErrorList();
            if (result.Ok)
                foreach (var file in result.Files)
                    list.Ok(title, $"{file.Name}: {file.Lods} LOD{(file.Lods == 1 ? "" : "s")}");
            else
                list.Error(title, result.Error ?? "Blender failed without a message.");
            foreach (var warning in result.Warnings)
                list.Warning(title, warning);
            return list;
        }

        /// <summary>Shows what a Blender run wrote, and its error and warnings.</summary>
        public static void Show(string title, BlenderResult result)
        {
            var list = Build(title, result);
            if (list.HasData)
                CommonControls.BaseDialogs.ErrorListDialog.ErrorListWindow.ShowDialog(title, list, false);
        }
    }
}
