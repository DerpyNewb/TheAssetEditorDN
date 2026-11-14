using Editors.KitbasherEditor.ChildEditors.PinTool;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.PinTool
{
    // The Pin tool used to refuse a mesh with a pivot and tell the user to bake it first. Apply now bakes it, in the same undo
    // step as the pin, the skin wrap or the bind, says which meshes it baked, and one Undo takes back both.
    public class PinToolPivotTests
    {
        AssetEditorTestRunner _runner = null!;
        PinToolViewModel _tool = null!;
        List<Rmv2MeshNode> _karl = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var main = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            // Two of his meshes share a name, and the message names meshes, so each test takes meshes with names of their own.
            _karl = main.GetMeshesInLod(0, true).DistinctBy(x => x.Name).ToList();
            Assert.That(_karl, Has.Count.GreaterThanOrEqualTo(2));
            _tool = Service<PinToolViewModel>();
        }

        [TearDown]
        public void Close() => _tool.Dispose();

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        SelectionManager Selection => Service<SelectionManager>();
        void Undo() => Service<CommandManager>().Undo();

        [Test]
        public void Pin_BakesTheTargetsPivot_AndOneUndoTakesBackBoth()
        {
            var (target, source) = (_karl[0], _karl[1]);
            var pivot = new Vector3(0.1f, -0.2f, 0.3f);
            target.PivotPoint = pivot;
            source.PivotPoint = new Vector3(0, 0.5f, 0);
            var before = target.Geometry.VertexArray.ToArray();

            // Neither the target nor the source is refused for its pivot any more.
            ((ObjectSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([target], false);
            _tool.AddSelectionToAffectMeshCollectionCommand.Execute(null);
            ((VertexSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Vertex, source)).ModifySelection([5], false);
            _tool.PinMode.SetSelectionCommand.Execute(null);
            Assert.That(_tool.AffectedMeshCollection, Is.EqualTo(new[] { target }));
            Assert.That(_tool.PinMode.SelectedMesh, Is.SameAs(source));

            _tool.SelectedRiggingMode = RiggingMode.Pin;
            Assert.That(_tool.Apply(), Is.True);

            var pinned = source.Geometry.VertexArray[5];
            Assert.That(target.PivotPoint, Is.EqualTo(Vector3.Zero));
            for (var i = 0; i < before.Length; i++)
            {
                AreClose(target.Geometry.VertexArray[i].Position, before[i].Position + new Vector4(pivot, 0), i);
                Assert.That(target.Geometry.VertexArray[i].BlendIndices, Is.EqualTo(pinned.BlendIndices));
                Assert.That(target.Geometry.VertexArray[i].BlendWeights, Is.EqualTo(pinned.BlendWeights));
            }
            Assert.That(source.PivotPoint, Is.EqualTo(new Vector3(0, 0.5f, 0)), "Pin's source gives weights only, so its pivot stays");
            _runner.Dialogs.Verify(x => x.ShowDialogBox(It.Is<string>(m => m.Contains(target.Name) && !m.Contains(source.Name) && m.EndsWith("the bake and the pin.")), PinToolViewModel.PivotsBakedTitle), Times.Once);

            Undo();
            Assert.That(target.PivotPoint, Is.EqualTo(pivot), "one undo left the pivot baked");
            Assert.That(target.Geometry.VertexArray, Is.EqualTo(before), "one undo left the pin");
        }

        // The target's vertex is drawn at z = 10 only through its pivot, and so is source B's triangle; source A's is drawn at z = 0.
        // Matched by where they are drawn, the target takes B's bone. Matched without either pivot, it would land on A's or on a tie.
        [Test]
        public void SkinWrap_MatchesWhereTheTargetAndItsSourcesAreDrawn_AndOneUndoTakesBackBoth()
        {
            var lift = new Vector3(0, 0, 10);
            var target = Mesh("target", UiVertexFormat.Cinematic, [Vertex(0.1f, 0.1f, 0, 99)]);
            var a = Mesh("a", UiVertexFormat.Cinematic, Triangle(bone: 3));
            var b = Mesh("b", UiVertexFormat.Cinematic, Triangle(bone: 7));
            target.PivotPoint = lift;
            b.PivotPoint = lift;
            var (targetBefore, bBefore) = (target.Geometry.VertexArray.ToArray(), b.Geometry.VertexArray.ToArray());

            _tool.AffectedMeshCollection.Add(target);
            _tool.SkinWrapMode.SourceMeshes.Add(a);
            _tool.SkinWrapMode.SourceMeshes.Add(b);
            _tool.SelectedRiggingMode = RiggingMode.SkinWrap;
            Assert.That(_tool.Apply(), Is.True);

            Assert.That(target.Geometry.VertexArray[0].BlendIndices.X, Is.EqualTo(7));
            Assert.That((target.PivotPoint, b.PivotPoint), Is.EqualTo((Vector3.Zero, Vector3.Zero)));
            Assert.That(b.Geometry.VertexArray[0].Position.Z, Is.EqualTo(10));
            _runner.Dialogs.Verify(x => x.ShowDialogBox(It.Is<string>(m => m.Contains(": target, b.") && m.EndsWith("the skin wrap.")), PinToolViewModel.PivotsBakedTitle), Times.Once);

            Undo();
            Assert.That((target.PivotPoint, b.PivotPoint), Is.EqualTo((lift, lift)));
            Assert.That(target.Geometry.VertexArray, Is.EqualTo(targetBefore));
            Assert.That(b.Geometry.VertexArray, Is.EqualTo(bBefore));
        }

        [Test]
        public void Bone_BakesOnlyTheMeshesWithAPivot_AndSaysNothingWhenThereAreNone()
        {
            var (withPivot, without) = (_karl[0], _karl[1]);
            var pivot = new Vector3(0.2f, 0, 0);
            withPivot.PivotPoint = pivot;
            var (withBefore, withoutBefore) = (withPivot.Geometry.VertexArray.ToArray(), without.Geometry.VertexArray.ToArray());
            _tool.AffectedMeshCollection.Add(withPivot);
            _tool.AffectedMeshCollection.Add(without);
            _tool.SelectedRiggingMode = RiggingMode.Bone;
            _tool.BoneMode.Bones.SelectedItem = _tool.BoneMode.Bones.PossibleValues[4];

            Assert.That(_tool.Apply(), Is.True);

            Assert.That(withPivot.PivotPoint, Is.EqualTo(Vector3.Zero));
            AreClose(withPivot.Geometry.VertexArray[0].Position, withBefore[0].Position + new Vector4(pivot, 0), 0);
            Assert.That(without.Geometry.VertexArray[0].Position, Is.EqualTo(withoutBefore[0].Position));
            Assert.That(without.Geometry.VertexArray.Select(x => x.BlendIndices.X), Is.All.EqualTo(4));
            _runner.Dialogs.Verify(x => x.ShowDialogBox(It.Is<string>(m => m.Contains(withPivot.Name) && !m.Contains(without.Name) && m.EndsWith("the bake and the bind.")), PinToolViewModel.PivotsBakedTitle), Times.Once);

            Undo();
            Assert.That(withPivot.PivotPoint, Is.EqualTo(pivot));
            Assert.That(withPivot.Geometry.VertexArray, Is.EqualTo(withBefore));
            Assert.That(without.Geometry.VertexArray, Is.EqualTo(withoutBefore));

            _runner.Dialogs.Invocations.Clear();
            withPivot.PivotPoint = Vector3.Zero;
            Assert.That(_tool.Apply(), Is.True);
            _runner.Dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        static void AreClose(Vector4 actual, Vector4 expected, int vertex) =>
            Assert.That(Vector4.Distance(actual, expected), Is.LessThan(1e-5f), $"vertex {vertex}: {actual} is not {expected}");

        // The bake and the wrap each clone the geometry, so a clone's graphics context clones too.
        static IGraphicsCardGeometry Context()
        {
            var context = new Mock<IGraphicsCardGeometry>();
            context.Setup(x => x.Clone()).Returns(Context);
            return context.Object;
        }

        static VertexPositionNormalTextureCustom Vertex(float x, float y, float z, int bone) => new()
        {
            Position = new Vector4(x, y, z, 1),
            Normal = Vector3.Up,
            BlendIndices = new Vector4(bone, 0, 0, 0),
            BlendWeights = new Vector4(1, 0, 0, 0),
        };

        static VertexPositionNormalTextureCustom[] Triangle(int bone) => [Vertex(0, 0, 0, bone), Vertex(1, 0, 0, bone), Vertex(0, 1, 0, bone)];

        static Rmv2MeshNode Mesh(string name, UiVertexFormat format, VertexPositionNormalTextureCustom[] vertices)
        {
            var mesh = new MeshObject(Context(), "skel") { VertexArray = vertices, IndexArray = vertices.Length == 3 ? [0, 1, 2] : [0, 0, 0] };
            mesh.ChangeVertexType(format, false);

            var material = new Mock<IRmvMaterial>();
            material.Setup(m => m.ModelName).Returns(name);
            material.Setup(m => m.Clone()).Returns(() => material.Object);
            var shader = new Mock<CapabilityMaterial>(MockBehavior.Loose, CapabilityMaterialsEnum.SpecGlossPbr_Default, ShaderTypes.Pbr_SpecGloss, null!);
            return new Rmv2MeshNode(mesh, material.Object, shader.Object, null!);
        }
    }
}
