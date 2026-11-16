using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using static Test.E2EVerification.KeyframeEditor_Tests;

namespace Test.E2EVerification
{
    // The Keyframes strip under the player: rows for every bone and for each selected bone, frames across, copy and paste.
    internal class KeyframeSheet_Tests
    {
        /// <summary>The editor with Karl on frame 3, its clipboard a string the test holds instead of the Windows one.</summary>
        static (AnimationKeyframeEditorViewModel Editor, BoneSelectionState State, KeyframeSheetViewModel Sheet) Open()
        {
            var (editor, state) = OpenPosing();
            string? clipboard = null;
            editor.Sheet.ReadClipboard = () => clipboard;
            editor.Sheet.WriteClipboard = x =>
            {
                clipboard = x;
                return true;
            };
            return (editor, state, editor.Sheet);
        }

        static void SelectBones(BoneSelectionState state, params int[] bones)
        {
            state.SelectedBones.Clear();
            state.ModifySelection(bones, false);
        }

        static void SelectCells(KeyframeSheetViewModel sheet, int row, int firstFrame, int lastFrame)
        {
            sheet.Select(row, firstFrame, extend: false);
            sheet.Select(row, lastFrame, extend: true);
        }

        static float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(MathF.Min(1, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)))));

        [Test]
        public void Rows_AreEveryBone_ThenEachSelectedBone_InSkeletonOrder()
        {
            var (editor, state, sheet) = Open();
            var skeleton = editor.Skeleton;
            Assert.That(sheet.Rows.Select(x => x.Name), Is.EqualTo(new[] { "All bones" }));

            SelectBones(state, skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("lowerarm_left"));

            Assert.That(sheet.Rows.Select(x => x.Bone), Is.EqualTo(new[] { -1, skeleton.GetBoneIndexByName("lowerarm_left"), skeleton.GetBoneIndexByName("hand_left") }));
        }

        [Test]
        public void CopyThenPaste_OnTheAllBonesRow_CopiesWholePoses_AsOneUndoStep()
        {
            var (editor, _, sheet) = Open();
            var clip = editor.Rider.AnimationClip!;
            var before = clip.DynamicFrames.Select(x => x.Clone()).ToList();
            SelectCells(sheet, 0, 2, 4);
            sheet.Copy();
            editor.Rider.Player.CurrentFrame = 10;
            var stepBefore = editor.CommandManager.Peek();

            sheet.Paste();

            for (var i = 0; i < 3; i++)
                Assert.That(clip.DynamicFrames[10 + i].Rotation, Is.EqualTo(before[2 + i].Rotation), $"frame {10 + i}");
            Assert.That(clip.DynamicFrames[13].Rotation, Is.EqualTo(before[13].Rotation), "the frame after the paste");
            editor.UndoPose();
            Assert.That(editor.CommandManager.Peek(), Is.SameAs(stepBefore));
            Assert.That(clip.DynamicFrames[10].Rotation, Is.EqualTo(before[10].Rotation));
        }

        [Test]
        public void CopyThenPaste_OnABoneRow_WritesOnlyThatBone()
        {
            var (editor, state, sheet) = Open();
            var skeleton = editor.Skeleton;
            var (hand, head) = (skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("head"));
            var clip = editor.Rider.AnimationClip!;
            var headBefore = clip.DynamicFrames[10].Rotation[head];
            SelectBones(state, hand);
            SelectCells(sheet, 1, 0, 0);
            sheet.Copy();
            editor.Rider.Player.CurrentFrame = 10;

            sheet.Paste();

            Assert.That(clip.DynamicFrames[10].Rotation[hand], Is.EqualTo(clip.DynamicFrames[0].Rotation[hand]));
            Assert.That(clip.DynamicFrames[10].Rotation[head], Is.EqualTo(headBefore));
        }

        [Test]
        public void PasteFlipped_OfTheLeftHand_ChangesTheRightHand_AndLeavesTheLeft()
        {
            var (editor, state, sheet) = Open();
            var skeleton = editor.Skeleton;
            var (left, right) = (skeleton.GetBoneIndexByName("hand_left"), skeleton.GetBoneIndexByName("hand_right"));
            var clip = editor.Rider.AnimationClip!;
            var frame10 = clip.DynamicFrames[10].Clone();
            SelectBones(state, left);
            SelectCells(sheet, 1, 0, 0);
            sheet.Copy();
            editor.Rider.Player.CurrentFrame = 10;

            sheet.PasteFlipped();

            Assert.That(clip.DynamicFrames[10].Rotation[left], Is.EqualTo(frame10.Rotation[left]));
            Assert.That(Angle(clip.DynamicFrames[10].Rotation[right], frame10.Rotation[right]), Is.GreaterThan(1e-3f));
        }

        [Test]
        public void PastePastTheEnd_SaysHowManyFramesDidNotFit()
        {
            var (editor, _, sheet) = Open();
            var last = editor.Rider.AnimationClip!.DynamicFrames.Count - 1;
            SelectCells(sheet, 0, 0, 2);
            sheet.Copy();
            sheet.CurrentFrame = last;

            sheet.Paste();

            Assert.That(sheet.Status, Does.Contain("2 frames"));
        }

        // Arriving on the last frame counted as playback reaching the end: with looping on, the player jumped back to frame 0
        // and started playing, so the ruler could never reach the last frame.
        [Test]
        public void TheRuler_CanReachTheLastFrame_WithoutStartingPlayback()
        {
            var (editor, _, sheet) = Open();
            var last = editor.Rider.AnimationClip!.DynamicFrames.Count - 1;

            sheet.CurrentFrame = last;

            Assert.That(sheet.CurrentFrame, Is.EqualTo(last));
            Assert.That(editor.Rider.Player.IsPlaying, Is.False);
        }

        [Test]
        public void Paste_WithNoFramesOnTheClipboard_SaysSo_AndChangesNothing()
        {
            var (editor, _, sheet) = Open();
            sheet.ReadClipboard = () => "not frames";
            var stepBefore = editor.CommandManager.Peek();

            sheet.Paste();

            Assert.That(sheet.Status, Does.Contain("no frames"));
            Assert.That(editor.CommandManager.Peek(), Is.SameAs(stepBefore));
        }

        // A selection over several frames is frames A and B, which interpolation, mirror and hold-pins act between.
        [Test]
        public void SelectingSeveralFrames_SetsFramesAAndB()
        {
            var (editor, _, sheet) = Open();
            var clip = editor.Rider.AnimationClip!;
            var frame7 = clip.DynamicFrames[7].Clone();

            SelectCells(sheet, 0, 2, 6);
            Assert.That((editor.SelectedFrameAInterpolation.Value, editor.SelectedFrameBInterpolation.Value), Is.EqualTo(("2", "6")));
            editor.MirrorPose();

            Assert.That(clip.DynamicFrames[7].Rotation, Is.EqualTo(frame7.Rotation), "outside A to B");
            sheet.ClearSelection();
            Assert.That(editor.SelectedFrameAInterpolation.Value, Is.Not.EqualTo("2"));
        }

        [Test]
        public void SelectingBones_KeepsTheSelectedFrames()
        {
            var (editor, state, sheet) = Open();
            SelectCells(sheet, 0, 2, 6);

            SelectBones(state, editor.Skeleton.GetBoneIndexByName("foot_left"));

            Assert.That(sheet.Selection, Is.Not.Null);
            Assert.That((sheet.Selection!.FirstFrame, sheet.Selection.LastFrame), Is.EqualTo((2, 6)));
        }

        [Test]
        public void Paste_KeepsAPinnedFootWhereItWas()
        {
            var (editor, state, sheet) = Open();
            var foot = editor.Skeleton.GetBoneIndexByName("foot_left");
            Select(state, foot);
            editor.PinSelectedBone();
            var footOn10 = At(editor, 10, foot);
            Assume.That(Vector3.Distance(At(editor, 0, foot), footOn10), Is.GreaterThan(1e-4f), "the idle must move the foot between frames 0 and 10");
            SelectCells(sheet, 0, 0, 0);
            sheet.Copy();
            editor.Rider.Player.CurrentFrame = 10;

            sheet.Paste();

            Assert.That(Vector3.Distance(At(editor, 10, foot), footOn10), Is.LessThan(1e-3f));
        }

        // Pasting into a pinned leg's shin row must not be turned straight back by the foot's pin.
        [Test]
        public void PasteIntoTheShinRow_IsNotUndoneByTheFootPin()
        {
            var (editor, state, sheet) = Open();
            var skeleton = editor.Skeleton;
            var (foot, shin) = (skeleton.GetBoneIndexByName("foot_left"), skeleton.GetBoneIndexByName("lowerleg_left"));
            Select(state, foot);
            editor.PinSelectedBone();
            var copied = editor.Rider.AnimationClip!.DynamicFrames[0].Rotation[shin];
            Assume.That(Angle(editor.Rider.AnimationClip!.DynamicFrames[10].Rotation[shin], copied), Is.GreaterThan(1e-3f));
            SelectBones(state, shin);
            SelectCells(sheet, 1, 0, 0);
            sheet.Copy();
            editor.Rider.Player.CurrentFrame = 10;

            sheet.Paste();

            Assert.That(Angle(editor.Rider.AnimationClip!.DynamicFrames[10].Rotation[shin], copied), Is.LessThan(1e-4f));
        }

        // The strip's geometry: a click lands on the frame and row it is drawn over.
        [Test]
        public void Layout_ClicksLandOnTheFrameAndRowTheyAreOver()
        {
            var layout = KeyframeSheetLayout.Fit(width: 1000, frameCount: 40);
            Assert.That(layout.FrameWidth, Is.EqualTo((1000 - KeyframeSheetLayout.HeaderWidth) / 40));
            for (var frame = 0; frame < 40; frame++)
                Assert.That(layout.FrameAt(layout.FrameCentre(frame), 40), Is.EqualTo(frame));
            Assert.That(layout.RowAt(KeyframeSheetLayout.RulerHeight / 2), Is.EqualTo(KeyframeSheetLayout.Ruler));
            Assert.That(layout.RowAt(KeyframeSheetLayout.RulerHeight + 1.5 * KeyframeSheetLayout.RowHeight), Is.EqualTo(1));
            Assert.That(layout.FrameAt(KeyframeSheetLayout.HeaderWidth / 2, 40), Is.EqualTo(KeyframeSheetLayout.Header));
            Assert.That(layout.FrameAt(5000, 40), Is.EqualTo(39), "past the end is the last frame, so a drag can run off the strip");
        }
    }
}
