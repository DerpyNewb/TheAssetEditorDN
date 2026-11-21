using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.BmiEditor;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.UiCommands
{
    // The BMI tool, restored 2026-11-22: Apply pushes each vertex of Karl Franz's largest mesh out along its normal by the
    // scale factor times the weight it gives the ticked bones, leaves every other vertex where it was, keeps the vertices
    // that share a position together, and undoes in one step.
    public class BmiToolTests
    {
        AssetEditorTestRunner _runner = null!;
        Rmv2MeshNode _mesh = null!;
        VertexPositionNormalTextureCustom[] _vertices = null!;

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
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();

        static float WeightOn(VertexPositionNormalTextureCustom v, int bone) =>
            v.GetBoneIndexs().Zip(v.GetBoneWeights()).Where(x => x.First == bone).Sum(x => x.Second);

        static IEnumerable<SkeletonBoneNode> All(IEnumerable<SkeletonBoneNode> nodes) => nodes.SelectMany(x => All(x.Children).Prepend(x));

        [Test]
        public void Apply_GrowsOnlyTheTickedBone_ByFactorTimesWeight_AndUndoes()
        {
            var skeletonFile = Service<ISkeletonAnimationLookUpHelper>().GetSkeletonFileFromName(_mesh.Geometry.SkeletonName)!;
            var bmi = Service<BmiViewModel>();
            bmi.Initialize(new GameSkeleton(skeletonFile, null), _mesh);

            // The bone with the most weight in the mesh, so plenty of vertices move and plenty stay.
            var bone = Enumerable.Range(0, skeletonFile.Bones.Length).MaxBy(b => _vertices.Sum(v => WeightOn(v, b)));
            foreach (var node in All(bmi.Bones))
                node.IsChecked = node.BoneIndex == bone;
            const float factor = 0.01f;
            bmi.ScaleFactor.Value = factor;

            bmi.Apply();

            var moved = 0;
            for (var i = 0; i < _vertices.Length; i++)
            {
                var distance = (_mesh.Geometry.VertexArray[i].Position3() - _vertices[i].Position3()).Length();
                var weight = WeightOn(_vertices[i], bone);
                Assert.That(distance, Is.EqualTo(factor * weight).Within(1e-5f), $"vertex {i}, weight {weight}");
                if (weight > 0)
                    moved++;
            }
            Assert.That(moved, Is.InRange(_vertices.Length / 20, _vertices.Length - _vertices.Length / 20), "the chosen bone should move some of the mesh, not none or all");

            // The direction is the sum of the normals of every vertex within 0.01 of it, found by comparing every pair, as the
            // tool did before its grid search. Checked on every 10th vertex the bone moves.
            var sampled = 0;
            var nearButNotSame = 0;
            for (var i = 0; i < _vertices.Length; i++)
            {
                if (WeightOn(_vertices[i], bone) <= 0 || i % 10 != 0)
                    continue;
                var p = _vertices[i].Position3();
                var near = _vertices.Where(v => (v.Position3() - p).LengthSquared() < 0.0001f).ToList();
                nearButNotSame += near.Count(v => v.Position3() != p);
                var expected = Vector3.Normalize(near.Aggregate(Vector3.Zero, (sum, v) => sum + v.Normal));
                var actual = Vector3.Normalize(_mesh.Geometry.VertexArray[i].Position3() - p);
                Assert.That(Vector3.Dot(expected, actual), Is.GreaterThan(0.9999f), $"vertex {i} moved off its shared normal");
                sampled++;
            }
            Assert.That(sampled, Is.GreaterThan(50));
            Assert.That(nearButNotSame, Is.GreaterThan(0), "no sampled vertex has a near neighbour at another position, so the grid's neighbour cells are untested");

            // Vertices that share a position and the same weights (a UV seam) must still share it, or the seam tears open.
            var seams = _vertices.Select((v, i) => (v, i))
                .GroupBy(x => (x.v.Position3(), x.v.BlendIndices, x.v.BlendWeights))
                .Where(g => g.Count() > 1 && WeightOn(g.First().v, bone) > 0)
                .ToList();
            Assert.That(seams, Is.Not.Empty, "the mesh has no seam on the chosen bone, so the shared normal is untested");
            foreach (var seam in seams)
            {
                var first = _mesh.Geometry.VertexArray[seam.First().i].Position3();
                foreach (var (_, i) in seam)
                    Assert.That((_mesh.Geometry.VertexArray[i].Position3() - first).Length(), Is.LessThan(1e-6f), $"seam at vertex {i} tore open");
            }

            Service<CommandManager>().Undo();
            Assert.That(_mesh.Geometry.VertexArray, Is.EqualTo(_vertices));
        }
    }
}
