using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Ui.Common.Behaviors;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // SuperView's handle on a position tag: which tags get one, how a move in the scene is written into the tag's own space,
    // and drags through the real Gizmo widget in an opened SuperView, with the meta panel's boxes, undo and unsaved mark.
    public class MetaTagGizmoTests
    {
        static void AreClose(Vector3 actual, Vector3 expected, float within = 1e-4f) =>
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(within), $"{actual} is not {expected}");

        static MetaDataEntry Entry(ParsedMetadataAttribute tag, IEventHub? hub = null) => new(tag, "", hub ?? new Mock<IEventHub>().Object, true);

        static IEnumerable<TestCaseData> PositionTags()
        {
            string[] position = ["Position"];
            yield return new TestCaseData(new FirePos_v10(), position, -1, null).SetName("FIRE_POS");
            yield return new TestCaseData(new ImpactPosition_v10(), position, -1, null).SetName("IMPACT_POS");
            yield return new TestCaseData(new TargetPos_10(), position, -1, null).SetName("TARGET_POS");
            yield return new TestCaseData(new CameraShakePos(), position, -1, null).SetName("CAMERA_SHAKE_POS");
            yield return new TestCaseData(new CrewLocation_v10(), position, -1, null).SetName("CREW_LOCATION_10");
            yield return new TestCaseData(new CrewLocation_v2(), position, -1, null).SetName("CREW_LOCATION_2");
            yield return new TestCaseData(new SplashAttack_v10(), new[] { "StartPosition", "EndPosition" }, -1, null).SetName("SPLASH_ATTACK");
            yield return new TestCaseData(new SoundTrigger_v10 { BoneIndex = 7 }, position, 7, null).SetName("SOUND_TRIGGER_10");
            yield return new TestCaseData(new SoundTrigger_v4 { BoneIndex = 3 }, position, 3, null).SetName("SOUND_TRIGGER_4");
            yield return new TestCaseData(new Effect_v11 { NodeIndex = 5, Tracking = true, StartTime = 0.4f }, position, 5, null).SetName("EFFECT_tracked");
            yield return new TestCaseData(new Effect_v12 { NodeIndex = 5, Tracking = false, StartTime = 0.4f }, position, 5, 0.4f).SetName("EFFECT_untracked");
        }

        // The bone each field is read from, and when: an untracked EFFECT is drawn where its bone was at its Start Time.
        [TestCaseSource(nameof(PositionTags))]
        public void APositionTag_GetsAHandlePerField_OnWhatItIsMeasuredFrom(ParsedMetadataAttribute tag, string[] fields, int bone, float? at)
        {
            var asked = new List<(int, float?)>();
            var handles = PositionHandle.Of(Entry(tag), (b, t) => { asked.Add((b, t)); return Matrix.Identity; });
            foreach (var handle in handles)
                _ = handle.World;

            Assert.That(handles.Select(x => x.Property), Is.EqualTo(fields));
            Assert.That(asked, Has.Count.EqualTo(fields.Length).And.All.EqualTo((bone, at)));
        }

        [Test]
        public void OtherTags_AndNoTag_GetNoHandle()
        {
            Assert.That(PositionHandle.Of(Entry(new DisableModel()), (_, _) => Matrix.Identity), Is.Empty);
            Assert.That(PositionHandle.Of(Entry(new Blood_v12()), (_, _) => Matrix.Identity), Is.Empty);
            Assert.That(PositionHandle.Of(null, (_, _) => Matrix.Identity), Is.Empty);
        }

        // The bone is turned a quarter turn about Y, scaled twice and moved, so a step along the scene's X is a step half as long
        // along the bone's own Z. Written as it is in the scene, the half-metre step along X would move the handle a metre along -Z.
        [Test]
        public void AMoveInTheScene_IsWrittenInTheBonesOwnSpace_AsOneEdit()
        {
            var hub = new Mock<IEventHub>();
            var tag = new SoundTrigger_v10 { BoneIndex = 2, Position = new Vector3(0.1f, 0.2f, 0.3f) };
            var entry = Entry(tag, hub.Object);
            var frame = Matrix.CreateScale(2) * Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(1, 2, 3);
            var handle = PositionHandle.Of(entry, (_, _) => frame).Single();
            var before = handle.World;

            handle.MoveBy(new Vector3(0.5f, 0, 0));

            AreClose(handle.World, before + new Vector3(0.5f, 0, 0));
            AreClose(tag.Position, new Vector3(0.1f, 0.2f, 0.55f));
            AreClose(entry.Variables.OfType<VectorAttributeViewModel>().Single().Value.GetAsVector3(), new Vector3(0.1f, 0.2f, 0.55f));
            Assert.That(entry.Variables.OfType<VectorAttributeViewModel>().Single().IsModified, Is.True, "the row is not marked edited");
            // Box by box, the Y and Z boxes would each say so as well, and SuperView redraws on every one.
            hub.Verify(x => x.Publish(It.IsAny<MetaDataAttributeChangedEvent>()), Times.Once);
        }

        // CA hides a prop by scaling its bone to nothing; a frame with no inverse would write NaN into the file.
        [Test]
        public void ABoneScaledToNothing_LeavesThePositionAsItWas()
        {
            var tag = new SoundTrigger_v10 { BoneIndex = 2, Position = new Vector3(0.1f, 0.2f, 0.3f) };
            var handle = PositionHandle.Of(Entry(tag), (_, _) => Matrix.CreateScale(0)).Single();

            handle.MoveBy(new Vector3(0.5f, 0, 0));

            Assert.That(tag.Position, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
        }

        // SuperView's frame: a bone read from the clip at the time asked, or at the player's time, under the asset's offset.
        [Test]
        public void FrameOf_ReadsTheBoneFromTheClip_AtTheTimeAskedOrNow_UnderTheAssetsOffset()
        {
            var runner = new AssetEditorTestRunner();
            using var builder = new MetaDataBuilder(null!, null!, null!, runner.PackFileService, null!);
            var player = new AnimationPlayer();
            var skeleton = new GameSkeleton(TwoBones(), player);
            // The second bone walks 2 m/s along X, 1 m up, in 21 frames over 2 s.
            var clip = new AnimationClip { PlayTimeInSec = 2 };
            for (var frame = 0; frame <= 20; frame++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame { Position = [Vector3.Zero, new Vector3(frame * 0.2f, 1, 0)], Rotation = [Quaternion.Identity, Quaternion.Identity], Scale = [Vector3.One, Vector3.One] });
            player.SetAnimation(clip, skeleton);
            var root = new GroupNode("root") { ModelMatrix = Matrix.CreateTranslation(0, 0, 5) };
            var scene = new SceneObject("asset") { ParentNode = root, ModelNode = root.AddObject(new GroupNode("model")), Skeleton = skeleton, Player = player };
            player.SetTime(1_500_000);

            var frameOf = MetaTagGizmoComponent.FrameOf(builder, scene);

            AreClose(frameOf(1, 0.5f).Translation, new Vector3(1, 1, 5));
            AreClose(frameOf(1, null).Translation, new Vector3(3, 1, 5));
            AreClose(frameOf(-1, null).Translation, new Vector3(0, 0, 5));
        }

        static AnimationFile TwoBones()
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones =
            [
                new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = AnimationFile.BoneIndexNoParent },
                new AnimationFile.BoneInfo { Id = 1, Name = "fx", ParentId = 0 },
            ];
            var bind = new AnimationFile.Frame();
            bind.Transforms.Add(new RmvVector3(0, 0, 0));
            bind.Transforms.Add(new RmvVector3(0, 1, 0));
            bind.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            bind.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(bind);
            file.AnimationParts.Add(part);
            return file;
        }

        // ---- Drags through the real widget, in SuperView opened on Throt's ATTACK_5 ----

        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";
        const string ThrotTable = @"animations/database/battle/bin/hu17_dlc16_throt.bin";

        AssetEditorTestRunner _runner = null!;
        SuperViewViewModel _superView = null!;
        // The editor's scope owns it, and disposes of it.
        MetaTagGizmoComponent Gizmo => Service<MetaTagGizmoComponent>();
        Mock<IMouseComponent> _mouse = null!;

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        MetaDataEditorViewModel Editor => _superView.MetaEditor;
        ParsedMetadataAttribute LastTag => Editor.ParsedFile!.Attributes.Last();

        void OpenSuperView()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            _superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(_runner.PackFileService.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = _superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == ThrotTable);
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            _mouse = Mock.Get(Service<IMouseComponent>());
            _mouse.SetupProperty(x => x.MouseOwner);
            _mouse.Setup(x => x.IsMouseOwner(It.IsAny<IGameComponent>())).Returns<IGameComponent>(x => _mouse.Object.MouseOwner is null || _mouse.Object.MouseOwner == x);
        }

        // As the New button adds a tag: appended, the rows rebuilt and the new row picked. Then saved, so a drag is what marks it.
        T AddTag<T>(string key, Action<T> set) where T : ParsedMetadataAttribute
        {
            var tag = (T)_runner.ServiceProvider.GetRequiredService<MetaDataFileParser>().CreateDefault(key);
            set(tag);
            Editor.ParsedFile!.Attributes.Add(tag);
            Editor.UpdateView();
            Editor.SelectedTag = Editor.Tags.Last();
            Editor.HasUnsavedChanges = false;
            return tag;
        }

        void Frame(Vector2 at, bool down, bool wasDown)
        {
            static MouseState State(Vector2 at, bool down) => new((int)at.X, (int)at.Y, 0, down ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            _mouse.Setup(x => x.Position()).Returns(at);
            _mouse.Setup(x => x.IsMouseButtonDown(MouseButton.Left)).Returns(down);
            _mouse.Setup(x => x.State()).Returns(State(at, down));
            _mouse.Setup(x => x.LastState()).Returns(State(at, wasDown));
            Gizmo.Update(new GameTime());
        }

        Vector2 OnScreen(Vector3 world)
        {
            var camera = Service<ArcBallCamera>();
            var point = Service<IDeviceResolver>().Device.Viewport.Project(world, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
            return new Vector2(point.X, point.Y);
        }

        // Where the mouse ray meets the plane through z, the plane a drag along X slides on.
        Vector3 OnPlaneZ(Vector2 mouse, float z)
        {
            var ray = Service<ArcBallCamera>().CreateCameraRay(mouse);
            return ray.Position + ray.Direction * ((z - ray.Position.Z) / ray.Direction.Z);
        }

        Vector2 _mouseAt;
        Vector2 _pressedAt;
        float _planeZ;

        // Rests the mouse on the handle's X arrow tip and presses. The widget draws its X arrow 3 units long at a twenty-fifth of
        // the camera's distance and takes the tip within one such unit; a first frame away from it lets the widget size itself.
        void Press(int handle)
        {
            Frame(new Vector2(-1000), false, false);
            var at = Gizmo.Handles[handle].World;
            var unit = Vector3.Distance(Service<ArcBallCamera>().Position, at) / 25;
            _mouseAt = _pressedAt = OnScreen(at + new Vector3(3 * unit, 0, 0));
            _planeZ = at.Z;
            Frame(_mouseAt, false, false);
            Frame(_mouseAt, true, false);
        }

        void Move(float pixels)
        {
            _mouseAt += new Vector2(pixels, 0);
            Frame(_mouseAt, true, true);
        }

        // Lets go, and says how far along X the camera's rays put the drag on the plane it slid on.
        float Release()
        {
            Frame(_mouseAt, false, true);
            return OnPlaneZ(_mouseAt, _planeZ).X - OnPlaneZ(_pressedAt, _planeZ).X;
        }

        float DragX(int handle, params float[] steps)
        {
            Press(handle);
            foreach (var step in steps)
                Move(step);
            return Release();
        }

        static VectorAttributeViewModel Box(MetaDataEditorViewModel editor) => editor.SelectedTag!.Variables.OfType<VectorAttributeViewModel>().Single();

        // Every step of the drag redraws SuperView, and the handle carries on from where each step left it.
        [Test]
        public void DraggingAFirePos_MovesItAlongX_AndMarksTheFileUnsaved()
        {
            OpenSuperView();
            var origin = new Vector3(0.3f, 1, 0.2f);
            AddTag<FirePos_v10>("FIRE_POS_10", x => x.Position = origin);

            Press(0);
            Assert.That(_mouse.Object.MouseOwner, Is.SameAs(Gizmo), "the press is left to the scene's selection");
            foreach (var step in new[] { 0.2f, 0.2f, 0.2f })
                Move(step);
            var moved = Release();

            var position = ((FirePos_v10)LastTag).Position;
            Assert.That(Math.Abs(moved), Is.GreaterThan(0.05f), "the drag went nowhere");
            AreClose(position, origin + new Vector3(moved, 0, 0), 1e-3f);
            AreClose(Box(Editor).Value.GetAsVector3(), position);
            Assert.That((Editor.HasUnsavedChanges, _superView.HasUnsavedChanges), Is.EqualTo((true, true)));
            Assert.That((ScrubGesture.IsActive, _mouse.Object.MouseOwner), Is.EqualTo((false, (IGameComponent?)null)), "the drag did not end");
        }

        // A number typed into the tag a moment before is a step of its own: out of a drag, edits to one tag under a second apart
        // are one step, and the drag is a gesture of its own.
        [Test]
        public void ADrag_IsOneUndoStep_ApartFromATypedEditJustBeforeIt()
        {
            OpenSuperView();
            AddTag<FirePos_v10>("FIRE_POS_10", x => x.Position = new Vector3(0.3f, 1, 0.2f));
            Box(Editor).Value.Y.TextValue = "1.5";
            var typed = new Vector3(0.3f, 1.5f, 0.2f);

            DragX(0, 0.2f, 0.2f, 0.2f);

            Editor.UndoCommand.Execute(null);
            Assert.That(((FirePos_v10)LastTag).Position, Is.EqualTo(typed));
            Editor.UndoCommand.Execute(null);
            Assert.That(((FirePos_v10)LastTag).Position, Is.EqualTo(new Vector3(0.3f, 1, 0.2f)));
        }

        // An undo, or a click in the tag list, mid-drag: the drag ends there, and the rest of it moves nothing.
        [Test]
        public void PickingAnotherTagMidDrag_EndsTheDrag()
        {
            OpenSuperView();
            var at = new Vector3(0.3f, 1, 0.2f);
            AddTag<FirePos_v10>("FIRE_POS_10", x => x.Position = at);
            AddTag<FirePos_v10>("FIRE_POS_10", x => x.Position = at);
            Press(0);
            Move(0.2f);

            Editor.SelectedTag = Editor.Tags[^2];
            Gizmo.Update(new GameTime());
            Assert.That((ScrubGesture.IsActive, _mouse.Object.MouseOwner), Is.EqualTo((false, (IGameComponent?)null)));
            Move(0.2f);
            Release();

            Assert.That(((FirePos_v10)Editor.ParsedFile!.Attributes[^2]).Position, Is.EqualTo(at));
        }

        // The drawn marker rides a bone that is turned at the clip's first frame; the handle stands on the marker, a drag along
        // the scene's X moves the marker along X, and the field takes the move in the bone's own space.
        [Test]
        public void DraggingASoundTriggerOnATurnedBone_MovesItAlongTheScenesX()
        {
            OpenSuperView();
            var data = _superView.SceneObjects[0].Data;
            var frameOf = MetaTagGizmoComponent.FrameOf(Service<IMetaDataBuilder>(), data);
            var bone = Enumerable.Range(0, data.Skeleton.BoneCount).First(x => Vector3.Distance(frameOf(x, null).Right, Vector3.UnitX) > 0.5f
                && Vector3.Distance(frameOf(x, null).Translation, Vector3.Zero) < 2);
            var local = new Vector3(0.1f, 0.2f, 0.3f);
            AddTag<SoundTrigger_v10>("SOUND_TRIGGER_10", x => (x.BoneIndex, x.Position, x.SoundEvent) = (bone, local, "gizmo_test"));
            Gizmo.Update(new GameTime());
            var marker = data.MainNode.Children.OfType<SceneNode>().Single(x => x.Name == "Sound:gizmo_test");
            var before = Gizmo.Handles[0].World;
            AreClose(before, Vector3.Transform(local, marker.ModelMatrix * data.MainNode.ModelMatrix));

            var moved = DragX(0, 0.2f, 0.2f);

            var after = Vector3.Transform(((SoundTrigger_v10)LastTag).Position, frameOf(bone, null));
            Assert.That(Math.Abs(moved), Is.GreaterThan(0.05f), "the drag went nowhere");
            AreClose(after, before + new Vector3(moved, 0, 0), 1e-3f);
            Assert.That(Vector3.Distance(((SoundTrigger_v10)LastTag).Position, local + new Vector3(moved, 0, 0)), Is.GreaterThan(0.01f), "written as if the bone were not turned");
        }

        // SPLASH_ATTACK has a handle on each end; dragging the end's moves only the end.
        [Test]
        public void DraggingASplashAttacksEnd_MovesOnlyTheEnd()
        {
            OpenSuperView();
            var (start, end) = (new Vector3(-1, 1, 0), new Vector3(1, 1, 0));
            AddTag<SplashAttack_v10>("SPLASH_ATTACK_10", x => (x.StartPosition, x.EndPosition) = (start, end));

            var moved = DragX(1, 0.2f, 0.2f);

            var tag = (SplashAttack_v10)LastTag;
            Assert.That(tag.StartPosition, Is.EqualTo(start));
            AreClose(tag.EndPosition, end + new Vector3(moved, 0, 0), 1e-3f);
        }

        // Two ends on one point: both widgets light up under the mouse, and only the one pressed first takes the drag.
        [Test]
        public void TwoHandlesOnOnePoint_OnlyThePressedFirstMoves()
        {
            OpenSuperView();
            var at = new Vector3(0, 1, 0);
            AddTag<SplashAttack_v10>("SPLASH_ATTACK_10", x => (x.StartPosition, x.EndPosition) = (at, at));

            var moved = DragX(0, 0.2f, 0.2f);

            var tag = (SplashAttack_v10)LastTag;
            AreClose(tag.StartPosition, at + new Vector3(moved, 0, 0), 1e-3f);
            Assert.That(tag.EndPosition, Is.EqualTo(at));
        }

        // Alt and a drag turns the camera, as everywhere in the 3D views, even with the mouse on a handle.
        [Test]
        public void HoldingAlt_LeavesTheTagWhereItIs()
        {
            OpenSuperView();
            var at = new Vector3(0.3f, 1, 0.2f);
            AddTag<FirePos_v10>("FIRE_POS_10", x => x.Position = at);
            Mock.Get(Service<IKeyboardComponent>()).Setup(x => x.IsKeyDown(Keys.LeftAlt)).Returns(true);

            DragX(0, 0.2f, 0.2f);

            Assert.That(((FirePos_v10)LastTag).Position, Is.EqualTo(at));
        }

        // The camera holds the mouse through a middle-button drag; a left press on a handle then is the camera's.
        [Test]
        public void APressWhileTheCameraHoldsTheMouse_LeavesTheTagWhereItIs()
        {
            OpenSuperView();
            var at = new Vector3(0.3f, 1, 0.2f);
            AddTag<FirePos_v10>("FIRE_POS_10", x => x.Position = at);
            var camera = Service<ArcBallCamera>();
            _mouse.Object.MouseOwner = camera;

            DragX(0, 0.2f, 0.2f);

            Assert.That((((FirePos_v10)LastTag).Position, _mouse.Object.MouseOwner), Is.EqualTo((at, (IGameComponent)camera)));
        }

        [Test]
        public void ATagWithNoPosition_ShowsNoHandle()
        {
            OpenSuperView();
            AddTag<DisableModel>("DISABLE_MODEL_10", _ => { });
            Gizmo.Update(new GameTime());
            Assert.That(Gizmo.Handles, Is.Empty);
        }
    }
}
