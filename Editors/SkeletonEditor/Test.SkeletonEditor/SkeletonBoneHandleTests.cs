using Editor.VisualSkeletonEditor.SkeletonEditor;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.SkeletonEditor
{
    // The skeleton editor's bones could only be moved by typing numbers. A click in the view now picks a bone, and a handle on it
    // moves or turns it. Each test measures the bones where the skeleton node draws them, through the scene object's offset, which
    // flips Z for a tech skeleton, so a handle working in the skeleton's own space is caught there.
    internal class SkeletonBoneHandleTests
    {
        AssetEditorTestRunner _runner = null!;
        SkeletonEditorViewModel _editor = null!;
        GraphicsDevice _device = null!;
        Viewport _viewport;

        [SetUp]
        public void Open()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(@"animations\skeletons\humanoid01.anim"), EditorEnums.VisualSkeletonEditor);
            _editor = Service<SkeletonEditorViewModel>();
            // The test device's back buffer is 20 pixels square, where every bone is within a click of every other.
            _device = Service<IDeviceResolver>().Device;
            _viewport = _device.Viewport;
            _device.Viewport = new Viewport(0, 0, 800, 600);
        }

        [TearDown]
        public void RestoreViewport() => _device.Viewport = _viewport;

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        SceneObject Scene => _editor.SceneObjects.First().Data;
        GameSkeleton Skeleton => Scene.Skeleton;
        int Bone(string name) => Skeleton.GetBoneIndexByName(name);
        void Select(string name) => _editor.SelectedBone = SkeletonToolHelpers.GetSkeletonNodeFromName(name, _editor.Bones);
        Vector3 Drawn(int bone) => Vector3.Transform(Skeleton.GetWorldTransform(bone).Translation, Scene.Offset);

        static string State(GameSkeleton skeleton) => string.Join("\n", Enumerable.Range(0, skeleton.BoneCount).Select(i =>
            $"{skeleton.BoneNames[i]} {skeleton.GetParentBoneIndex(i)} {skeleton.Translation[i]} {skeleton.Rotation[i]} {skeleton.Scale[i]}"));

        static Quaternion Turn(Matrix m)
        {
            m.Decompose(out _, out var turn, out _);
            return turn;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AMoveDrag_PutsTheBoneWhereTheHandleWent_ItsChildrenGoWithIt_AsOneUndoStep(bool techSkeleton)
        {
            _editor.IsTechSkeleton = techSkeleton;
            var loaded = State(Skeleton);
            Select("lowerarm_left");
            var (arm, hand) = (Bone("lowerarm_left"), Bone("hand_left"));
            var (armAt, handAt, armTurn) = (Drawn(arm), Drawn(hand), Turn(Skeleton.GetWorldTransform(arm)));
            var drag = new Vector3(0.1f, -0.2f, 0.3f);

            var handle = _editor.Handle;
            handle.Mode = BoneHandleMode.Move;
            Assert.That(Vector3.Distance(handle.Where!.Value, armAt), Is.LessThan(1e-5f), "the handle stands on the bone as drawn");
            handle.Begin();
            handle.MoveBy(drag * 0.5f);
            handle.MoveBy(drag * 0.5f);
            handle.End();

            Assert.That(Vector3.Distance(Drawn(arm), armAt + drag), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(Drawn(hand), handAt + drag), Is.LessThan(1e-4f), "the hand goes with the arm");
            Assert.That(Math.Abs(Quaternion.Dot(Turn(Skeleton.GetWorldTransform(arm)), armTurn)), Is.GreaterThan(0.99999f), "a move does not turn the bone");
            var shown = _editor.SelectedBoneTranslationOffset;
            var world = Skeleton.GetWorldTransform(arm).Translation;
            Assert.That(Vector3.Distance(new Vector3((float)shown.X.Value, (float)shown.Y.Value, (float)shown.Z.Value), world), Is.LessThan(1e-3f), "the panel shows the new numbers");

            // A click on the handle that moves nothing is no step, so one undo takes back the whole drag.
            handle.Begin();
            handle.End();
            _editor.UndoAction();
            Assert.That(State(Skeleton), Is.EqualTo(loaded));

            // Pressing the lit button again hides the handle.
            handle.Moving = false;
            Assert.That(handle.Where, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ATurnDrag_TurnsTheBoneAboutItsPoint_InTheScenesAxes(bool techSkeleton)
        {
            _editor.IsTechSkeleton = techSkeleton;
            Select("lowerarm_left");
            var (arm, hand) = (Bone("lowerarm_left"), Bone("hand_left"));
            var (armAt, handAt) = (Drawn(arm), Drawn(hand));
            var (first, second) = (Matrix.CreateRotationY(MathHelper.ToRadians(10)), Matrix.CreateRotationX(MathHelper.ToRadians(25)));

            var handle = _editor.Handle;
            handle.Mode = BoneHandleMode.Rotate;
            handle.Begin();
            handle.TurnBy(first);
            handle.TurnBy(second);
            handle.End();

            Assert.That(Vector3.Distance(Drawn(arm), armAt), Is.LessThan(1e-4f), "the bone stays where it is");
            Assert.That(Vector3.Distance(Drawn(hand), armAt + Vector3.Transform(handAt - armAt, first * second)), Is.LessThan(1e-4f),
                "the hand swings about the arm by the drag, as drawn");
        }

        // Framed from the side, where a tech skeleton's flipped Z mirrors it on screen, so a pick made in the skeleton's own space
        // lands on another bone. Picks the bone drawn furthest from any other, so a click on it cannot be nearer another.
        [TestCase(false)]
        [TestCase(true)]
        public void AClickOnABone_SelectsIt_AndTheTreeShowsIt_AMissKeepsTheSelection(bool techSkeleton)
        {
            _editor.IsTechSkeleton = techSkeleton;
            var mouse = Mock.Get(Service<IMouseComponent>());
            mouse.Setup(x => x.IsMouseOwner(It.IsAny<IGameComponent>())).Returns(true);
            var drawn = Enumerable.Range(0, Skeleton.BoneCount).Select(Drawn).ToList();
            var bounds = BoundingBox.CreateFromPoints(drawn);
            var centre = (bounds.Min + bounds.Max) / 2;
            var camera = Service<ArcBallCamera>();
            camera.ViewMatrixOverride = Matrix.CreateLookAt(centre + new Vector3((bounds.Max - bounds.Min).Length() * 1.5f, 0, 0), centre, Vector3.Up);
            var screen = drawn.Select(x => _device.Viewport.Project(x, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity)).Select(x => new Vector2(x.X, x.Y)).ToList();
            var bone = Enumerable.Range(0, screen.Count).OrderByDescending(i => Enumerable.Range(0, screen.Count).Where(j => j != i).Min(j => Vector2.Distance(screen[i], screen[j]))).First();

            void Click(Vector2 at)
            {
                mouse.Setup(x => x.Position()).Returns(at);
                mouse.Setup(x => x.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
                Service<SelectionComponent>().Update(new GameTime());
                mouse.Setup(x => x.IsMouseButtonReleased(MouseButton.Left)).Returns(false);
            }

            Click(screen[bone]);
            Assert.That(_editor.SelectedBone?.BoneIndex, Is.EqualTo(bone), Skeleton.BoneNames[bone]);
            Assert.That(_editor.SelectedBone!.IsSelected, Is.True, "the tree");

            Click(new Vector2(2, 2));
            Assert.That(_editor.SelectedBone?.BoneIndex, Is.EqualTo(bone), "a click on no bone keeps the pick");

            // A hidden skeleton has no handle, and its bones cannot be clicked.
            var root = Enumerable.Range(0, screen.Count).First(i => i != bone);
            _editor.ShowSkeleton = false;
            Assert.That(_editor.Handle.Where, Is.Null);
            Click(screen[root]);
            Assert.That(_editor.SelectedBone?.BoneIndex, Is.EqualTo(bone));
        }
    }
}
