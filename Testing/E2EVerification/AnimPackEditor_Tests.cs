using CommonControls.Editors.AnimationPack;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // The animation pack editor had no undo: a removed table, a rename or an applied text edit was only taken back by closing
    // without saving, losing everything else.
    internal class AnimPackEditor_Tests
    {
        const string GraphA = "animations/locomotion_graphs/derpy_a.xml";
        const string GraphB = "animations/locomotion_graphs/derpy_b.xml";

        // No slots: applying text runs the converter's checks, and a slot naming any file this test pack lacks is a warning, which
        // opens a window. The skeleton is Karl's pack's, and both graphs are in the pack, so the edit below passes them all.
        static AnimationBinWh3 Table(string name) => new($"animations/database/battle/bin/{name}.bin")
        {
            Name = name,
            SkeletonName = "humanoid01",
            LocomotionGraph = GraphA,
        };

        static AnimPackViewModel Open(AssetEditorTestRunner runner)
        {
            var animPack = new AnimationPackFileDatabase("derpy_test.animpack");
            animPack.AddFile(Table("derpy_a"));
            animPack.AddFile(Table("derpy_b"));
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [
                new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes("derpy_test.animpack", AnimationPackSerializer.ConvertToBytes(animPack))),
                new NewPackFileEntry(@"animations\locomotion_graphs", PackFile.CreateFromBytes("derpy_a.xml", [0])),
                new NewPackFileEntry(@"animations\locomotion_graphs", PackFile.CreateFromBytes("derpy_b.xml", [0]))]);
            // The editor is transient: the instance the command opened, not a new one from the scope.
            return (AnimPackViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(@"animations\database\battle\bin\derpy_test.animpack")!, EditorEnums.AnimationPack_Editor);
        }

        static List<string> Names(AnimPackViewModel editor) => editor.AnimationPackItems.PossibleValues.Select(x => x.FileName).ToList();

        const string AnimPackPath = @"animations\database\battle\bin\derpy_test.animpack";

        // Another editor saving into the animpack, as SuperView's slot grid does: derpy_a now names the other graph.
        static void SaveElsewhere(AssetEditorTestRunner runner)
        {
            var pfs = runner.PackFileService;
            var file = pfs.FindFile(AnimPackPath)!;
            var animPack = AnimationPackSerializer.Load(file, pfs);
            animPack.Files.OfType<AnimationBinWh3>().Single(x => x.FileName.EndsWith("derpy_a.bin")).LocomotionGraph = GraphB;
            pfs.SaveFile(file, AnimationPackSerializer.ConvertToBytes(animPack));
        }

        static string GraphOfA(AnimPackViewModel editor) =>
            editor.AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Single(x => x.FileName.EndsWith("derpy_a.bin")).LocomotionGraph;

        // The editor writes its whole copy back on save. Unedited, it reads the animpack again when another editor saves into it, and
        // shows the table it showed, as read again. Another file saved in the same pack is not its business.
        [Test]
        public void AnUneditedEditor_ReadsTheAnimpackAgain_WhenAnotherEditorSavesIt()
        {
            var runner = new AssetEditorTestRunner();
            var editor = Open(runner);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.Single(x => x.FileName.EndsWith("derpy_a.bin"));
            var listed = editor.AnimationPackItems.PossibleValues.ToList();
            runner.PackFileService.SaveFile(runner.PackFileService.FindFile(@"animations\locomotion_graphs\derpy_b.xml")!, [1]);
            Assert.That(editor.AnimationPackItems.PossibleValues, Is.EqualTo(listed), "another file's save read the animpack again");

            SaveElsewhere(runner);
            var shown = editor.AnimationPackItems.SelectedItem as AnimationBinWh3;
            Assert.That((shown?.FileName, shown?.LocomotionGraph, editor.AnimationPackItems.PossibleValues.Contains(shown!), editor.HasUnsavedChanges),
                Is.EqualTo((listed[0].FileName, GraphB, true, false)));
        }

        // Edited, it keeps its edits, and its save asks before writing them over the other editor's change: No leaves the file as the
        // other editor saved it, Yes writes the editor's copy. Its own save is not another editor's, so the next one does not ask.
        [Test]
        public void AnEditedEditor_AsksBeforeItsSaveWritesOverAnotherEditorsSave()
        {
            var runner = new AssetEditorTestRunner();
            var editor = Open(runner);
            var (asked, answer) = (new List<string>(), ShowMessageBoxResult.Cancel);
            runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((message, _) =>
                {
                    if (!message.StartsWith("Another editor saved"))
                        return ShowMessageBoxResult.OK;
                    asked.Add(message);
                    return answer;
                });
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues[1];
            editor.RemoveActionCommand.Execute(null);

            SaveElsewhere(runner);
            var file = runner.PackFileService.FindFile(AnimPackPath)!;
            var theirs = file.DataSource.ReadData();
            Assert.That((Names(editor).Count, GraphOfA(editor)), Is.EqualTo((1, GraphA)), "the edit was dropped for the other editor's save");
            Assert.That((editor.Save(), asked.Count, file.DataSource.ReadData()), Is.EqualTo((false, 1, theirs)));

            answer = ShowMessageBoxResult.OK;
            Assert.That((editor.Save(), asked.Count), Is.EqualTo((true, 2)));
            var saved = AnimationPackSerializer.Load(runner.PackFileService.FindFile(AnimPackPath)!, runner.PackFileService);
            Assert.That(saved.Files.Select(x => x.FileName), Is.EqualTo(Names(editor)));

            editor.UndoAction();
            Assert.That((editor.HasUnsavedChanges, editor.Save(), asked.Count), Is.EqualTo((true, true, 2)));
        }

        [Test]
        public void ARemovedTable_ComesBackWhereItWas_AndGoesAgainOnRedo()
        {
            var editor = Open(new AssetEditorTestRunner());
            var loaded = Names(editor);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues[0];

            editor.RemoveActionCommand.Execute(null);
            Assert.That((Names(editor), editor.HasUnsavedChanges), Is.EqualTo((loaded.Skip(1).ToList(), true)));

            editor.UndoAction();
            Assert.That(Names(editor), Is.EqualTo(loaded));
            editor.RedoAction();
            Assert.That(Names(editor), Is.EqualTo(loaded.Skip(1).ToList()));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void AnAppliedTextEdit_IsUndoneOnTheTableAndInTheText()
        {
            var editor = Open(new AssetEditorTestRunner());
            var table = editor.AnimationPackItems.PossibleValues[0];
            editor.AnimationPackItems.SelectedItem = table;
            var before = table.ToByteArray();
            var text = editor.SelectedItemViewModel.Text;
            Assert.That(text, Does.Contain(GraphA));

            editor.SelectedItemViewModel.Text = text.Replace(GraphA, GraphB);
            Assert.That(editor.SaveActiveFile(), Is.True);
            var after = table.ToByteArray();
            Assert.That(after, Is.Not.EqualTo(before));

            editor.UndoAction();
            Assert.That((table.ToByteArray(), editor.SelectedItemViewModel.Text), Is.EqualTo((before, text)));
            editor.RedoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(after));
        }
    }
}
