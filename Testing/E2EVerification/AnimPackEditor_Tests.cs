using CommonControls.Editors.AnimationPack;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
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
