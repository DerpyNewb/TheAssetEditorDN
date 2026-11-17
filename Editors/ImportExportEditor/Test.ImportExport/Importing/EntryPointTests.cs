using Editors.ImportExport.Blender;
using Editors.ImportExport.ContextMenu;
using Editors.ImportExport.Exporting;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.RmvToBlender;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Presentation.RmvToBlender;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.Importing;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;

namespace Test.ImportExport.Importing;

public class EntryPointTests
{
    static (RootTreeNode Root, TreeNode Rmv2, TreeNode Dds, TreeNode Folder) Tree(bool isCa)
    {
        var pfs = new PackFileService(null);
        var container = pfs.CreateNewPackFileContainer("tree", PackFileVersion.PFH5, PackFileCAType.MOD);
        pfs.AddFilesToPack(container, [
            new NewPackFileEntry("models", PackFile.CreateFromBytes("thing.rigid_model_v2", [0])),
            new NewPackFileEntry("models", PackFile.CreateFromBytes("thing.dds", [0])),
        ]);
        container.IsCaPackFile = isCa;
        var root = new RootTreeNode("tree", container);
        var folder = new TreeNode("models", NodeType.Directory, root);
        root.AddChild(folder);
        var rmv2 = new TreeNode("thing.rigid_model_v2", NodeType.File, folder);
        var dds = new TreeNode("thing.dds", NodeType.File, folder);
        folder.AddChild(rmv2);
        folder.AddChild(dds);
        return (root, rmv2, dds, folder);
    }

    static ExportToCommand ExportTo()
    {
        var runner = new BlenderRunner(new ApplicationSettingsService(GameTypeEnum.Warhammer3));
        var exporter = new RmvToBlenderExporter(new PackFileService(null), runner);
        IExporterViewModel[] all =
        [
            new RmvToBlenderExporterViewModel(exporter),
            new RmvToGltfStaticExporterViewModel(new RmvToGltfStaticExporter(null!, null!, null!)),
            new RmvToObjExporterViewModel(exporter),
        ];
        return new ExportToCommand(all);
    }

    [Test]
    public void ExportToCommandAppearsOnlyOnRmv2()
    {
        var (_, rmv2, dds, folder) = Tree(isCa: true);
        var command = ExportTo();

        Assert.That(command.ShouldAdd(rmv2), Is.True);
        Assert.That(command.ShouldAdd(dds), Is.False);
        Assert.That(command.ShouldAdd(folder), Is.False);
        Assert.That(command.GetDisplayName(rmv2), Is.EqualTo("Export to"));
        Assert.That(command.Choices(rmv2).Select(c => c.Name), Is.EqualTo(new[] { "Blender (.blend)...", "glTF (.glb)...", "OBJ (.obj, LOD0 only)..." }));
    }

    [Test]
    public void ImportCommandAppearsOnFoldersAndRootOfANonCaPack()
    {
        var helper = new Mock<IImportFileContextMenuHelper>().Object;
        var (root, rmv2, _, folder) = Tree(isCa: false);
        var (caRoot, _, _, caFolder) = Tree(isCa: true);
        var command = new AdvancedImportCommand(new PackFileService(null), helper);

        Assert.That(command.ShouldAdd(folder), Is.True);
        Assert.That(command.ShouldAdd(root), Is.True);
        Assert.That(command.ShouldAdd(rmv2), Is.False);
        Assert.That(command.ShouldAdd(caFolder), Is.False);
        Assert.That(command.ShouldAdd(caRoot), Is.False);
        Assert.That(command.GetDisplayName(folder), Is.EqualTo("Import 3D model or image..."));
    }

    [TestCase("thing.blend", true)]
    [TestCase("thing.OBJ", true)]
    [TestCase("thing.gltf", true)]
    [TestCase("thing.glb", true)]
    [TestCase("thing.txt", false)]
    [TestCase("thing.png", false)]
    public void DropHandlerAcceptsModelFilesOnly(string file, bool accepted)
    {
        var handler = new ImportDropHandler(new Mock<IUiCommandFactory>().Object);

        Assert.That(handler.CanHandle(file), Is.EqualTo(accepted));
    }
}
