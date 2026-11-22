using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.LoadAndSave
{
    // A cloth mesh (six vanilla WH3 cloaks have one, with collision shapes) carries an undecoded physics block after its weighted
    // header. A Kitbash save keeps the mesh's type and block while it has the vertices it loaded with, and saves it as a plain
    // weighted mesh once its vertex count has changed. Karl's first mesh stands in for one, made a cloth mesh in memory.
    internal class LoadAndSave_PhysicsMeshes
    {
        static readonly byte[] s_block = [.. Enumerable.Range(1, 40).Select(x => (byte)x)];

        [TestCase(false, ModelMaterialEnum.cloth, TestName = "KitbashSave_KeepsAClothMeshsTypeAndPhysicsBlock")]
        [TestCase(true, ModelMaterialEnum.weighted, TestName = "KitbashSave_DropsThePhysicsBlock_WhenTheVerticesChanged")]
        public void KitbashSave_PhysicsMesh(bool deleteFaces, ModelMaterialEnum expectedType)
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(TestFiles.KarlPackFile, true);

            var file = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!;
            var rmv = ModelFactory.Create().Load(file.DataSource.ReadData());
            var cloth = (WeightedMaterial)rmv.ModelList[0][0].Material;
            cloth.MaterialId = ModelMaterialEnum.cloth;
            cloth.PhysicsBlock = s_block;
            rmv.RecalculateOffsets();
            runner.PackFileService.SaveFile(file, ModelFactory.Create().Save(rmv));

            runner.CommandFactory.Create<OpenEditorCommand>().Execute(file);
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.SceneManager>().RootNode)
                .First(x => x.RmvMaterial.MaterialId == ModelMaterialEnum.cloth);
            if (deleteFaces)
            {
                var before = mesh.Geometry.VertexCount();
                mesh.Geometry.RemoveFaces([.. Enumerable.Range(0, 200).Select(x => x * 3)]);
                Assert.That(mesh.Geometry.VertexCount(), Is.LessThan(before));
            }

            var saveSettings = runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            saveSettings.IsUserInitialized = true;
            saveSettings.DisplayDialogOnNextSave = false;
            runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>().Create<SaveCommand>().Execute();

            var saved = ModelFactory.Create().Load(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl, outputPackFile)!.DataSource.ReadData());
            var savedMaterial = (WeightedMaterial)saved.ModelList[0][0].Material;
            Assert.That(savedMaterial.MaterialId, Is.EqualTo(expectedType));
            Assert.That(savedMaterial.PhysicsBlock, Is.EqualTo(deleteFaces ? Array.Empty<byte>() : s_block));
            Assert.That(saved.ModelList.Skip(1).SelectMany(x => x).Select(x => x.Material.MaterialId), Has.None.EqualTo(ModelMaterialEnum.cloth),
                "the lower LODs are rebuilt from LOD 0 with fewer vertices, so none keeps the block");
        }
    }
}
