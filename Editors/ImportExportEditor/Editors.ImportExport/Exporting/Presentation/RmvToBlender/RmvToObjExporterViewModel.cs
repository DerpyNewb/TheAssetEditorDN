using Editors.ImportExport.Blender;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.RmvToBlender;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Exporting.Presentation.RmvToBlender
{
    public class RmvToObjExporterViewModel(RmvToBlenderExporter exporter) : IExporterViewModel, IViewProvider<BlenderNoteView>
    {
        public string DisplayName => "OBJ (.obj, LOD0 only)";
        public string OutputExtension => ".obj";
        public string OutputFilter => "Wavefront OBJ (*.obj)|*.obj";
        public string Note => "The best LOD only, with textures, through Blender. An OBJ has no LODs, skeleton, weights or pivot, so use Blender (.blend) for a model you will bring back.";

        public ExportSupportEnum CanExportFile(PackFile file) =>
            FileExtensionHelper.IsRmvFile(file.Name) ? ExportSupportEnum.Supported : ExportSupportEnum.NotSupported;

        public void Execute(PackFile exportSource, string outputPath, bool generateImporter) =>
            BlenderResultReport.Show("Export to OBJ", exporter.ExportObj(exportSource, outputPath));
    }
}
