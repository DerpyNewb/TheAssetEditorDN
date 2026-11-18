using GameWorld.Core.Services.SceneSaving.Material;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.WsModel;
using Test.ImportExport.Blender;

namespace Test.ImportExport.Importing.Importers;

// An imported mesh gets a .wsmodel built from its rmv2 alone. Measured against CA: Karl's body and hammer, written from their
// rmv2 files, must point every LOD and part at a material with CA's shader, vertex type, alpha and textures.
public class RmvWsModelWriterTests
{
    [TestCase(RmvToBlenderExportTests.KarlBody)]
    [TestCase(RmvToBlenderExportTests.KarlHammer)]
    public void WrittenFromTheRmv2Alone_MatchesCasOwnWsModel(string rmvPath)
    {
        var ca = RmvToBlenderExportTests.KarlPack();
        var rmv = ModelFactory.Create().Load(ca.FindFile(rmvPath)!.DataSource.ReadData());
        var caWsModel = new WsModelFile(ca.FindFile(Path.ChangeExtension(rmvPath, ".wsmodel"))!);

        var files = RmvWsModelWriter.Create(rmv, rmvPath, GameTypeEnum.Warhammer3).ToDictionary(x => x.PackPath, x => x.Bytes);

        Assert.That(files.Keys, Has.All.Matches<string>(p => p == p.ToLowerInvariant() && !p.Contains('\\') && !p.Contains(' ')), "pack paths are lower case, forward-slashed, without spaces");
        var ours = new WsModelFile(PackFile.CreateFromBytes("x.wsmodel", files[Normalise(Path.ChangeExtension(rmvPath, ".wsmodel"))]));
        Assert.That(Normalise(ours.GeometryPath), Is.EqualTo(Normalise(caWsModel.GeometryPath)));
        Assert.That(ours.MaterialList.Select(x => (x.LodIndex, x.PartIndex)), Is.EquivalentTo(caWsModel.MaterialList.Select(x => (x.LodIndex, x.PartIndex))));

        foreach (var entry in caWsModel.MaterialList)
        {
            var where = $"LOD {entry.LodIndex} part {entry.PartIndex}";
            var caMaterial = new WsModelMaterialFile(ca.FindFile(entry.MaterialPath)!);
            var ourPath = ours.MaterialList.Single(x => x.LodIndex == entry.LodIndex && x.PartIndex == entry.PartIndex).MaterialPath;
            Assert.That(files.ContainsKey(ourPath), Is.True, $"{where}: the wsmodel names {ourPath}, which was not written");
            var ourMaterial = new WsModelMaterialFile(PackFile.CreateFromBytes("x.material", files[ourPath]));

            Assert.That(ourMaterial.ShaderPath, Is.EqualTo(caMaterial.ShaderPath).IgnoreCase, $"{where}: shader");
            Assert.That(ourMaterial.VertexType, Is.EqualTo(caMaterial.VertexType), $"{where}: vertex type");
            Assert.That(ourMaterial.Alpha, Is.EqualTo(caMaterial.Alpha), $"{where}: alpha");
            Assert.That(ourMaterial.Textures.ToDictionary(x => x.Key, x => Normalise(x.Value)),
                Is.EquivalentTo(caMaterial.Textures.ToDictionary(x => x.Key, x => Normalise(x.Value))), $"{where}: textures");
        }
    }

    [Test]
    public void TwoModelsInOneFolderNeverShareAMaterialPath()
    {
        var ca = RmvToBlenderExportTests.KarlPack();
        var rmv = ModelFactory.Create().Load(ca.FindFile(RmvToBlenderExportTests.KarlBody)!.DataSource.ReadData());

        var a = RmvWsModelWriter.Create(rmv, "test/a.rigid_model_v2", GameTypeEnum.Warhammer3).Select(x => x.PackPath);
        var b = RmvWsModelWriter.Create(rmv, "test/b.rigid_model_v2", GameTypeEnum.Warhammer3).Select(x => x.PackPath);

        Assert.That(a.Intersect(b), Is.Empty);
    }

    static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}
