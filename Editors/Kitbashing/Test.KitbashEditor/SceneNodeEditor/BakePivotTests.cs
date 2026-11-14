using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.SceneNodeEditor
{
    // Bake into mesh, beside the pivot in a mesh's panel: the pivot goes into the vertices and the mesh draws where it did,
    // on a node that is also moved, turned and scaled. One undo gives back the pivot and the vertices exactly.
    public class BakePivotTests
    {
        [Test]
        public void BakePivot_DrawsTheMeshWhereItWas_AndUndoes()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var root = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(root).First();
            var renderer = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();

            var pivot = new Vector3(0.1f, -0.2f, 0.3f);
            mesh.PivotPoint = pivot;
            mesh.Position = new Vector3(1, 0, -1);
            mesh.Orientation = Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, 0.1f);
            mesh.Scale = new Vector3(2, 1.5f, 0.5f);
            var vertices = (VertexPositionNormalTextureCustom[])mesh.Geometry.VertexArray.Clone();
            var drawnBefore = Drawn(mesh, renderer);

            var panel = runner.GetRequiredServiceInCurrentEditorScope<MeshViewModel>();
            panel.Initialize(mesh);
            panel.BakePivotCommand.Execute(null);

            Assert.That(mesh.PivotPoint, Is.EqualTo(Vector3.Zero));
            Assert.That(panel.Pivot.GetAsVector3(), Is.EqualTo(Vector3.Zero), "the panel still shows the old pivot");
            var drawnAfter = Drawn(mesh, renderer);
            for (var i = 0; i < drawnBefore.Count; i++)
                Assert.That(Vector3.Distance(drawnAfter[i], drawnBefore[i]), Is.LessThan(1e-4f), $"vertex {i} moved");

            runner.GetRequiredServiceInCurrentEditorScope<CommandManager>().Undo();
            Assert.That(mesh.PivotPoint, Is.EqualTo(pivot));
            Assert.That(mesh.Geometry.VertexArray, Is.EqualTo(vertices));
        }

        static List<Vector3> Drawn(Rmv2MeshNode mesh, RenderEngineComponent renderer)
        {
            mesh.Render(renderer, Matrix.Identity);
            return mesh.Geometry.VertexArray.Select(x => Vector3.Transform(new Vector3(x.Position.X, x.Position.Y, x.Position.Z), mesh.RenderMatrix)).ToList();
        }
    }
}
