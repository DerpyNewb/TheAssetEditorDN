using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimatioReTarget
{
    // The retarget tool's undo: a setting left is a step, a mapping applied by name is a step of its own, a change not yet
    // checkpointed is what Undo takes back, and undo and redo select the bone the step was made in.
    public class RetargetUndoTests
    {
        [Test]
        public void SettingsAndMapping_UndoAndRedo()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var editor = (AnimationRetargetEditor)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationRetarget_Editor);
            var scene = runner.GetRequiredServiceInCurrentEditorScope<Editors.Shared.Core.Common.SceneObjectEditor>();
            scene.SetMesh(editor.GetSceneObjectFromId(AnimationRetargetIds.Source).Data, runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.wsmodel")!);
            scene.SetMesh(editor.GetSceneObjectFromId(AnimationRetargetIds.Target).Data, runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.wsmodel")!);
            var bones = editor.BoneManager;
            bones.ApplyDefaultMapping();

            SkeletonBoneNode_new Node(string name) => SkeletonBoneNodeHelper.GetNodeFromName(name, bones.Bones) ?? throw new AssertionException($"no node named {name}");
            var (hand, other) = (Node("hand_right"), Node("hand_left"));
            var (length, scale, otherFrom) = (hand.BoneLengthMult, editor.Settings.SkeletonScale, other.MappedFrom);
            Assert.That(otherFrom, Is.Not.EqualTo("hand_right"));

            bones.SelectedBone = hand;
            hand.BoneLengthMult = length + 1;
            editor.Checkpoint();
            bones.ApplyPresetBones([bones.PresetBones().Single(x => x.Name == "hand_left") with { MappedFrom = "hand_right" }]);
            bones.SelectedBone = other;
            editor.Settings.SkeletonScale = scale + 0.5f;

            (float, float, string, string?) Now() => (hand.BoneLengthMult, editor.Settings.SkeletonScale, other.MappedFrom, bones.SelectedBone?.BoneName);

            editor.Undo();
            Assert.That(Now(), Is.EqualTo((length + 1, scale, "hand_right", "hand_left")), "the scale is undone on its own");
            editor.Undo();
            Assert.That(Now(), Is.EqualTo((length + 1, scale, otherFrom, "hand_right")), "the mapping is undone on its own, made while the hand was selected");
            editor.Undo();
            Assert.That(Now(), Is.EqualTo((length, scale, otherFrom, "hand_right")));

            editor.Redo();
            editor.Redo();
            editor.Redo();
            Assert.That(Now(), Is.EqualTo((length + 1, scale + 0.5f, "hand_right", "hand_left")));

            // Another target skeleton is another bone tree: the last one's steps are not put on it by name.
            scene.SetMesh(editor.GetSceneObjectFromId(AnimationRetargetIds.Target).Data, runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.wsmodel")!);
            var newHand = Node("hand_right");
            var newLength = newHand.BoneLengthMult;
            editor.Undo();
            Assert.That(newHand.BoneLengthMult, Is.EqualTo(newLength), "nothing to undo on the new skeleton");
        }
    }
}
