using Editors.KitbasherEditor.ChildEditors.SnapTool;
using Editors.KitbasherEditor.Core;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.SnapTool
{
    // The snap window moves the selection, as one undo step, so the centre of what is drawn lands on a bone or on a target taken
    // from selected vertices. Karl's meshes are first moved off where they load and given pivots, so a move by the bone's position
    // instead of to it, or a centre that leaves the pivot out, lands somewhere else.
    public class SnapToolTests
    {
        AssetEditorTestRunner _runner = null!;
        SnapToolViewModel _snap = null!;
        CommandManager _commands = null!;
        GameSkeleton _skeleton = null!;
        Rmv2MeshNode _a = null!;
        Rmv2MeshNode _b = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);

            var main = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main);
            (_a, _b) = (meshes[0], meshes[1]);
            _a.PivotPoint = new Vector3(0.3f, 0.2f, -0.1f);
            _b.PivotPoint = new Vector3(-0.2f, 0.1f, 0.4f);

            _snap = Service<SnapToolViewModel>();
            _commands = Service<CommandManager>();
            _skeleton = Service<KitbasherRootScene>().Skeleton;
            Assert.That(_skeleton, Is.Not.Null, "Karl opened with no skeleton");

            SelectObjects(_a, _b);
            var away = TransformGizmoWrapper.CreateFromSelectionState(Selection.GetState(), Service<IUiCommandFactory>(), Service<IScopedLogger>())!;
            away.Start(_commands);
            away.GizmoTranslateEvent(new Vector3(1.5f, -0.5f, 2), PivotType.ObjectCenter);
            away.Stop(_commands);
        }

        [TearDown]
        public void Close() => _snap.Dispose();

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        SelectionManager Selection => Service<SelectionManager>();
        void SelectObjects(params ISelectable[] nodes) => ((ObjectSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection(nodes, false);
        static Vector3 Drawn(Rmv2MeshNode mesh) => mesh.Geometry.MeshCenter + mesh.PivotPoint;
        static void AreClose(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-3), $"{actual} is not {expected}");

        // The bone furthest from the origin, so a bone at or near the origin cannot hide a move by the wrong amount.
        (string Name, int Index) FarBone()
        {
            var index = Enumerable.Range(0, _skeleton.BoneCount).MaxBy(i => _skeleton.GetWorldTransform(i).Translation.Length());
            return (_skeleton.GetBoneNameByIndex(index), index);
        }

        // Every bone carried off its bind pose, as an animation frame would carry it.
        void ShowAPose()
        {
            var frame = new AnimationFrame();
            for (var i = 0; i < _skeleton.BoneCount; i++)
                frame.BoneTransforms.Add(new AnimationFrame.BoneKeyFrame { BoneIndex = i, WorldTransform = Matrix.CreateRotationY(0.7f) * Matrix.CreateTranslation(0.7f, 0.3f, -0.4f) });
            _skeleton.SetAnimationFrame(frame);
        }

        [Test]
        public void MoveToBone_PutsTheDrawnCentreOnTheBindPoseBone_EvenWithAnAnimationShown_AndOneUndoTakesItBack()
        {
            var (name, index) = FarBone();
            ShowAPose();
            var bind = _skeleton.GetWorldTransform(index).Translation;
            Assert.That(Vector3.Distance(_skeleton.GetAnimatedWorldTranform(index).Translation, bind), Is.GreaterThan(0.1f), "the pose did not move the bone");
            var before = _a.Geometry.GetVertexById(0);

            _snap.Bones.SelectedItem = name;
            _snap.MoveToBone();

            AreClose((Drawn(_a) + Drawn(_b)) / 2, bind);
            _commands.Undo();
            AreClose(_a.Geometry.GetVertexById(0), before);
            Assert.That(_commands.CanUndo(), Is.True, "the undo took the move made before the snap as well");
        }

        [Test]
        public void TheVertexTarget_IsTheSelectedVerticesAsDrawn_AndTheSelectionMovesOntoIt()
        {
            var vertices = (VertexSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Vertex, _b);
            vertices.ModifySelection([0, 1, 2], false);
            var expected = (_b.Geometry.GetVertexById(0) + _b.Geometry.GetVertexById(1) + _b.Geometry.GetVertexById(2)) / 3 + _b.PivotPoint;

            _snap.UseSelectedVerticesAsTarget();
            Assert.That(_snap.Target, Is.Not.Null);
            AreClose(_snap.Target!.Value, expected);

            SelectObjects(_a);
            var before = _a.Geometry.GetVertexById(0);
            _snap.MoveToTarget();
            AreClose(Drawn(_a), expected);

            _commands.Undo();
            AreClose(_a.Geometry.GetVertexById(0), before);
            Assert.That(_snap.Target, Is.Not.Null, "the target went with the undo");
        }

        [Test]
        public void WithNothingToMoveTo_OrNothingSelected_NothingMoves()
        {
            var before = _a.Geometry.GetVertexById(0);
            var steps = 0;
            Service<IEventHub>().Register<CommandStackChangedEvent>(this, _ => steps++);

            _snap.MoveToBone();
            _snap.MoveToTarget();
            _snap.UseSelectedVerticesAsTarget();
            Assert.That(_snap.Target, Is.Null, "object mode set a vertex target");
            Selection.CreateSelectionSate(GeometrySelectionMode.Vertex, _b);
            _snap.UseSelectedVerticesAsTarget();
            Assert.That(_snap.Target, Is.Null, "vertex mode with no vertex picked set a target");

            Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!);
            _snap.Bones.SelectedItem = FarBone().Name;
            _snap.MoveToBone();

            Assert.That(steps, Is.EqualTo(0));
            AreClose(_a.Geometry.GetVertexById(0), before);
        }

        // The window's content is XAML no other test loads: a bad property or binding path shows only when it opens. Hosted in a
        // plain window, because an AssetEditorWindow needs the app's main window, which belongs to another test's thread.
        [Test, Apartment(ApartmentState.STA)]
        public void TheViewLoads_AndEachButtonReachesItsCommand()
        {
            var view = new SnapToolView { DataContext = _snap };
            var host = new System.Windows.Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = System.Windows.WindowStyle.None, Left = -10000, Width = 380, Height = 600 };
            try
            {
                host.Show();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                var buttons = Descendants(view).OfType<System.Windows.Controls.Button>().ToList();
                Assert.That(buttons, Has.Count.EqualTo(4));
                Assert.That(buttons.Select(x => x.Command), Is.EquivalentTo(new[] { _snap.MoveToBoneCommand, _snap.UseSelectedVerticesAsTargetCommand, _snap.MoveToTargetCommand, _snap.ClearTargetCommand }));
            }
            finally
            {
                host.Close();
            }
        }

        static IEnumerable<object> Descendants(object node) =>
            node is System.Windows.DependencyObject d
                ? System.Windows.LogicalTreeHelper.GetChildren(d).Cast<object>().SelectMany(x => Descendants(x).Prepend(x))
                : [];

        [Test]
        public void TheRotationSnapBox_TurnsTheGizmosSnapOn_AtTheStepTyped()
        {
            var gizmo = Service<GizmoComponent>();
            _snap.RotationSnap = true;
            _snap.RotationSnapStep.TextValue = "15";

            Assert.That(gizmo.RotationSnap, Is.True);
            Assert.That(gizmo.RotationSnapDegrees, Is.EqualTo(15));
        }
    }
}
