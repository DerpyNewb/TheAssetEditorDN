using Editors.ImportExport.Misc;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;

namespace Editors.ImportExport.Importing
{
    /// <summary>A .blend, .obj, .gltf or .glb dropped on the Pack Explorer opens the import window for that folder.</summary>
    public class ImportDropHandler(IUiCommandFactory uiCommandFactory) : IExternalFileDropHandler
    {
        public bool CanHandle(string diskPath) => FileExtensionHelper.IsBlenderImportFile(diskPath) || FileExtensionHelper.IsGltfFile(diskPath);

        public void Handle(string diskPath, IPackFileContainer container, string packFolder) =>
            uiCommandFactory.Create<DisplayImportFileToolCommand>(x => x.Configure(container, packFolder, diskPath)).Execute();
    }
}
