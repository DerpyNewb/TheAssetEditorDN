using Editors.ImportExport.Blender;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.RmvToBlender;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Exporting.Presentation.RmvToBlender
{
    public class RmvToBlenderExporterViewModel(RmvToBlenderExporter exporter) : IExporterViewModel, IViewProvider<BlenderNoteView>
    {
        public string DisplayName => "Blender (.blend)";
        public string OutputExtension => ".blend";
        public string OutputFilter => "Blender (*.blend)|*.blend";
        public string Note => "Every LOD, the skeleton and the textures, through Blender and ChaosRobie's io_scene_rmv2 add-on. Import the .blend again to bring your edits back.";

        public ExportSupportEnum CanExportFile(PackFile file) =>
            FileExtensionHelper.IsRmvFile(file.Name) ? ExportSupportEnum.Supported : ExportSupportEnum.NotSupported;

        public void Execute(PackFile exportSource, string outputPath, bool generateImporter) =>
            BlenderResultReport.Show("Export to Blender", exporter.Export(exportSource, outputPath));
    }
}
