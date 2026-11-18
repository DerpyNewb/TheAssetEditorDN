using Editors.KitbasherEditor.Core;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.SceneNodeEditor
{
    // A mesh is boxed and picked where it is drawn, its parents' transforms included. Both used to take the mesh's own matrix
    // and pivot only, so a mesh placed by its parent node, as every BMD component is, was boxed and picked where it would stand
    // unplaced.
    public class SelectionWorldMatrixTests
    {
        [Test]
        public void AMeshMovedByItsParent_IsBoxedAndPickedWhereItIsDrawn()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            T Service<T>() where T : notnull => runner.GetRequiredServiceInCurrentEditorScope<T>();

            var scene = Service<SceneManager>();
            var main = scene.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main)[0];
            mesh.PivotPoint = new Vector3(0.3f, 0.2f, -0.1f);
            var away = new Vector3(5, 0, 0);
            main.ModelMatrix = Matrix.CreateTranslation(away);
            ((ObjectSelectionState)Service<SelectionManager>().CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([mesh], false);

            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            var engine = Service<RenderEngineComponent>();
            engine.Update(time);
            scene.Draw(time);
            Service<SelectionManager>().Draw(time);

            var box = mesh.Geometry.BoundingBox;
            var drawnCentre = (box.Min + box.Max) / 2 + mesh.PivotPoint + away;
            var boxLines = engine.RenderLines.TakeLast(24).ToList();
            var boxCentre = boxLines.Aggregate(Vector3.Zero, (sum, x) => sum + x.Position) / boxLines.Count;
            Assert.That(Vector3.Distance(boxCentre, drawnCentre), Is.LessThan(1e-3f), "the selection box is around the mesh as drawn");

            var vertex = mesh.Geometry.GetVertexById(0) + mesh.PivotPoint;
            Assert.That(scene.SelectObjects(Around(vertex + away)), Does.Contain(mesh), "a box drawn round the mesh picks it");
            Assert.That(scene.SelectObjects(Around(vertex)), Is.Empty, "where it would stand unplaced there is nothing");

            var ray = new Ray(drawnCentre + new Vector3(0, 0, 50), -Vector3.UnitZ);
            var hit = scene.SelectObject(ray);
            Assert.That(hit, Is.Not.Null, "a click through the middle of the drawn model picks something of it");
        }

        // A 2 cm box seen from the front, as a drag on the screen gives.
        static BoundingFrustum Around(Vector3 point) =>
            new(Matrix.CreateLookAt(point + new Vector3(0, 0, 10), point, Vector3.Up) * Matrix.CreateOrthographic(0.02f, 0.02f, 0.1f, 100));
    }
}
