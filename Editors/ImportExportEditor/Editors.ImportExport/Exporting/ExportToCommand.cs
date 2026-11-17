using System.IO;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Presentation.RmvToBlender;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Editors.ImportExport.Exporting
{
    /// <summary>Right-click a model > Export to > Blender / glTF / OBJ: a save dialog, then that exporter's defaults.</summary>
    public class ExportToCommand(IEnumerable<IExporterViewModel> exporters) : IContextMenuCommand, IContextMenuChoices
    {
        public string GetDisplayName(TreeNode node) => "Export to";
        public bool ShouldAdd(TreeNode node) => node.NodeType == NodeType.File && FileExtensionHelper.IsRmvFile(node.Name);
        public bool IsEnabled(TreeNode node) => true;
        public void Configure(TreeNode node) { }
        public void Execute() { }

        public IEnumerable<(string Name, Action Choose)> Choices(TreeNode node)
        {
            var list = exporters.ToList();
            var blender = list.OfType<RmvToBlenderExporterViewModel>().FirstOrDefault();
            var gltf = list.OfType<RmvToGltfStaticExporterViewModel>().FirstOrDefault();
            var obj = list.OfType<RmvToObjExporterViewModel>().FirstOrDefault();
            if (blender != null)
                yield return ("Blender (.blend)...", () => Export(node, blender, blender.OutputFilter, ".blend"));
            if (gltf != null)
                yield return ("glTF (.glb)...", () => Export(node, gltf, "Binary glTF (*.glb)|*.glb|glTF (*.gltf)|*.gltf", ".glb"));
            if (obj != null)
                yield return ("OBJ (.obj, LOD0 only)...", () => Export(node, obj, obj.OutputFilter, ".obj"));
        }

        static void Export(TreeNode node, IExporterViewModel exporter, string filter, string extension)
        {
            var file = TreeNodeHelper.GetPackFile(node);
            if (file == null)
                return;
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(file.Name), DefaultExt = extension, Filter = filter };
            if (dialog.ShowDialog() != true)
                return;
            exporter.Initialize(file);
            exporter.Execute(file, dialog.FileName, false);
        }
    }
}
