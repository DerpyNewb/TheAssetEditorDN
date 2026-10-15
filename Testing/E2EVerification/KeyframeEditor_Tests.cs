using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
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
