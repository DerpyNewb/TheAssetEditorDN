using System.Text.RegularExpressions;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.UiCommands
{
    // Tools > Mirror left to right on Karl Franz: every vertex lands at -x, each weight on a left bone goes onto its right
    // partner by name (and the other way round), and one undo gives back the mesh exactly.
    public class MirrorMeshCommandTests
    {
        AssetEditorTestRunner _runner = null!;
        Rmv2MeshNode _mesh = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var root = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            _mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(root).OrderByDescending(x => x.Geometry.VertexCount()).First();

            var state = (ObjectSelectionState)Service<SelectionManager>().CreateSelectionSate(GeometrySelectionMode.Object, null!);
            state.ModifySelection([_mesh], false);
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();

        Dictionary<int, string> BoneNames() => Service<ISkeletonAnimationLookUpHelper>().GetSkeletonFileFromName(_mesh.Geometry.SkeletonName)!.Bones.ToDictionary(b => b.Id, b => b.Name);

        static IEnumerable<(int Index, float Weight)> Influences(VertexPositionNormalTextureCustom v) =>
            new[] { (v.BlendIndices.X, v.BlendWeights.X), (v.BlendIndices.Y, v.BlendWeights.Y), (v.BlendIndices.Z, v.BlendWeights.Z), (v.BlendIndices.W, v.BlendWeights.W) }
                .Where(x => x.Item2 > 0)
                .Select(x => ((int)x.Item1, x.Item2));

        [Test]
        public void Mirror_PutsLeftWeightsOnRightBones_AndUndoesExactly()
        {
            var names = BoneNames();
            var vertices = (VertexPositionNormalTextureCustom[])_mesh.Geometry.VertexArray.Clone();
            var indices = (ushort[])_mesh.Geometry.IndexArray.Clone();

            Service<MirrorMeshCommand>().Execute();

            var swapped = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var after = _mesh.Geometry.VertexArray[i];
                Assert.That(after.Position.X, Is.EqualTo(-vertices[i].Position.X));
                var expected = Influences(vertices[i]).Select(x => (Opposite(names[x.Index], names), x.Weight));
                var actual = Influences(after).Select(x => (names[x.Index], x.Weight));
                Assert.That(actual, Is.EqualTo(expected), $"vertex {i}");
                swapped += Influences(vertices[i]).Count(x => names[x.Index].Contains("left") || names[x.Index].Contains("right"));
            }
            Assert.That(swapped, Is.GreaterThan(100), "the mesh has too few weights on side bones to test the swap");

            Service<CommandManager>().Undo();
            Assert.That(_mesh.Geometry.VertexArray, Is.EqualTo(vertices));
            Assert.That(_mesh.Geometry.IndexArray, Is.EqualTo(indices));
        }

        [Test]
        public void KeepBones_LeavesEveryWeightOnItsBone()
        {
            var vertices = (VertexPositionNormalTextureCustom[])_mesh.Geometry.VertexArray.Clone();

            Service<MirrorMeshKeepBonesCommand>().Execute();

            Assert.That(_mesh.Geometry.VertexArray.Select(x => x.BlendIndices), Is.EqualTo(vertices.Select(x => x.BlendIndices)));
            Assert.That(_mesh.Geometry.VertexArray[0].Position.X, Is.EqualTo(-vertices[0].Position.X));
        }

        // humanoid01 marks a side with the word left or right; a bone with no partner stays where it is.
        static string Opposite(string name, Dictionary<int, string> names)
        {
            var other = Regex.Replace(name, "(?<=^|_)(left|right)(?=$|_)", x => x.Value == "left" ? "right" : "left");
            return names.ContainsValue(other) ? other : name;
        }
    }
}
