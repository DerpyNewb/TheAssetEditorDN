using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Commands;
using Shared.Core.Settings;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.SceneNodeEditor
{
    // The Skeleton panel's "Show the selected bone's weights": the editable model draws how strongly the selected bone moves
    // each vertex, blue for none through green to red for all of it, instead of its textures.
    public class WeightViewTests
    {
        const int Bone = 3, OtherBone = 7, UnusedBone = 20;

        // Karl Franz is drawn by the metal-rough shader, a Rome II skin by the spec-gloss one.
        [TestCase(false)]
        [TestCase(true)]
        public void ShowWeights_ColoursTheModelByTheSelectedBonesWeight(bool specGloss)
        {
            var runner = specGloss ? OpenRomeSkin(out var mainNode) : OpenKarl(out mainNode);
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode);
            Assert.That(meshes, Is.Not.Empty);

            SetWeights(meshes, new Vector4(Bone, OtherBone, 0, 0), new Vector4(0.5f, 0.5f, 0, 0));
            var background = RenderFrame(runner, hideModel: true);
            var textured = RenderFrame(runner);
            var model = Enumerable.Range(0, background.Length).Where(i => background[i] != textured[i]).ToList();
            Assert.That(model, Has.Count.GreaterThan(1000), "the model is not in the frame");

            mainNode.SkeletonNode.ShowWeights = true;
            mainNode.SkeletonNode.SelectedBoneIndex = Bone;
            Assert.That(Share(RenderFrame(runner), model, x => x.G > x.R + 40 && x.G > x.B + 40), Is.GreaterThan(0.8), "half weight draws green");

            mainNode.SkeletonNode.SelectedBoneIndex = UnusedBone;
            Assert.That(Share(RenderFrame(runner), model, x => x.B > x.R + 20 && x.B > x.G + 20), Is.GreaterThan(0.8), "no weight draws blue");

            SetWeights(meshes, new Vector4(Bone, 0, 0, 0), new Vector4(1, 0, 0, 0));
            mainNode.SkeletonNode.SelectedBoneIndex = Bone;
            Assert.That(Share(RenderFrame(runner), model, x => x.R > x.G + 40 && x.R > x.B + 40), Is.GreaterThan(0.8), "full weight draws red");

            mainNode.SkeletonNode.ShowWeights = false;
            var back = RenderFrame(runner);
            Assert.That(model.Count(i => back[i] == textured[i]), Is.GreaterThan(model.Count * 0.8), "switched off, the textures come back");
        }

        [Test]
        public void SkeletonPanel_KeepsTheBoneAndTheWeightViewWhenItIsOpenedAgain()
        {
            OpenKarl(out var mainNode);
            var skeletonNode = mainNode.SkeletonNode;
            Assert.That(skeletonNode.Skeleton, Is.Not.Null);

            var panel = new SkeletonSceneNodeViewModel();
            panel.Initialize(skeletonNode);
            panel.ShowWeights = true;
            panel.SelectedBone = panel.Bones[0].Children[0];
            var bone = panel.SelectedBone.BoneIndex;
            Assert.That(skeletonNode.WeightViewBone, Is.EqualTo(bone));

            var reopened = new SkeletonSceneNodeViewModel();
            reopened.Initialize(skeletonNode);
            Assert.That(reopened.ShowWeights, Is.True);
            Assert.That(reopened.SelectedBone?.BoneIndex, Is.EqualTo(bone));
            Assert.That(skeletonNode.WeightViewBone, Is.EqualTo(bone));

            reopened.ShowWeights = false;
            Assert.That(skeletonNode.WeightViewBone, Is.EqualTo(-1));
        }

        static AssetEditorTestRunner OpenKarl(out MainEditableNode mainNode)
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            mainNode = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            return runner;
        }

        static AssetEditorTestRunner OpenRomeSkin(out MainEditableNode mainNode)
        {
            var runner = new AssetEditorTestRunner(GameTypeEnum.Rome2);
            runner.CreateCaContainer();
            runner.LoadFolderPackFile(TestFiles.RomePackFileFolder);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(TestFiles.RomePack_MeshSkin);
            mainNode = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            Assert.That(SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode).Select(x => x.Material.Type),
                Is.All.AnyOf(CapabilityMaterialsEnum.SpecGlossPbr_Default, CapabilityMaterialsEnum.SpecGlossPbr_Advanced));
            return runner;
        }

        static void SetWeights(List<Rmv2MeshNode> meshes, Vector4 boneIndices, Vector4 weights)
        {
            foreach (var mesh in meshes)
            {
                for (var i = 0; i < mesh.Geometry.VertexCount(); i++)
                {
                    mesh.Geometry.SetVertexBlendIndex(i, boneIndices);
                    mesh.Geometry.SetVertexWeights(i, weights);
                }
                mesh.Geometry.RebuildVertexBuffer();
            }
        }

        static double Share(Color[] frame, List<int> pixels, Func<Color, bool> test) => pixels.Count(i => test(frame[i])) / (double)pixels.Count;

        static Color[] RenderFrame(AssetEditorTestRunner runner, bool hideModel = false)
        {
            var root = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode;
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(root);
            meshes.ForEach(x => x.IsVisible = !hideModel);
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            // Framed on the model, whatever its size.
            var bounds = meshes.Select(x => x.Geometry.BoundingBox).Aggregate(BoundingBox.CreateMerged);
            var centre = (bounds.Min + bounds.Max) / 2;
            var size = (bounds.Max - bounds.Min).Length();
            runner.GetRequiredServiceInCurrentEditorScope<ArcBallCamera>().ViewMatrixOverride = Matrix.CreateLookAt(centre + new Vector3(0, 0, size * 1.2f), centre, Vector3.Up);
            var components = runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            using var target = new RenderTarget2D(device, 256, 192, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            device.SetRenderTarget(target);
            foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                component.Update(time);
            foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                component.Draw(time);
            device.SetRenderTarget(null);
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            meshes.ForEach(x => x.IsVisible = true);
            return pixels;
        }
    }
}
