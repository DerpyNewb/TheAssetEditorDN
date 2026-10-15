using Editor.VisualSkeletonEditor.SkeletonEditor;
using GameWorld.Core.Animation;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.SkeletonEditor
{
    // The skeleton editor had no undo: a rename, a move or a deleted bone could only be taken back by reloading the file.
    internal class SkeletonUndoTests
    {
        static string State(GameSkeleton skeleton) => string.Join("\n", Enumerable.Range(0, skeleton.BoneCount).Select(i =>
            $"{skeleton.BoneNames[i]} {skeleton.GetParentBoneIndex(i)} {skeleton.Translation[i]} {skeleton.Rotation[i]} {skeleton.Scale[i]}"));

        static int Count(IEnumerable<Editors.Shared.Core.Common.ReferenceModel.SkeletonBoneNode> bones) => bones.Sum(x => 1 + Count(x.Children));

        [Test]
        public void EachEdit_IsUndoneAndRedoneInTurn()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile("animations\\skeletons\\humanoid01.anim"), EditorEnums.VisualSkeletonEditor);
            var editor = runner.GetRequiredServiceInCurrentEditorScope<SkeletonEditorViewModel>();
            var skeleton = editor.SceneObjects.First().Data.Skeleton;
            var loaded = State(skeleton);

            editor.SelectedBone = SkeletonToolHelpers.GetSkeletonNodeFromName("upperleg_left", editor.Bones);
            editor.SelectedBoneName = "renamed";
            var renamed = State(skeleton);
            editor.SelectedBoneTranslationOffset.Z.Value = 10;
            editor.SelectedBoneTranslationOffset.Z.Value = 11;
            var moved = State(skeleton);
            editor.SelectedBone = SkeletonToolHelpers.GetSkeletonNodeFromName("clav_right", editor.Bones);
            editor.DeleteBoneAction();
            var deleted = State(skeleton);

            editor.UndoAction();
            Assert.That(State(skeleton), Is.EqualTo(moved), "the delete");
            Assert.That(Count(editor.Bones), Is.EqualTo(skeleton.BoneCount), "the bone list follows");
            editor.UndoAction();
            Assert.That(State(skeleton), Is.EqualTo(renamed), "both moves, typed one after the other, as one step");
            editor.UndoAction();
            Assert.That(State(skeleton), Is.EqualTo(loaded), "the rename");
            editor.UndoAction();
            Assert.That(State(skeleton), Is.EqualTo(loaded), "nothing left to undo");

            editor.RedoAction();
            editor.RedoAction();
            editor.RedoAction();
            Assert.That(State(skeleton), Is.EqualTo(deleted));
            Assert.That(editor.SceneObjects.First().Data.Skeleton, Is.SameAs(skeleton), "restored in place, so the scene still draws this skeleton");
        }

        // Picking another bone sets the name box; that is no edit and leaves nothing to undo.
        [Test]
        public void PickingABone_IsNoStep()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile("animations\\skeletons\\humanoid01.anim"), EditorEnums.VisualSkeletonEditor);
            var editor = runner.GetRequiredServiceInCurrentEditorScope<SkeletonEditorViewModel>();
            var skeleton = editor.SceneObjects.First().Data.Skeleton;

            editor.SelectedBone = SkeletonToolHelpers.GetSkeletonNodeFromName("upperleg_left", editor.Bones);
            editor.SelectedBoneName = "renamed";
            var renamed = State(skeleton);
            editor.SelectedBone = SkeletonToolHelpers.GetSkeletonNodeFromName("clav_right", editor.Bones);
            editor.SelectedBone = SkeletonToolHelpers.GetSkeletonNodeFromName("renamed", editor.Bones);

            editor.UndoAction();

            Assert.That(State(skeleton), Is.Not.EqualTo(renamed), "the one undo went to the rename, not to a bone pick");
        }
    }
}
