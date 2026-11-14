using System.Windows;
using System.Windows.Controls;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using System.Windows.Threading;
using Editors.KitbasherEditor.ChildEditors.PinTool;
using Editors.KitbasherEditor.ChildEditors.SnapTool;
using Editors.KitbasherEditor.Events;
using Editors.KitbasherEditor.Services;
using Editors.KitbasherEditor.ViewModels;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using KitbasherEditor.Views.EditorViews;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.KitbashEditor.BonePicking
{
    // The picking maths, with a camera made up for the test. Screen positions come from MonoGame's own Viewport.Project, not from
    // the code under test.
    public class BonePickerTests
    {
        const int Width = 800, Height = 600;
        static readonly Matrix View = Matrix.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.Up);
        static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, Width / (float)Height, 0.1f, 100);

        // 0 is the root at the origin. 1 is a second root far behind 2, drawn under a pixel above it, which is off the lines from 2
        // to 0 and to 3. 2 is above the root, 3 to the right of 2, and 4 behind the camera.
        static readonly Vector3[] Joints = [Vector3.Zero, new(0, 4.022f, -10), new(0, 2, 0), new(2, 2, 0), new(0, 0, 20)];
        static readonly int[] Parents = [-1, -1, 0, 2, 0];

        static Vector2 OnScreen(Vector3 point)
        {
            var projected = new Viewport(0, 0, Width, Height).Project(point, Projection, View, Matrix.Identity);
            return new Vector2(projected.X, projected.Y);
        }

        static int Pick(Vector2 click) => BonePicker.Nearest(click, View * Projection, Width, Height, Joints, Parents, BonePicker.MaxPixels);

        [Test]
        public void AClickOnABone_PicksIt()
        {
            Assert.That(Pick(OnScreen(Joints[3])), Is.EqualTo(3));
            Assert.That(Pick(OnScreen(Joints[2]) + new Vector2(0, -8)), Is.EqualTo(2));
        }

        [Test]
        public void AClickOnTheLineToAParent_PicksTheNearerEnd()
        {
            var (from, to) = (OnScreen(Joints[2]), OnScreen(Joints[3]));
            Assert.That(Vector2.Distance(from, to), Is.GreaterThan(4 * BonePicker.MaxPixels), "the line is too short to tell its halves apart");

            Assert.That(Pick(Vector2.Lerp(from, to, 0.3f)), Is.EqualTo(2));
            Assert.That(Pick(Vector2.Lerp(from, to, 0.7f)), Is.EqualTo(3));
            Assert.That(Pick(Vector2.Lerp(from, to, 0.3f) + new Vector2(0, 10)), Is.EqualTo(2), "10 pixels off the line");
        }

        [Test]
        public void AClickFurtherThanMaxPixelsFromEveryBone_PicksNothing()
        {
            var tip = OnScreen(Joints[3]);

            Assert.That(Pick(tip + new Vector2(BonePicker.MaxPixels - 1, 0)), Is.EqualTo(3));
            Assert.That(Pick(tip + new Vector2(BonePicker.MaxPixels + 1, 0)), Is.EqualTo(-1));
        }

        // Seen from the side, a left and a right leg draw one over the other. The far bone here comes first in the skeleton and the
        // click is right on it, so taking the first bone found, or the one nearest the click, would pick it.
        [Test]
        public void OfTwoBonesDrawnWithinAPixel_TheOneNearerTheCameraWins()
        {
            Assert.That(Vector2.Distance(OnScreen(Joints[1]), OnScreen(Joints[2])), Is.InRange(0.5f, 1f));

            Assert.That(Pick(OnScreen(Joints[1])), Is.EqualTo(2));
        }

        // Projected without the check, the bone behind the camera lands on the root, and nearer the camera than it.
        [Test]
        public void ABoneBehindTheCamera_IsNeverPicked()
        {
            var clip = Vector4.Transform(new Vector4(Joints[4], 1), View * Projection);
            var unguarded = new Vector2((clip.X / clip.W + 1) * 0.5f * Width, (1 - clip.Y / clip.W) * 0.5f * Height);
            Assert.That(Vector2.Distance(unguarded, OnScreen(Joints[0])), Is.LessThan(1), "the probe is not in the case");

            Assert.That(Pick(OnScreen(Joints[0])), Is.EqualTo(0));
        }
    }

    // Pick mode in a Kitbash editor, through the real selection component: the click is given to the mouse the test runner mocks,
    // below WPF's input, which ignores synthetic mouse moves.
    public class BonePickingInKitbashTests
    {
        AssetEditorTestRunner _runner = null!;
        MainEditableNode _karl = null!;
        Rmv2MeshNode _mesh = null!;
        Mock<IMouseComponent> _mouse = null!;
        GraphicsDevice _device = null!;
        Viewport _viewport;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            _karl = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            _mesh = _karl.GetMeshesInLod(0, true)[0];
            Assert.That(_karl.SkeletonNode.Skeleton, Is.Not.Null, "Karl opened with no skeleton");

            _mouse = Mock.Get(Service<IMouseComponent>());
            _mouse.Setup(x => x.IsMouseOwner(It.IsAny<IGameComponent>())).Returns(true);

            // The test device's back buffer is 20 pixels square, where every bone is within a click of every other.
            _device = Service<IDeviceResolver>().Device;
            _viewport = _device.Viewport;
            _device.Viewport = new Viewport(0, 0, 800, 600);

            // Framed on the skeleton from the front, whatever its size.
            var bounds = BoundingBox.CreateFromPoints(Enumerable.Range(0, Skeleton.BoneCount).Select(i => Skeleton.GetAnimatedWorldTranform(i).Translation));
            var centre = (bounds.Min + bounds.Max) / 2;
            Service<ArcBallCamera>().ViewMatrixOverride = Matrix.CreateLookAt(centre + new Vector3(0, 0, (bounds.Max - bounds.Min).Length() * 1.5f), centre, Vector3.Up);
        }

        [TearDown]
        public void RestoreViewport() => _device.Viewport = _viewport;

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        GameWorld.Core.Animation.GameSkeleton Skeleton => _karl.SkeletonNode.Skeleton!;
        SelectionManager Selection => Service<SelectionManager>();

        void SelectMesh() => ((ObjectSelectionState)Selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([_mesh], false);

        void Click(Vector2 at)
        {
            _mouse.Setup(x => x.Position()).Returns(at);
            _mouse.Setup(x => x.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            Service<SelectionComponent>().Update(new GameTime());
            _mouse.Setup(x => x.IsMouseButtonReleased(MouseButton.Left)).Returns(false);
        }

        // Bones by how far they are drawn from every other bone, loneliest first, so a click on one cannot be nearer another.
        List<(int Index, Vector2 At)> LoneBones()
        {
            var camera = Service<ArcBallCamera>();
            var viewport = Service<IDeviceResolver>().Device.Viewport;
            var screen = Enumerable.Range(0, Skeleton.BoneCount)
                .Select(i => viewport.Project(Skeleton.GetAnimatedWorldTranform(i).Translation, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity))
                .Select(x => new Vector2(x.X, x.Y))
                .ToList();
            return Enumerable.Range(0, screen.Count)
                .OrderByDescending(i => Enumerable.Range(0, screen.Count).Where(j => j != i).Min(j => Vector2.Distance(screen[i], screen[j])))
                .Select(i => (i, screen[i]))
                .ToList();
        }

        [Test]
        public void AClickInPickMode_PicksTheBone_ForTheHeatmapThePinToolTheSnapWindowAndTheSkeletonPanel_AndSelectsNothing()
        {
            var snap = Service<SnapToolViewModel>();
            var pinTool = Service<PinToolViewModel>();
            var panel = Service<SkeletonSceneNodeViewModel>();
            panel.Initialize(_karl.SkeletonNode);
            panel.ShowWeights = true;
            // Lists filtered to names the bone does not have.
            snap.Bones.Filter = "no such bone";
            pinTool.BoneMode.Bones.Filter = "no such bone";
            SelectMesh();
            var selection = Selection.GetState();
            var (bone, at) = LoneBones()[0];
            Assert.That(bone, Is.Not.EqualTo(0), "the root is where every list starts");

            Service<BonePicker>().IsActive = true;
            Click(at);

            Assert.That(_karl.SkeletonNode.SelectedBoneIndex, Is.EqualTo(bone), "the skeleton does not draw the pick highlighted");
            Assert.That(_karl.SkeletonNode.WeightViewBone, Is.EqualTo(bone), "the heatmap");
            Assert.That(panel.SelectedBone?.BoneIndex, Is.EqualTo(bone), "the Skeleton panel");
            Assert.That(panel.SelectedBone!.IsSelected, Is.True, "the Skeleton panel's tree");
            Assert.That(pinTool.BoneMode.Bones.SelectedItem?.Index, Is.EqualTo(bone), "the Pin tool's Bone mode");
            Assert.That(pinTool.BoneMode.Bones.Values, Does.Contain(pinTool.BoneMode.Bones.SelectedItem), "the Pin tool's list hides it");
            Assert.That(snap.Bones.SelectedItem, Is.EqualTo(Skeleton.BoneNames[bone]), "the Snap window");
            Assert.That(snap.Bones.Values, Does.Contain(snap.Bones.SelectedItem), "the Snap window's list hides it");
            Assert.That(Selection.GetState(), Is.SameAs(selection));
            Assert.That(selection.SelectedObjects(), Is.EqualTo(new[] { _mesh }), "the pick changed the mesh selection");

            // A closed Pin tool, Snap window or panel stops following.
            pinTool.Dispose();
            snap.Dispose();
            panel.Dispose();
            var (other, otherAt) = LoneBones()[1];
            Click(otherAt);
            Assert.That(_karl.SkeletonNode.SelectedBoneIndex, Is.EqualTo(other));
            Assert.That((pinTool.BoneMode.Bones.SelectedItem?.Index, snap.Bones.SelectedItem, panel.SelectedBone?.BoneIndex),
                Is.EqualTo((bone, Skeleton.BoneNames[bone], bone)));
        }

        [Test]
        public void WithPickModeOff_AClickSelects_AndWithItOn_AMissOrAHiddenSkeletonSelectsAndPicksNothing()
        {
            var corner = new Vector2(1, 1);
            SelectMesh();
            Click(corner);
            Assert.That(Selection.GetState().SelectionCount(), Is.EqualTo(0), "with pick mode off, a click on nothing clears the selection");

            SelectMesh();
            Service<BonePicker>().IsActive = true;
            Click(corner);
            Assert.That(Selection.GetState().SelectedObjects(), Is.EqualTo(new[] { _mesh }));
            Assert.That(_karl.SkeletonNode.SelectedBoneIndex, Is.Null);

            _karl.SkeletonNode.IsVisible = false;
            Click(LoneBones()[0].At);
            Assert.That(_karl.SkeletonNode.SelectedBoneIndex, Is.Null, "a hidden skeleton was picked from");
            Assert.That(Selection.GetState().SelectedObjects(), Is.EqualTo(new[] { _mesh }));
        }

        // A skeleton is drawn in the pose of the animation shown, which can be far from the pose it binds in.
        [Test]
        public void APosedSkeleton_IsPickedWhereItIsDrawn()
        {
            var frame = new GameWorld.Core.Animation.AnimationFrame();
            for (var i = 0; i < Skeleton.BoneCount; i++)
                frame.BoneTransforms.Add(new() { BoneIndex = i, ParentBoneIndex = Skeleton.GetParentBoneIndex(i), WorldTransform = Matrix.CreateTranslation(0.5f, 0, 0) });
            Skeleton.SetAnimationFrame(frame);
            var (bone, at) = LoneBones()[0];

            Service<BonePicker>().IsActive = true;
            Click(at);

            Assert.That(_karl.SkeletonNode.SelectedBoneIndex, Is.EqualTo(bone));
        }

        // The panel the scene explorer shows for the skeleton is made while the event hub is calling its selection handler, and is
        // dropped when the selection goes.
        [Test]
        public void TheSkeletonPanelTheSceneExplorerShows_FollowsPicks_UntilTheSkeletonIsDeselected()
        {
            var hub = Service<IEventHub>();
            var editor = Service<KitbasherViewModel>().SceneNodeEditor;
            hub.Publish(new SceneNodeSelectedEvent([_karl.SkeletonNode]));
            var panel = editor.CurrentEditor as SkeletonSceneNodeViewModel;
            Assert.That(panel, Is.Not.Null);
            Service<BonePicker>().IsActive = true;
            var bones = LoneBones();

            Click(bones[0].At);
            Assert.That(panel!.SelectedBone?.BoneIndex, Is.EqualTo(bones[0].Index));

            hub.Publish(new SceneNodeSelectedEvent([]));
            Click(bones[1].At);
            Assert.That(panel.SelectedBone?.BoneIndex, Is.EqualTo(bones[0].Index), "the panel left behind still follows picks");
        }

        // The tree's selection behaviour passes a click in the tree to the view model and nothing back, so a pick reaches the tree
        // through the bone's IsSelected.
        [Test, Apartment(ApartmentState.STA)]
        public void TheSkeletonPanelsTree_ShowsAPickedBoneSelected()
        {
            AppResources.Load();
            var panel = Service<SkeletonSceneNodeViewModel>();
            panel.Initialize(_karl.SkeletonNode);
            var view = new SkeletonView { DataContext = panel };
            var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 400, Height = 3000 };
            try
            {
                host.Show();
                Pump();
                var (bone, at) = LoneBones()[0];
                Service<BonePicker>().IsActive = true;
                Click(at);
                Pump();

                var selected = Descendants(view).OfType<TreeViewItem>().Where(x => x.IsSelected).Select(x => ((SkeletonBoneNode)x.DataContext).BoneIndex);
                Assert.That(selected, Is.EqualTo(new[] { bone }));
            }
            finally
            {
                host.Close();
            }
        }

        static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent) =>
            Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(parent)).Select(i => VisualTreeHelper.GetChild(parent, i)).SelectMany(x => Descendants(x).Prepend(x));
    }
}
