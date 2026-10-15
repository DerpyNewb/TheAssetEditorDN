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
    // Kitbash had undo and no redo. Redo puts an undone step back, including the gizmo's, whose change was made before it went
    // on the stack, and a step that builds a list as it runs (divide, duplicate) undoes cleanly after a redo.
    // STA: divide shows a wait cursor, which throws on any other thread.
    [Apartment(ApartmentState.STA)]
    public class RedoTests
    {
        AssetEditorTestRunner _runner = null!;
        CommandManager _commands = null!;
        Rmv2MeshNode _mesh = null!;
        MainEditableNode _root = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            _root = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            _mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(_root).First();
            _commands = Service<CommandManager>();
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        SelectionManager Selection => Service<SelectionManager>();
        Vector3 Vertex(int id) => _mesh.Geometry.GetVertexById(id);
        ObjectSelectionState SelectMesh()
        {
            var state = (ObjectSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!);
            state.ModifySelection([_mesh], false);
            return state;
        }
        static void AreClose(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-3), $"{actual} is not {expected}");
        int Meshes() => SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(_root).Count;

        [Test]
        public void AGizmoDrag_Redoes()
        {
            SelectMesh();
            var before = Vertex(0);
            var gizmo = TransformGizmoWrapper.CreateFromSelectionState(Selection.GetState(), Service<IUiCommandFactory>(), Service<IScopedLogger>())!;
            gizmo.Start(_commands);
            gizmo.GizmoTranslateEvent(new Vector3(0, 2, 0), PivotType.ObjectCenter);
            gizmo.GizmoRotateEvent(Matrix.CreateRotationY(0.5f), PivotType.ObjectCenter);
            gizmo.Stop(_commands);
            var moved = Vertex(0);
            Assert.That(Vector3.Distance(moved, before), Is.GreaterThan(0.1f), "the drag moved nothing");

            _commands.Undo();
            AreClose(Vertex(0), before);
            Assert.That(_commands.CanRedo(), Is.True);

            _commands.Redo();
            AreClose(Vertex(0), moved);
            Assert.That(_commands.CanRedo(), Is.False);

            _commands.Undo();
            AreClose(Vertex(0), before);
        }

        [Test]
        public void ATypedTransform_Redoes_AndItsPreviewsLeaveNoRedo()
        {
            SelectMesh();
            var before = Vertex(0);
            var tool = Service<TransformToolViewModel>();
            tool.SetMode(TransformMode.Translate);
            tool.Vector3.X.TextValue = "1";
            tool.Vector3.X.TextValue = "3";
            Assert.That(_commands.CanRedo(), Is.False, "the preview the 3 replaced is a step to redo");

            _commands.Undo();
            _commands.Redo();
            AreClose(Vertex(0), before + new Vector3(3, 0, 0));
        }

        [Test]
        public void ANewStep_DropsTheRedo()
        {
            var state = SelectMesh();
            Service<ObjectEditor>().DuplicateObject(state);
            _commands.Undo();
            Assert.That(_commands.CanRedo(), Is.True);

            Service<ObjectEditor>().DuplicateObject(SelectMesh());
            Assert.That(_commands.CanRedo(), Is.False);
        }

        [Test]
        public void DivideAndDuplicate_UndoCleanly_AfterARedo()
        {
            var meshes = Meshes();

            // The first of Karl's meshes that is more than one piece.
            var divided = meshes;
            foreach (var mesh in SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(_root).ToList())
            {
                _mesh = mesh;
                Service<ObjectEditor>().DivideIntoSubmeshes(SelectMesh(), true);
                divided = Meshes();
                if (divided > meshes)
                    break;
                _commands.Undo(keepForRedo: false);
            }
            Assert.That(divided, Is.GreaterThan(meshes), "no mesh divided");
            _commands.Undo();
            _commands.Redo();
            Assert.That(Meshes(), Is.EqualTo(divided));
            _commands.Undo();
            Assert.That(Meshes(), Is.EqualTo(meshes));

            Service<ObjectEditor>().DuplicateObject(SelectMesh());
            _commands.Undo();
            _commands.Redo();
            Assert.That(Meshes(), Is.EqualTo(meshes + 1));
            _commands.Undo();
            Assert.That(Meshes(), Is.EqualTo(meshes));
        }
    }
}
