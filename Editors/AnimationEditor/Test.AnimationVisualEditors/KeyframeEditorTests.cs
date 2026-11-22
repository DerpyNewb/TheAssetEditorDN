using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Test.TestingUtility.Shared;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using static Test.AnimationVisualEditors.VanillaRiders;

namespace Test.AnimationVisualEditors
{
    public class KeyframeEditorTests
    {
        const string RiderAnimation = @"animations\battle\humanoid01\rider\horse01\spear_and_shield\stand\hu1_hr1_sps_rider1_stand_01.anim";

        static (AnimationKeyframeEditorViewModel Editor, SceneObject Rider, AnimationPlayerViewModel Controls) Open()
        {
            var runner = VanillaRiders.Load();
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(RiderMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, RiderAnimation);
            var controls = runner.GetRequiredServiceInCurrentEditorScope<AnimationPlayerViewModel>();
            controls.IsEnabled.Value = true;
            return (editor, editor.Rider, controls);
        }

        [Test]
        public void MovingABoneWorksAfterSelectBonesIsClickedAgain()
        {
            var runner = VanillaRiders.Load();
            var editor = (AnimationKeyframeEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationKeyFrame_Editor);
            var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            sceneObjectEditor.SetMesh(editor.Rider, runner.PackFileService.FindFile(RiderMesh)!);
            sceneObjectEditor.SetAnimation(editor.Rider, RiderAnimation);

            // The first Select bones, then back out of bone mode, then a second one: a new selection state
            editor.EnterSelectMode();
            var first = editor.SelectionManager.GetState<BoneSelectionState>();
            editor.UndoPose();
            editor.UndoPose();
            Assert.That(editor.SelectionManager.GetState().Mode, Is.EqualTo(GeometrySelectionMode.Object), "precondition: undo left bone mode");
            editor.EnterSelectMode();
            var state = editor.SelectionManager.GetState<BoneSelectionState>()!;
            Assert.That(state, Is.Not.SameAs(first), "precondition: a new bone selection state");

            const int bone = 7;
            editor.CommandFactory.CreateWithBuilder<BoneSelectionCommand>().Configure(x => x.Configure([bone], false, false)).BuildAndExecute();
            var before = editor.Rider.AnimationClip.DynamicFrames[state.CurrentFrame].Rotation[bone];
            ((Rmv2MeshNode)state.RenderObject).AnimationPlayer.Refresh(); // the game loop does this every tick

            // What the gizmo does on a rotate and a move
            foreach (var (mode, transform) in new[] { (GizmoMode.Rotate, Microsoft.Xna.Framework.Matrix.CreateRotationY(0.3f)), (GizmoMode.Translate, Microsoft.Xna.Framework.Matrix.CreateTranslation(0.1f, 0, 0)) })
            {
                var command = editor.CommandFactory.CreateWithBuilder<TransformBoneCommand>().Configure(x => x.Configure(state.SelectedBones, state)).Build();
                // The gizmo sends a stream of transforms; the first only sets where the drag starts
                Assert.DoesNotThrow(() => command.ApplyTransformation(Microsoft.Xna.Framework.Matrix.CreateTranslation(0.001f, 0, 0), mode), mode.ToString());
                Assert.DoesNotThrow(() => command.ApplyTransformation(transform, mode), mode.ToString());
            }

            Assert.That(editor.Rider.AnimationClip.DynamicFrames[state.CurrentFrame].Rotation[bone], Is.Not.EqualTo(before));
            Assert.That(editor.GetModifiedBones(), Does.Contain(bone), "the editor heard about the edit");
        }

    }
}
