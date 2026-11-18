using Editors.ImportExport.Exporting.Exporters.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Exporters.DdsToNormalPng;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.TestUtility;
using Test.TestingUtility.TestUtility;

namespace Test.ImportExport.Exporting.Exporters.RmvToGlft
{
    // A skinned model holding a mesh with no weights used to fail the whole export: "Weights must sum 1, but found 0"
    // (hit 2026-10-05). Karl with his first part's weights taken off stands in for such a model.
    public class GltfUnweightedMeshTests
    {
        const string KarlRmv = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";

        [Test]
        public void ASkinnedModelWithAnUnweightedPart_Exports()
        {
            var pfs = PackFileSerivceTestHelper.Create(PathHelper.GetDataFolder("Data\\Karl_and_celestialgeneral_Pack"));
            var file = pfs.FindFile(KarlRmv)!;
            var rmv = ModelFactory.Create().Load(file.DataSource.ReadData());
            var lod0 = rmv.ModelList[0];
            Assert.That(lod0.Length, Is.GreaterThan(1), "needs a weighted part beside the unweighted one");
            ((WeightedMaterial)lod0[0].Material).BinaryVertexFormat = VertexFormat.Static;    // a rigid part, as a kitbashed prop is
            foreach (var vertex in lod0[0].Mesh.VertexList)
            {
                vertex.WeightCount = 0;
                vertex.BoneIndex = [];
                vertex.BoneWeight = [];
            }
            rmv.RecalculateOffsets();
            var edited = PackFile.CreateFromBytes(Path.GetFileName(KarlRmv), ModelFactory.Create().Save(rmv));
            var output = pfs.CreateNewPackFileContainer("out", PackFileVersion.PFH5, PackFileCAType.MOD, true);
            pfs.AddFilesToPack(output, [new NewPackFileEntry(Path.GetDirectoryName(KarlRmv)!, edited)]);
            Assert.That(ModelFactory.Create().Load(edited.DataSource.ReadData()).ModelList[0][0].Mesh.VertexList.All(v => v.WeightCount == 0));

            var eventHub = new Mock<IGlobalEventHub>();
            var sceneSaver = new TestGltfSceneSaver();
            var exporter = new RmvToGltfExporter(sceneSaver, new GltfMeshBuilder(),
                new GltfTextureHandler(new Mock<IDdsToNormalPngExporter>().Object, new Mock<IDdsToMaterialPngExporter>().Object),
                new GltfSkeletonBuilder(pfs), new GltfAnimationBuilder(pfs), new SkeletonAnimationLookUpHelper(pfs, eventHub.Object), new ModelAssetResolver(pfs));

            exporter.Export(new RmvToGltfExporterSettings(edited, [], @"C:\test\unweighted.glb", false, false, false, ExportAnimations: true, false));

            Assert.That(sceneSaver.IsSaveCalled, Is.True);
            Assert.That(sceneSaver.ModelRoot!.LogicalMeshes, Has.Count.EqualTo(lod0.Length));
            Assert.That(sceneSaver.ModelRoot.LogicalSkins, Is.Not.Empty, "exported without its skeleton, which is not the case that failed");
        }
    }
}
