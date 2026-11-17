using System.IO;
using Editors.ImportExport.Exporting.Exporters.RmvToBlender;
using Editors.ImportExport.Importing.Importers.BlenderToRmv;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;

namespace Test.ImportExport.Blender;

public class RmvToObjTests
{
    static HashSet<(int, int)> Uvs(IEnumerable<RmvModel> meshes) =>
        meshes.SelectMany(m => m.Mesh.VertexList).Select(v => ((int)MathF.Round(v.Uv.X * 1000), (int)MathF.Round(v.Uv.Y * 1000))).ToHashSet();

    [Test]
    [CancelAfter(300_000)]
    public void HammerObjRoundTripKeepsPositionsAndUvs()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var source = RmvToBlenderExportTests.KarlPack();
        var original = source.FindFile(RmvToBlenderExportTests.KarlHammer)!;
        var obj = Path.Combine(RmvToBlenderExportTests.OutFolder("obj"), "hammer.obj");

        var exported = new RmvToBlenderExporter(source, runner).ExportObj(original, obj);

        Assert.That(exported.Ok, Is.True, exported.Error);
        Assert.That(File.Exists(obj), Is.True);
        var pfs = new PackFileService(null);
        var container = pfs.CreateNewPackFileContainer("dest", PackFileVersion.PFH5, PackFileCAType.MOD);
        var imported = new BlenderImporter(pfs, runner).Import(obj, container, "test", null, 0, false);
        Assert.That(imported.Result.Ok, Is.True, imported.Result.Error);
        var before = ModelFactory.Create().Load(original.DataSource.ReadData());
        var after = ModelFactory.Create().Load(pfs.FindFile(imported.AddedPackPaths[0])!.DataSource.ReadData());
        Assert.That(after.Header.LodCount, Is.EqualTo(1));
        Assert.That(BlenderImportTests.Positions(after.ModelList[0]), Is.EquivalentTo(BlenderImportTests.Positions(before.ModelList[0])), "positions (a mirror fails this)");
        Assert.That(Uvs(after.ModelList[0]), Is.EquivalentTo(Uvs(before.ModelList[0])), "UVs (a V flip fails this)");
    }
}
