using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using GameWorld.Core.Commands.Bone;
using Editors.Shared.Core.Common;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // The KeyFrame tool was switched off in the Tools menu and its scene setup commented out
    // (upstream bbf405d, 95f6c75, df81d04). This opens it the way the menu does and drives it.
    internal class KeyframeEditor_Tests
    {
        const string KarlMesh = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";
        const string IdleAnimation = @"animations\battle\humanoid01\2handed_hammer\stand\hu1_2hh_stand_idle_01.anim";

        // The editor opens with a rider, a mount and a "new anim" object that have no animation yet, and every frame failed on
        // them: SceneObject.Update read the player's clock, which was null until an animation was set. A test never draws a
        // frame, so this runs the update a frame makes.
        [Test]
        public void Warhammer3_TheFirstFrame_DrawsWithNothingLoaded()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);

            var objects = ((WpfGameMock)runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.WpfWindow.IWpfGame>()).Components.OfType<SceneObject>().ToList();
            Assert.That(objects.Select(x => x.Description), Is.EqualTo(new[] { "Rider", "Mount", "New Anim" }));
            Assert.That(() => objects.ForEach(x => x.Update(new GameTime())), Throws.Nothing);
        }

        [Test]
        public void Warhammer3_OpenFromToolsMenu_LoadAnimation_DuplicateAndUndoFrame()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);

            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor) as AnimationKeyframeEditorViewModel;
            Assert.That(editor, Is.Not.Null);

            // EditorHostView draws a tool's panel only for a data context that names its view type.
            Assert.That(editor!.EditorViewModelType, Is.EqualTo(typeof(AnimationEditor.AnimationKeyframeEditor.EditorView)));
            Assert.That(editor.SceneObjects.Select(x => x.Data.Id), Is.EqualTo(new[] { "Rider", "Mount" }));

            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);

            var clip = editor.Rider.AnimationClip;
            Assert.That(clip, Is.Not.Null, "the idle animation did not load onto the rider");
            var frames = clip!.DynamicFrames.Count;
            Assert.That(editor.FramesDurationInSeconds, Is.EqualTo(clip.PlayTimeInSec.ToString()));
            Assert.That(editor.FrameNrLength, Is.EqualTo((frames - 1).ToString()));
            Assert.That(editor.IsDirty.Value, Is.False);

            editor.DuplicateFrame();
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames.Count, Is.EqualTo(frames + 1));

            editor.UndoPose();
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames.Count, Is.EqualTo(frames));

            editor.RedoPose();
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames.Count, Is.EqualTo(frames + 1));
        }

        // The interpolation slider applies to the frame on every step it moves. A whole drag of it undoes as one step, back to the
        // frame as it was, not one step per slider position.
        [Test]
        public void Warhammer3_InterpolationSliderDrag_UndoesInOneStep()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);
            var commands = editor.CommandManager;
            Assert.That(commands.CanUndo(), Is.False, "nothing to undo as loaded");

            editor.Rider.Player.CurrentFrame = 0;
            editor.SelectFrameAInterpolation();
            editor.Rider.Player.CurrentFrame = 6;
            editor.SelectFrameBInterpolation();
            editor.Rider.Player.CurrentFrame = 3;
            var before = editor.Rider.AnimationClip!.DynamicFrames[3].Clone();
            editor.PreviewInterpolation = true;

            foreach (var value in new[] { 0.2f, 0.4f, 0.6f, 0.8f })
                editor.InterpolationValue = value;
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation, Is.Not.EqualTo(before.Rotation), "the drag moved the frame");

            commands.Undo();
            Assert.That(commands.CanUndo(), Is.False, "the whole drag was one step");
            var after = editor.Rider.AnimationClip!.DynamicFrames[3];
            Assert.That((after.Position, after.Rotation), Is.EqualTo((before.Position, before.Rotation)));
        }

        // IK solves on the key frame alone. On a real skeleton and clip, the hand it places must be where the sampler, and so the
        // viewport and the gizmo, draws it.
        [Test]
        public void Warhammer3_InverseKinematics_PutsTheHandWhereTheSamplerDrawsIt()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);
            var skeleton = editor.Skeleton;
            var frame = editor.Rider.AnimationClip!.DynamicFrames[3];
            var (hand, shoulder) = (skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("upperarm_left"));
            Assert.That((hand, shoulder), Is.Not.EqualTo((-1, -1)));
            Vector3 Sampled() => AnimationSampler.Sample(3, 0, skeleton, editor.Rider.AnimationClip).GetSkeletonAnimatedWorld(skeleton, hand).Translation;
            Assert.That(Vector3.Distance(TransformBoneCommand.WorldTransform(frame, skeleton, hand).Translation, Sampled()), Is.LessThan(1e-4f), "the solver's space is the sampler's");

            var target = Sampled() + new Vector3(0.1f, 0.15f, -0.05f);
            TransformBoneCommand.SolveInverseKinematics(frame, skeleton, hand, shoulder, target);

            Assert.That(Vector3.Distance(Sampled(), target), Is.LessThan(5e-4f));
        }

        // "Interpolate across frames" said "not available yet". It sets every frame between A and B to the pose that far between
        // them, leaves A and B alone, and undoes as one step.
        [Test]
        public void Warhammer3_InterpolateAcrossFrames_SetsEachFrameBetweenByHowFarAlong_AndUndoesInOneStep()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);
            var clip = editor.Rider.AnimationClip!;
            var original = clip.Clone();
            string Pose(int frame) => string.Join(" ", clip.DynamicFrames[frame].Position) + " " + string.Join(" ", clip.DynamicFrames[frame].Rotation);
            var loaded = Enumerable.Range(0, 7).Select(Pose).ToList();

            editor.Rider.Player.CurrentFrame = 0;
            editor.SelectFrameAInterpolation();
            editor.Rider.Player.CurrentFrame = 6;
            editor.SelectFrameBInterpolation();
            editor.ApplyInterpolationAcrossFrames();

            var expected = AnimationSampler.SampleBetween2Frames(0, 6, 2 / 6f, editor.Skeleton, original);
            var frame2 = clip.DynamicFrames[2];
            for (var bone = 0; bone < expected.BoneTransforms.Count; bone++)
                Assert.That((frame2.Position[bone], frame2.Rotation[bone]), Is.EqualTo((expected.BoneTransforms[bone].Translation, expected.BoneTransforms[bone].Rotation)), $"bone {bone}");
            Assert.That((Pose(0), Pose(6)), Is.EqualTo((loaded[0], loaded[6])), "A and B are left as they were");
            Assert.That(Enumerable.Range(1, 5).Count(x => Pose(x) != loaded[x]), Is.EqualTo(5), "every frame between was set");
            Assert.That(editor.IsDirty.Value, Is.True);

            editor.UndoPose();
            Assert.That(Enumerable.Range(0, 7).Select(Pose), Is.EqualTo(loaded), "one undo puts all of them back");
            Assert.That(editor.CommandManager.CanUndo(), Is.False);
        }

        // The Mount is a reference to pose the Rider against. Its animation must not replace the
        // Rider's original clip, its duration, or the unsaved-changes flag.
        [Test]
        public void Warhammer3_MountAnimationChange_LeavesRiderEditStateAlone()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);

            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            var karl = runner.PackFileService.FindFile(KarlMesh)!;
            sceneObjectEditor.SetMesh(editor.Rider, karl);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);
            sceneObjectEditor.SetMesh(editor.Mount, karl);

            var riderDuration = editor.FramesDurationInSeconds;
            editor.FramesDurationInSeconds = "3";
            editor.ApplyDuration();
            Assert.That(editor.IsDirty.Value, Is.True);

            var mountClip = editor.Rider.AnimationClip!.Clone();
            mountClip.PlayTimeInSec = 9;
            sceneObjectEditor.SetAnimationClip(editor.Mount, mountClip, "mount");

            Assert.That(editor.FramesDurationInSeconds, Is.EqualTo("3"), $"was {riderDuration} before the edit");
            Assert.That(editor.IsDirty.Value, Is.True, "loading a mount animation discarded the unsaved flag");
        }

        // A one-frame clip saved as one dynamic frame and no static frame, a file CA never ships and the game crashes on
        // (upstream #159). It now saves the way CA stores a pose: every bone static, no dynamic frames, held for
        // round(time x 20) + 1 frames and never under 3. Loading it back gives the same pose.
        [TestCase(0.3f, 7, 0.3f)]
        [TestCase(0f, 3, 0.1f)]
        public void Warhammer3_OneFrameClip_SavesAsAPose(float playTime, int expectedFrameCount, float expectedTime)
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);

            var clip = editor.Rider.AnimationClip!.Clone();
            clip.DynamicFrames.RemoveRange(1, clip.DynamicFrames.Count - 1);
            clip.PlayTimeInSec = playTime;
            var skeleton = editor.Rider.Skeleton;

            var bytes = Shared.GameFormats.Animation.AnimationFile.ConvertToBytes(clip.ConvertToFileFormat(skeleton));
            var saved = Shared.GameFormats.Animation.AnimationFile.Create(new Shared.ByteParsing.ByteChunk(bytes));
            var part = saved.AnimationParts.Single();

            Assert.That(part.DynamicFrames, Is.Empty);
            Assert.That(part.StaticFrame, Is.Not.Null);
            Assert.That(part.TranslationMappings.All(x => x.IsStatic) && part.RotationMappings.All(x => x.IsStatic), Is.True);
            Assert.That(part.PoseFrameCount, Is.EqualTo(expectedFrameCount));
            Assert.That(saved.Header.FrameRate, Is.EqualTo(20));
            Assert.That(saved.Header.AnimationTotalPlayTimeInSec, Is.EqualTo(expectedTime).Within(1e-6));

            // The pose must come back as close as the same frame does through the dynamic path, whose 16-bit rotations
            // already lose a little.
            var asDynamic = editor.Rider.AnimationClip!.Clone();
            asDynamic.DynamicFrames.RemoveRange(3, asDynamic.DynamicFrames.Count - 3);
            var dynamicBack = new AnimationClip(Shared.GameFormats.Animation.AnimationFile.Create(new Shared.ByteParsing.ByteChunk(
                Shared.GameFormats.Animation.AnimationFile.ConvertToBytes(asDynamic.ConvertToFileFormat(skeleton)))), skeleton);

            var reloaded = new AnimationClip(saved, skeleton);
            Assert.That(reloaded.DynamicFrames, Has.Count.EqualTo(1));
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                Assert.That(reloaded.DynamicFrames[0].Position[bone], Is.EqualTo(dynamicBack.DynamicFrames[0].Position[bone]), $"bone {bone} position");
                Assert.That(reloaded.DynamicFrames[0].Rotation[bone], Is.EqualTo(dynamicBack.DynamicFrames[0].Rotation[bone]), $"bone {bone} rotation");
            }
        }

        // ---- The posing kit: pins, bend direction, mirror and joint limits, driven through the panel's own methods. ----

        /// <param name="store">Replaces the editor's joint-limit store; the default one reads the user's own cache.</param>
        /// <param name="beforeMesh">Runs on the open editor before Karl's mesh loads, which is what loads the limits.</param>
        internal static (AnimationKeyframeEditorViewModel Editor, GameWorld.Core.Components.Selection.BoneSelectionState State) OpenPosing(
            JointLimitStore? store = null, Action<AnimationKeyframeEditorViewModel>? beforeMesh = null)
        {
            var runner = new AssetEditorTestRunner(testServices: store == null ? null : services => services.AddScoped(_ => store));
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            beforeMesh?.Invoke(editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, IdleAnimation);
            editor.Rider.Player.CurrentFrame = 3;
            editor.EnterSelectMode();
            var state = editor.SelectionManager.GetState<GameWorld.Core.Components.Selection.BoneSelectionState>();
            Assert.That(state, Is.Not.Null, "bone selection mode did not start");
            return (editor, state!);
        }

        internal static void Select(GameWorld.Core.Components.Selection.BoneSelectionState state, int bone)
        {
            state.SelectedBones.Clear();
            state.ModifySelection([bone], false);
        }

        internal static Vector3 At(AnimationKeyframeEditorViewModel editor, int frame, int bone) =>
            TransformBoneCommand.WorldTransform(editor.Rider.AnimationClip!.DynamicFrames[frame], editor.Skeleton, bone).Translation;

        /// <summary>A store in a fresh temp folder whose clip list comes from <paramref name="clips"/>, called once per lookup.</summary>
        static JointLimitStore StoreWith(Func<int, ObservableCollection<AnimationReference>> clips) =>
            new(new ClipLookup(clips), Path.Combine(Path.GetTempPath(), "jointlimits_e2e_" + Guid.NewGuid().ToString("N")));

        // Not a Moq mock: Moq runs a setup's callback under its own lock, so one lookup held at a gate blocked every other.
        sealed class ClipLookup(Func<int, ObservableCollection<AnimationReference>> clips) : ISkeletonAnimationLookUpHelper
        {
            int _calls;
            public List<AnimationReference> CopyAnimationsForSkeleton(string skeletonName) => clips(Interlocked.Increment(ref _calls)).ToList();
            public ObservableCollection<AnimationReference> GetAnimationsForSkeleton(string skeletonName) => [];
            public AnimationReference? FindAnimationRefFromPackFile(Shared.Core.PackFiles.Models.PackFile animation) => null;
            public ObservableCollection<string> GetAllSkeletonFileNames() => [];
            public Shared.GameFormats.Animation.AnimationFile? GetSkeletonFileFromName(string skeletonName) => null;
            public void Dispose() { }
        }

        static ObservableCollection<AnimationReference> NoClips => [];

        /// <summary>
        /// Waits up to 10 s, pumping the test thread's messages: the editor leaves a WinForms context on it, and the limits load
        /// posts its continuations there, as it does to the UI thread in the app. A plain Wait would never see them run.
        /// </summary>
        static bool PumpUntil(Func<bool> done)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!done())
            {
                if (DateTime.UtcNow > until)
                    return false;
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(5);
            }
            return true;
        }

        static JointLimits OneLimit(GameSkeleton skeleton, int bone) =>
            new(skeleton.SkeletonName, new Dictionary<int, BoneLimit> { [bone] = new(0, 0.1f, new Vector2(-0.1f), new Vector2(0.1f)) });

        /// <summary>Holds every continuation posted to it until the test runs them, so a test can act between a task finishing and its continuation.</summary>
        sealed class HeldContinuations : SynchronizationContext
        {
            readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _posted = new();
            public override void Post(SendOrPostCallback d, object? state) => _posted.Add((d, state));

            public (SendOrPostCallback Callback, object? State) Next() =>
                _posted.TryTake(out var item, TimeSpan.FromSeconds(10)) ? item : throw new TimeoutException("nothing was posted");

            public void RunUntil(Func<bool> done)
            {
                while (!done())
                {
                    var (callback, state) = Next();
                    callback(state);
                }
            }
        }

        [Test]
        public void Warhammer3_PinnedFeet_StayWhenAnIkDragMovesTheBody()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var (footL, footR, hand) = (skeleton.GetBoneIndexByName("foot_left"), skeleton.GetBoneIndexByName("foot_right"), skeleton.GetBoneIndexByName("hand_left"));
            Assert.That(new[] { footL, footR, hand }, Has.None.EqualTo(-1));
            Select(state, footL);
            editor.PinSelectedBone();
            Select(state, footR);
            editor.PinSelectedBone();
            Assert.That(editor.PinRows.Select(x => x.BoneName), Is.EqualTo(new[] { "foot_left", "foot_right" }));
            var feet = (At(editor, 3, footL), At(editor, 3, footR));
            var root = editor.Rider.AnimationClip!.DynamicFrames[3].Rotation[skeleton.GetBoneIndexByName("root")];

            // An IK drag from the hand all the way up to the root turns the spine and pelvis, which swing the legs.
            editor.EnableInverseKinematics.Value = true;
            editor.ModelBoneListForIKEndBone.SelectedItem = editor.ModelBoneListForIKEndBone.PossibleValues.First(x => x.BoneIndex == 0);
            Select(state, hand);
            var command = editor.CommandFactory.CreateWithBuilder<TransformBoneCommand>().Configure(x => x.Configure([hand], state)).Build();
            command.ApplyTransformation(Matrix.CreateTranslation(Vector3.Zero), GameWorld.Core.Components.Gizmo.GizmoMode.Translate);
            command.ApplyTransformation(Matrix.CreateTranslation(new Vector3(0.1f, -0.15f, 0.1f)), GameWorld.Core.Components.Gizmo.GizmoMode.Translate);
            editor.CommandManager.ExecuteCommand(command);

            // The idle's legs are nearly straight, so a bigger drag swings the hips past where a leg can follow.
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation[skeleton.GetBoneIndexByName("root")], Is.Not.EqualTo(root), "the drag must turn the root for the pins to have work");
            Assert.That(Vector3.Distance(At(editor, 3, footL), feet.Item1), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(At(editor, 3, footR), feet.Item2), Is.LessThan(1e-3f));
        }

        // The plain gizmo move (no IK) takes another path through TransformBoneCommand, one that needs the rider's real mesh node.
        [Test]
        public void Warhammer3_PinnedFeet_StayWhenTheGizmoMovesThePelvis()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var (footL, footR, root) = (skeleton.GetBoneIndexByName("foot_left"), skeleton.GetBoneIndexByName("foot_right"), skeleton.GetBoneIndexByName("root"));
            Select(state, footL);
            editor.PinSelectedBone();
            Select(state, footR);
            editor.PinSelectedBone();
            var feet = (At(editor, 3, footL), At(editor, 3, footR));
            var rootBefore = At(editor, 3, root);
            Select(state, root);

            // The plain path reads the mesh's sampled frame, which a render tick builds; a test draws no frame, so build it here.
            ((GameWorld.Core.SceneNodes.Rmv2MeshNode)state.RenderObject).AnimationPlayer.SetManualFrame(AnimationSampler.Sample(3, 0, skeleton, editor.Rider.AnimationClip!));
            // It also takes an identity matrix to mean "no drag yet", so the gizmo starts where a real one would: off the origin.
            var command = editor.CommandFactory.CreateWithBuilder<TransformBoneCommand>().Configure(x => x.Configure([root], state)).Build();
            command.ApplyTransformation(Matrix.CreateTranslation(rootBefore), GameWorld.Core.Components.Gizmo.GizmoMode.Translate);
            command.ApplyTransformation(Matrix.CreateTranslation(rootBefore + new Vector3(0, -0.08f, 0)), GameWorld.Core.Components.Gizmo.GizmoMode.Translate);
            editor.CommandManager.ExecuteCommand(command);

            Assert.That(Vector3.Distance(At(editor, 3, root), rootBefore), Is.GreaterThan(0.05f), "the hips came down");
            Assert.That(Vector3.Distance(At(editor, 3, footL), feet.Item1), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(At(editor, 3, footR), feet.Item2), Is.LessThan(1e-3f));
        }

        [Test]
        public void Warhammer3_HoldPinsAcrossAB_KeepsTheFootOnEveryFrame()
        {
            var (editor, state) = OpenPosing();
            var foot = editor.Skeleton.GetBoneIndexByName("foot_left");
            Select(state, foot);
            editor.PinSelectedBone();
            editor.Rider.Player.CurrentFrame = 0;
            editor.SelectFrameAInterpolation();
            editor.Rider.Player.CurrentFrame = 6;
            editor.SelectFrameBInterpolation();
            var held = At(editor, 0, foot);
            var stepBefore = editor.CommandManager.Peek();

            editor.HoldPinsAcrossAB();

            for (var frame = 0; frame <= 6; frame++)
                Assert.That(Vector3.Distance(At(editor, frame, foot), held), Is.LessThan(1e-3f), $"frame {frame}");
            editor.UndoPose();
            Assert.That(editor.CommandManager.Peek(), Is.SameAs(stepBefore), "the hold was one step");
        }

        // With "Allow anim root" on, pinning the root looked up its grandparent's parent at index -1 and threw, after the
        // pin was already added, so it held nothing and showed no row.
        [Test]
        public void Warhammer3_PinningTheRootBone_DoesNotThrow()
        {
            var (editor, state) = OpenPosing();
            editor.AllowToSelectAnimRoot.Value = true;
            Select(state, 0);

            Assert.That(() => editor.PinSelectedBone(), Throws.Nothing);
            Assert.That(editor.PinRows.Select(x => x.BoneName), Is.EqualTo(new[] { editor.Skeleton.BoneNames[0] }));
        }

        // When a limit stopped the bend slider short, it still recorded the full slider value as applied. After a pause, the
        // next drag back to 0 turned from that value, not from what was applied, and left the arm on the far side.
        [Test]
        public void Warhammer3_Bend_StoppedByALimit_ComesBackToWhereItStarted()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var (hand, top) = (skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("upperarm_left"));
            var start = editor.Rider.AnimationClip!.DynamicFrames[3].Rotation[top];
            GameWorld.Core.Animation.Posing.SwingTwist.Decompose(Quaternion.Inverse(skeleton.Rotation[top]) * start,
                GameWorld.Core.Animation.Posing.SwingTwist.BoneAxis(skeleton, top), out var twist, out var swing);
            editor.MeasuredJointLimits = new GameWorld.Core.Animation.Posing.JointLimits(skeleton.SkeletonName, new Dictionary<int, GameWorld.Core.Animation.Posing.BoneLimit>
            {
                [top] = new(twist, 0.1f, swing - new Vector2(0.1f), swing + new Vector2(0.1f)),
            });
            editor.EnableInverseKinematics.Value = true;
            Select(state, hand);

            editor.BendDegrees = 90;
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation[top], Is.Not.EqualTo(start), "the arm turned as far as its limit");
            Thread.Sleep(1100);   // a pause ends the slider's undo step
            editor.BendDegrees = 0;

            var back = editor.Rider.AnimationClip!.DynamicFrames[3].Rotation[top];
            Assert.That(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(back), Quaternion.Normalize(start))), Is.GreaterThan(1 - 1e-6f));
        }

        [Test]
        public void Warhammer3_Mirror_TwiceRestoresTheFrame()
        {
            var (editor, _) = OpenPosing();
            var before = editor.Rider.AnimationClip!.DynamicFrames[3].Clone();

            editor.MirrorPose();
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation, Is.Not.EqualTo(before.Rotation), "one mirror changes the pose");
            editor.MirrorPose();

            var after = editor.Rider.AnimationClip!.DynamicFrames[3];
            for (var bone = 0; bone < before.Rotation.Count; bone++)
            {
                // CA's 16-bit rotations are a touch short of unit length; the mirror hands back unit ones.
                var dot = MathF.Abs(Quaternion.Dot(after.Rotation[bone], Quaternion.Normalize(before.Rotation[bone])));
                Assert.That(2 * MathF.Acos(MathF.Min(1, dot)), Is.LessThan(2e-3f), $"rotation of {editor.Skeleton.BoneNames[bone]}");
                Assert.That(Vector3.Distance(after.Position[bone], before.Position[bone]), Is.LessThan(1e-4f), $"position of {editor.Skeleton.BoneNames[bone]}");
            }
        }

        [Test]
        public void Warhammer3_Bend_UndoesAsOneStep()
        {
            var (editor, state) = OpenPosing();
            var hand = editor.Skeleton.GetBoneIndexByName("hand_left");
            editor.EnableInverseKinematics.Value = true;
            Select(state, hand);
            var before = editor.Rider.AnimationClip!.DynamicFrames[3].Clone();
            var handBefore = At(editor, 3, hand);
            var stepBefore = editor.CommandManager.Peek();

            foreach (var degrees in new[] { 10f, 20f, 30f })
                editor.BendDegrees = degrees;

            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation, Is.Not.EqualTo(before.Rotation), "the bend moved the arm");
            Assert.That(Vector3.Distance(At(editor, 3, hand), handBefore), Is.LessThan(1e-3f), "the hand stays put");
            editor.UndoPose();
            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation, Is.EqualTo(before.Rotation));
            Assert.That(editor.CommandManager.Peek(), Is.SameAs(stepBefore), "the whole slider drag was one step");
        }

        [Test]
        public void Warhammer3_LimitsOff_IkMatchesTheOldSolver()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var (hand, shoulder) = (skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("upperarm_left"));
            editor.UseJointLimits.Value = false;
            editor.EnableInverseKinematics.Value = true;
            editor.ModelBoneListForIKEndBone.SelectedItem = editor.ModelBoneListForIKEndBone.PossibleValues.First(x => x.BoneIndex == shoulder);
            Select(state, hand);
            Assert.That(state.JointLimits, Is.Null);
            var expected = editor.Rider.AnimationClip!.DynamicFrames[3].Clone();
            TransformBoneCommand.SolveInverseKinematics(expected, skeleton, hand, shoulder, At(editor, 3, hand) + new Vector3(0.1f, 0.1f, 0));

            var command = editor.CommandFactory.CreateWithBuilder<TransformBoneCommand>().Configure(x => x.Configure([hand], state)).Build();
            command.ApplyTransformation(Matrix.CreateTranslation(Vector3.Zero), GameWorld.Core.Components.Gizmo.GizmoMode.Translate);
            command.ApplyTransformation(Matrix.CreateTranslation(new Vector3(0.1f, 0.1f, 0)), GameWorld.Core.Components.Gizmo.GizmoMode.Translate);

            Assert.That(editor.Rider.AnimationClip!.DynamicFrames[3].Rotation, Is.EqualTo(expected.Rotation));
        }

        [Test]
        public void Warhammer3_PasteIntoCurrentFrame_KeepsPinnedFeet()
        {
            var (editor, state) = OpenPosing();
            var foot = editor.Skeleton.GetBoneIndexByName("foot_left");
            Select(state, foot);
            editor.PinSelectedBone();
            editor.Rider.Player.CurrentFrame = 0;
            editor.CopyCurrentPose();
            editor.Rider.Player.CurrentFrame = 10;
            var footOn10 = At(editor, 10, foot);
            Assume.That(Vector3.Distance(At(editor, 0, foot), footOn10), Is.GreaterThan(1e-4f), "the idle must move the foot between frames 0 and 10");

            editor.PasteIntoCurrentFrame();

            Assert.That(Vector3.Distance(At(editor, 10, foot), footOn10), Is.LessThan(1e-3f));
        }

        // Review Focus 1: the posing code walks bones parents-first and must not rely on index order. This records whether
        // humanoid01 happens to be index-ordered; it fails nothing either way.
        [Test]
        public void Warhammer3_Humanoid01_ParentOrder_IsRecorded()
        {
            var (editor, _) = OpenPosing();
            var skeleton = editor.Skeleton;
            var outOfOrder = Enumerable.Range(0, skeleton.BoneCount).Count(x => skeleton.GetParentBoneIndex(x) > x);
            TestContext.Out.WriteLine($"{skeleton.SkeletonName}: {outOfOrder} of {skeleton.BoneCount} bones come before their parent");
            Assert.Pass();
        }

        // The bend slider was live whenever IK was on, so with no limb selected its thumb moved and nothing turned.
        [Test]
        public void Warhammer3_BendSlider_IsOnlyLiveWithOneLimbSelected()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var (hand, root) = (skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("root"));
            editor.EnableInverseKinematics.Value = true;

            Select(state, hand);
            Assert.That(editor.CanBend.Value, Is.True, "a hand, with IK on");
            Select(state, root);
            Assert.That(editor.CanBend.Value, Is.False, "root has no limb above it");
            state.ModifySelection([hand], false);
            Assert.That(editor.CanBend.Value, Is.False, "two bones");
            Select(state, hand);
            editor.EnableInverseKinematics.Value = false;
            Assert.That(editor.CanBend.Value, Is.False, "IK off");
        }

        // While the clip played, every new frame snapped the slider back to 0 under the user's thumb.
        [Test]
        public void Warhammer3_Bend_PausesPlayback()
        {
            var (editor, state) = OpenPosing();
            editor.EnableInverseKinematics.Value = true;
            Select(state, editor.Skeleton.GetBoneIndexByName("hand_left"));
            editor.Rider.Player.Play();

            editor.BendDegrees = 20;

            Assert.That(editor.Rider.Player.IsPlaying, Is.False);
        }

        // A cancelled re-measure said "IK is unlimited" while the earlier limits were still in use.
        [Test]
        public void Warhammer3_CancelledRemeasure_SaysTheEarlierLimitsStay()
        {
            using var gate = new ManualResetEventSlim();
            var (editor, _) = OpenPosing(StoreWith(call =>
            {
                if (call == 2)
                    gate.Wait();
                return NoClips;
            }));
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);
            var earlier = OneLimit(editor.Skeleton, editor.Skeleton.GetBoneIndexByName("upperarm_left"));
            editor.MeasuredJointLimits = earlier;

            editor.RemeasureJointLimits();
            editor.CancelMeasuring();
            gate.Set();
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);

            Assert.That(editor.MeasuredJointLimits, Is.SameAs(earlier));
            Assert.That(editor.JointLimitStatus.Value, Does.Contain("earlier joint limits stay"));
            Assert.That(editor.IsMeasuringJointLimits.Value, Is.False);
        }

        // A load that a newer one had replaced still wrote its own error over the newer load's status.
        [Test]
        public void Warhammer3_AStaleMeasureThatFails_LeavesTheNewerStatusAlone()
        {
            using var gate = new ManualResetEventSlim();
            using var entered = new ManualResetEventSlim();
            var (editor, _) = OpenPosing(StoreWith(call =>
            {
                if (call == 1)
                {
                    entered.Set();
                    gate.Wait();
                    throw new IOException("the replaced measure broke");
                }
                return NoClips;
            }));
            var stale = editor.JointLimitsTask;
            Assert.That(PumpUntil(() => entered.IsSet), Is.True, "the first load reached the clip list");

            editor.RemeasureJointLimits();
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);
            var status = editor.JointLimitStatus.Value;
            Assume.That(status, Does.Contain("No CA clips"));
            gate.Set();
            Assert.That(PumpUntil(() => stale.IsCompleted), Is.True);

            Assert.That(editor.JointLimitStatus.Value, Is.EqualTo(status));
        }

        // A cancel that landed after the measure had finished left the status on "N clips read" and dropped the limits.
        [Test]
        public void Warhammer3_CancelAfterTheMeasureFinished_StillReportsIt()
        {
            var (editor, _) = OpenPosing(StoreWith(_ => NoClips));
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);
            var held = new HeldContinuations();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(held);
            try
            {
                editor.RemeasureJointLimits();
                var finished = held.Next();   // the measure is done; what follows it waits here
                editor.CancelMeasuring();
                finished.Callback(finished.State);
                held.RunUntil(() => editor.JointLimitsTask.IsCompleted);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Assert.That(editor.JointLimitStatus.Value, Does.Contain("No CA clips"));
            Assert.That(editor.IsMeasuringJointLimits.Value, Is.False);
        }

        // Every CA clip for the skeleton was read the first time the editor opened, even with the Joint limits box unchecked.
        [Test]
        public void Warhammer3_LimitsOff_MeasuresNothingUntilTicked()
        {
            var lookups = 0;
            var (editor, _) = OpenPosing(StoreWith(call =>
            {
                lookups = call;
                return NoClips;
            }), beforeMesh: x => x.UseJointLimits.Value = false);
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);
            Assert.That(lookups, Is.Zero, "nothing read with the box unchecked");

            editor.UseJointLimits.Value = true;
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);

            Assert.That(lookups, Is.GreaterThan(0));
            Assert.That(editor.JointLimitStatus.Value, Does.Contain("No CA clips"));
        }

        // A paste that moved nothing still pushed a "Keep pinned bones" step after it.
        [Test]
        public void Warhammer3_PasteThatMovesNothing_AddsNoPinStep()
        {
            var (editor, state) = OpenPosing();
            editor.IncrementFrameAfterCopyOperation.Value = false;
            Select(state, editor.Skeleton.GetBoneIndexByName("foot_left"));
            editor.PinSelectedBone();
            editor.CopyCurrentPose();   // frame 3, pasted back onto frame 3
            var stepBefore = editor.CommandManager.Peek();

            editor.PasteIntoCurrentFrame();
            editor.UndoPose();

            Assert.That(editor.CommandManager.Peek(), Is.SameAs(stepBefore), "the paste was one step");
        }

        // Pasting into a pinned leg's shin was turned straight back by the foot's pin, which had no idea the shin was pasted.
        [Test]
        public void Warhammer3_PasteIntoTheSelectedShin_IsNotUndoneByTheFootPin()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var (foot, shin) = (skeleton.GetBoneIndexByName("foot_left"), skeleton.GetBoneIndexByName("lowerleg_left"));
            editor.IncrementFrameAfterCopyOperation.Value = false;
            Select(state, foot);
            editor.PinSelectedBone();
            editor.Rider.Player.CurrentFrame = 0;
            editor.CopyCurrentPose();
            var copied = Quaternion.Normalize(editor.Rider.AnimationClip!.DynamicFrames[0].Rotation[shin]);
            editor.Rider.Player.CurrentFrame = 10;
            Assume.That(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(editor.Rider.AnimationClip!.DynamicFrames[10].Rotation[shin]), copied)), Is.LessThan(1 - 1e-6f));
            Select(state, shin);

            editor.PasteIntoSelectedCurrentNode();

            var pasted = Quaternion.Normalize(editor.Rider.AnimationClip!.DynamicFrames[10].Rotation[shin]);
            Assert.That(MathF.Abs(Quaternion.Dot(pasted, copied)), Is.GreaterThan(1 - 1e-6f));
        }

        // The bend slider turned the hand with the arm, so a pin below the hand (a finger, a weapon bone) moved.
        [Test]
        public void Warhammer3_Bend_KeepsAPinBelowTheHand()
        {
            var (editor, state) = OpenPosing();
            var skeleton = editor.Skeleton;
            var hand = skeleton.GetBoneIndexByName("hand_left");
            var finger = Enumerable.Range(0, skeleton.BoneCount).First(x => skeleton.GetParentBoneIndex(x) == hand && Vector3.Distance(At(editor, 3, x), At(editor, 3, hand)) > 0.02f);
            Select(state, finger);
            editor.PinSelectedBone();
            var held = At(editor, 3, finger);
            editor.EnableInverseKinematics.Value = true;
            Select(state, hand);

            editor.BendDegrees = 60;

            Assert.That(Vector3.Distance(At(editor, 3, finger), held), Is.LessThan(1e-3f), skeleton.BoneNames[finger]);
        }

        // With nothing cached, "Reset selected bone" cleared the override file and left the bone free without a word.
        [Test]
        public void Warhammer3_ResetBoneLimit_WithNothingCached_SaysSo()
        {
            var (editor, state) = OpenPosing(StoreWith(_ => NoClips));
            Assert.That(PumpUntil(() => editor.JointLimitsTask.IsCompleted), Is.True);
            var skeleton = editor.Skeleton;
            var arm = skeleton.GetBoneIndexByName("upperarm_left");
            editor.MeasuredJointLimits = OneLimit(skeleton, arm);
            Select(state, arm);
            editor.WidenSelectedBoneLimit();
            var status = editor.JointLimitStatus.Value;

            editor.ResetSelectedBoneLimit();

            Assert.That(editor.JointLimitStatus.Value, Is.Not.EqualTo(status));
            Assert.That(editor.JointLimitStatus.Value, Does.Contain("Re-measure"));
        }

        // Both slot pickers in the mount-link panel had their handlers commented out, so picking a slot loaded nothing.
        // A mount slot X now plays its animation and picks the rider's RIDER_X, which plays the rider's.
        [Test]
        public void Warhammer3_MountLink_PickingASlot_LoadsItAndTheRiderPartner()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(KarlMesh)!);
            sceneObjectEditor.SetMesh(editor.Mount, runner.PackFileService.FindFile(KarlMesh)!);

            var link = editor.MountLinkController;
            var riderStand = new Shared.GameFormats.AnimationPack.AnimationBinEntryGenericFormat { SlotName = "RIDER_STAND", AnimationFile = IdleAnimation };
            var riderMissing = new Shared.GameFormats.AnimationPack.AnimationBinEntryGenericFormat { SlotName = "RIDER_RUN", AnimationFile = "animations/nope.anim" };
            link.SelectedRiderTag.UpdatePossibleValues(new[] { riderMissing, riderStand });
            link.SelectedMountTag.UpdatePossibleValues(new[] { new Shared.GameFormats.AnimationPack.AnimationBinEntryGenericFormat { SlotName = "STAND", AnimationFile = IdleAnimation } });

            link.SelectedMountTag.SelectedItem = link.SelectedMountTag.Values.Single();

            Assert.That(editor.Mount.AnimationClip, Is.Not.Null, "the mount slot loaded no animation");
            Assert.That(link.SelectedRiderTag.SelectedItem, Is.SameAs(riderStand), "the rider's RIDER_STAND was not picked");
            Assert.That(editor.Rider.AnimationClip, Is.Not.Null, "the rider slot loaded no animation");

            // A slot naming an .anim no pack has leaves the rider with nothing, not the last clip.
            link.SelectedRiderTag.SelectedItem = riderMissing;
            Assert.That(editor.Rider.AnimationClip, Is.Null);
        }
    }
}
