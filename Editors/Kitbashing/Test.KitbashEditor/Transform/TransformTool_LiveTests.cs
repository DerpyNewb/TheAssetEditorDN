using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.MenuBarViews;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;
using static KitbasherEditor.ViewModels.MenuBarViews.TransformToolViewModel;

namespace Test.KitbashEditor.Transform
{
    // The transform box under the toolbar moves the selection as its numbers are typed; there is no Apply. The box holds the
    // edit's total, so each change undoes the last preview and applies the total again, and the whole edit is one undo step.
    public class TransformTool_LiveTests
    {
        AssetEditorTestRunner _runner = null!;
        TransformToolViewModel _tool = null!;
        CommandManager _commands = null!;
        Rmv2MeshNode _mesh = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);

            var mainNode = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            _mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode).First();
            _tool = Service<TransformToolViewModel>();
            _commands = Service<CommandManager>();
            Assert.That(_commands.CanUndo(), Is.False, "opening the model left something to undo");
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        SelectionManager Selection => Service<SelectionManager>();
        Vector3 Vertex(int id) => _mesh.Geometry.GetVertexById(id);
        void SelectMesh() => ((ObjectSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([_mesh], false);
        static void AreClose(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-3), $"{actual} is not {expected}");

        [Test]
        public void Typing_MovesTheSelectionByTheTotal_AndOneUndoTakesTheWholeEditBack()
        {
            SelectMesh();
            var before = Vertex(0);
            _tool.SetMode(TransformMode.Translate);

            _tool.Vector3.X.TextValue = "1";
            AreClose(Vertex(0), before + new Vector3(1, 0, 0));
            _tool.Vector3.X.TextValue = "3";
            AreClose(Vertex(0), before + new Vector3(3, 0, 0));

            _commands.Undo();
            AreClose(Vertex(0), before);
            Assert.That(_commands.CanUndo(), Is.False);
            Assert.That(_tool.Vector3.X.Value, Is.EqualTo(0), "the box still shows a move the undo took back");
        }

        [Test]
        public void ClearingTheBox_LeavesNothingToUndo()
        {
            SelectMesh();
            var before = Vertex(0);
            _tool.SetMode(TransformMode.Translate);

            _tool.Vector3.X.TextValue = "3";
            _tool.Vector3.X.TextValue = "";

            AreClose(Vertex(0), before);
            Assert.That(_commands.CanUndo(), Is.False);
        }

        // A gizmo drag after typing is another undo step; the box starts over from where the drag left the model.
        [Test]
        public void AnotherCommand_EndsTheEdit_AndTheNextEditStartsFromThere()
        {
            SelectMesh();
            var before = Vertex(0);
            _tool.SetMode(TransformMode.Translate);
            _tool.Vector3.X.TextValue = "3";

            var gizmo = TransformGizmoWrapper.CreateFromSelectionState(Selection.GetState(), Service<IUiCommandFactory>(), Service<IScopedLogger>())!;
            gizmo.Start(_commands);
            gizmo.GizmoTranslateEvent(new Vector3(0, 2, 0), PivotType.ObjectCenter);
            gizmo.Stop(_commands);
            Assert.That(_tool.Vector3.X.Value, Is.EqualTo(0));

            _tool.Vector3.X.TextValue = "1";
            AreClose(Vertex(0), before + new Vector3(4, 2, 0));
        }

        [Test]
        public void SelectingAnotherMesh_EndsTheEdit_AndTheNextValueMovesTheNewSelection()
        {
            var mainNode = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var other = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode).Skip(1).First();
            SelectMesh();
            var before = Vertex(0);
            var otherBefore = other.Geometry.GetVertexById(0);
            _tool.SetMode(TransformMode.Translate);
            _tool.Vector3.X.TextValue = "3";

            ((ObjectSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([other], false);
            _tool.Vector3.X.TextValue = "1";

            AreClose(Vertex(0), before + new Vector3(3, 0, 0));
            AreClose(other.Geometry.GetVertexById(0), otherBefore + new Vector3(1, 0, 0));
        }

        // Emptying the box is not an edit: resetting it one axis at a time must not preview the axes still left on it.
        [Test]
        public void SwitchingTool_KeepsTheMove_AndEmptiesTheBoxWithoutTouchingTheModel()
        {
            SelectMesh();
            var before = Vertex(0);
            _tool.SetMode(TransformMode.Translate);
            _tool.Vector3.X.TextValue = "3";
            _tool.Vector3.Y.TextValue = "2";
            var pushed = 0;
            Service<IEventHub>().Register<CommandStackChangedEvent>(this, _ => pushed++);

            _tool.SetMode(TransformMode.Rotate);

            Assert.That(pushed, Is.EqualTo(0), "emptying the box pushed a transform");
            AreClose(Vertex(0), before + new Vector3(3, 2, 0));
            AreClose(_tool.Vector3.GetAsVector3(), Vector3.Zero);
        }

        // Typing 0.5 passes through 0, and a scale of 0 cannot be undone: the inverse of a flattened model is NaN.
        [Test]
        public void ScaleThroughZero_IsSkipped_AndBackToOne_LeavesNothingToUndo()
        {
            SelectMesh();
            var before = Vertex(0);
            var centre = _mesh.Geometry.MeshCenter;
            _tool.SetMode(TransformMode.Scale);

            _tool.Vector3.X.TextValue = "0";
            Assert.That(_commands.CanUndo(), Is.False);
            _tool.Vector3.X.TextValue = "0.5";
            AreClose(Vertex(0), new Vector3(centre.X + (before.X - centre.X) * 0.5f, before.Y, before.Z));

            _tool.Vector3.X.TextValue = "1";
            AreClose(Vertex(0), before);
            Assert.That(_commands.CanUndo(), Is.False);
        }

        // A weapon scaled about its own centre slides its grip off the hand; about the world origin, where it hangs from its bone,
        // the grip stays put. The undo must turn about the same point, or each new value starts from a model that has drifted.
        // Y, because Karl's centre is a metre above the origin and an undo about the wrong point drifts by half of that.
        [Test]
        public void ScaleAboutTheWorldOrigin_ScalesFromIt_AndEachValueStartsFromTheModelAsItWas()
        {
            SelectMesh();
            var before = Vertex(0);
            _tool.SetMode(TransformMode.Scale);
            _tool.PivotAtOrigin = true;

            _tool.Vector3.Y.TextValue = "2";
            AreClose(Vertex(0), before * new Vector3(1, 2, 1));
            _tool.Vector3.Y.TextValue = "3";
            AreClose(Vertex(0), before * new Vector3(1, 3, 1));

            _commands.Undo();
            AreClose(Vertex(0), before);
        }

        [Test]
        public void ChangingThePivot_ReshapesTheScaleAlreadyTyped()
        {
            SelectMesh();
            var before = Vertex(0);
            _tool.SetMode(TransformMode.Scale);
            _tool.Vector3.Y.TextValue = "2";

            _tool.PivotAtOrigin = true;

            AreClose(Vertex(0), before * new Vector3(1, 2, 1));
            Assert.That(_tool.Vector3.Y.Value, Is.EqualTo(2));
        }

        // The falloff spreads a vertex move over its neighbours; changing it reshapes the move already typed.
        [Test]
        public void ChangingTheFalloff_ReshapesTheMoveAlreadyTyped()
        {
            var state = (VertexSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Vertex, _mesh);
            state.ModifySelection([0], false);
            var before = Enumerable.Range(0, _mesh.Geometry.VertexCount()).Select(Vertex).ToList();
            _tool.SetMode(TransformMode.Translate);
            _tool.Vector3.X.TextValue = "1";
            var moved = Enumerable.Range(1, before.Count - 1).Count(i => Vector3.Distance(Vertex(i), before[i]) > 1e-4);
            Assert.That(moved, Is.EqualTo(0), "with no falloff only the selected vertex moves");

            _tool.VertexMovementFalloff.Value.TextValue = "0.2";

            AreClose(Vertex(0), before[0] + new Vector3(1, 0, 0));
            moved = Enumerable.Range(1, before.Count - 1).Count(i => Vector3.Distance(Vertex(i), before[i]) > 1e-4);
            Assert.That(moved, Is.GreaterThan(0), "the wider falloff moved no neighbour");

            _commands.Undo();
            Assert.That(Enumerable.Range(0, before.Count).Max(i => Vector3.Distance(Vertex(i), before[i])), Is.LessThan(1e-3));
        }
    }
}
