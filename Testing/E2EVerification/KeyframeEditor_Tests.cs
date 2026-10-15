using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using GameWorld.Core.Animation;
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
    }
}
