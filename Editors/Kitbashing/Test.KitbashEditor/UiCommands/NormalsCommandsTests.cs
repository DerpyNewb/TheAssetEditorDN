using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.UiCommands
{
    // Tools > Recalculate normals and Flip faces on Karl Franz's largest mesh, each one undo step that gives the mesh back
    // exactly. Recalculated normals land near CA's own, which a normal built on the wrong side of the faces would not.
    public class NormalsCommandsTests
    {
        AssetEditorTestRunner _runner = null!;
        Rmv2MeshNode _mesh = null!;
        VertexPositionNormalTextureCustom[] _vertices = null!;
        ushort[] _indices = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var root = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            _mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(root).OrderByDescending(x => x.Geometry.VertexCount()).First();
            _vertices = (VertexPositionNormalTextureCustom[])_mesh.Geometry.VertexArray.Clone();
            _indices = (ushort[])_mesh.Geometry.IndexArray.Clone();

            var state = (ObjectSelectionState)Service<SelectionManager>().CreateSelectionSate(GeometrySelectionMode.Object, null!);
            state.ModifySelection([_mesh], false);
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();

        void AssertUndoGivesTheMeshBack()
        {
            Service<CommandManager>().Undo();
            Assert.That(_mesh.Geometry.VertexArray, Is.EqualTo(_vertices));
            Assert.That(_mesh.Geometry.IndexArray, Is.EqualTo(_indices));
        }

        [Test]
        public void RecalculateNormals_LandsNearCasNormals_AndUndoes()
        {
            Service<RecalculateNormalsCommand>().Execute();

            var angles = _vertices.Select((v, i) => MathHelper.ToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(v.Normal), _mesh.Geometry.VertexArray[i].Normal), -1, 1)))).ToList();
            Assert.That(angles.Average(), Is.LessThan(15), "recalculated normals are far from CA's");
            Assert.That(angles.Count(x => x > 0.5f), Is.GreaterThan(angles.Count / 10), "the normals barely changed, so nothing was recalculated");
            AssertUndoGivesTheMeshBack();
        }

        [Test]
        public void FlipFaces_TurnsTheNormalsRound_AndUndoes()
        {
            Service<FlipFacesCommand>().Execute();

            Assert.That(_mesh.Geometry.VertexArray.Select(x => x.Normal), Is.EqualTo(_vertices.Select(x => -x.Normal)));
            Assert.That(_mesh.Geometry.IndexArray.Take(3), Is.EqualTo(new[] { _indices[0], _indices[2], _indices[1] }));
            AssertUndoGivesTheMeshBack();
        }
    }
}
