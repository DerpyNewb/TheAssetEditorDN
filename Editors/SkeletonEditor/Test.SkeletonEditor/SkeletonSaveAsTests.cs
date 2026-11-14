using Editor.VisualSkeletonEditor.SkeletonEditor;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Core.Commands;
using Shared.GameFormats.Animation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.SkeletonEditor
{
    // The Skeleton editor saved only over the file it opened: no Save as, and the path was a read-only label.
    internal class SkeletonSaveAsTests
    {
        const string Karl = "animations\\skeletons\\humanoid01.anim";

        static (AssetEditorTestRunner, SkeletonEditorViewModel) Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(Karl)!, EditorEnums.VisualSkeletonEditor);
            return (runner, runner.GetRequiredServiceInCurrentEditorScope<SkeletonEditorViewModel>());
        }

        static PackFile? InEditablePack(AssetEditorTestRunner runner, string path) => runner.PackFileService.FindFile(path, runner.PackFileService.GetEditablePack());

        [Test]
        public void SaveAs_WritesTheSkeletonAndItsMatricesToThePickedPath_AndCarriesOnThere()
        {
            var (runner, editor) = Open();
            runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>())).Returns(new SaveDialogResult(true, null, @"animations\skeletons\derpy_copy"));

            editor.SaveSkeletonAsAction();

            var saved = InEditablePack(runner, @"animations\skeletons\derpy_copy.anim");
            Assert.That(saved, Is.Not.Null, "Save as wrote no .anim at the picked path");
            Assert.That(InEditablePack(runner, @"animations\skeletons\derpy_copy.bone_inv_trans_mats"), Is.Not.Null);
            Assert.That(AnimationFile.Create(saved!.DataSource.ReadDataAsChunk()).Bones.Length, Is.EqualTo(72));
            Assert.That(editor.SkeletonName, Is.EqualTo(@"animations\skeletons\derpy_copy.anim"));
            Assert.That(editor.DisplayName, Is.EqualTo("derpy_copy.anim"));
            Assert.That(editor.CurrentFile, Is.SameAs(saved));
        }

        [Test]
        public void SaveAs_Cancelled_WritesNothing()
        {
            var (runner, editor) = Open();
            var editableFiles = runner.PackFileService.GetEditablePack()!.GetFileCount();
            runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>())).Returns(new SaveDialogResult(false, null, null));

            editor.SaveSkeletonAsAction();

            Assert.That(runner.PackFileService.GetEditablePack()!.GetFileCount(), Is.EqualTo(editableFiles));
            Assert.That(editor.SkeletonName, Is.EqualTo(Karl));
        }
    }
}
