using Editors.AnimationMeta.Presentation;
using Shared.Core.Commands;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.Ui.Common.Behaviors;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // The meta editor had no undo. A scrub box's whole drag undoes as one step, an add, delete, move or paste as one, and redo
    // puts it back.
    public class MetaDataEditorUndoTests
    {
        const string FilePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";

        static MetaDataEditorViewModel Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var metaPackFile = runner.PackFileService.FindFile(FilePath);
            return runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
        }

        static SplashAttack_v10 Splash(MetaDataEditorViewModel editor) => (SplashAttack_v10)editor.ParsedFile!.Attributes[4];

        // SplashAttack_v10's AoeShape, an int field, dragged through 1 to 5.
        static void DragAoeShape(MetaDataEditorViewModel editor, int from)
        {
            editor.SelectedTag = editor.Tags[4];
            ScrubGesture.Start();
            try
            {
                for (var value = from; value < from + 5; value++)
                    editor.SelectedTag!.Variables[5].ValueAsString = value.ToString();
            }
            finally
            {
                ScrubGesture.Stop();
            }
        }

        [Test]
        public void AScrubDrag_UndoesInOneStep_AndRedoes()
        {
            var editor = Open();
            var original = Splash(editor).AoeShape;
            Assert.That(editor.UndoCommand.CanExecute(null), Is.False, "nothing to undo as loaded");

            DragAoeShape(editor, 11);
            Assert.That(Splash(editor).AoeShape, Is.EqualTo(15));

            editor.UndoCommand.Execute(null);
            Assert.That(Splash(editor).AoeShape, Is.EqualTo(original));
            Assert.That(editor.UndoCommand.CanExecute(null), Is.False, "the whole drag was one step");
            Assert.That(editor.Tags[4].Variables[5].ValueAsString, Is.EqualTo(original.ToString()), "the box shows it");
            Assert.That(editor.SelectedTag, Is.SameAs(editor.Tags[4]), "the same tag stays selected");

            editor.RedoCommand.Execute(null);
            Assert.That(Splash(editor).AoeShape, Is.EqualTo(15));
        }

        [Test]
        public void TwoDrags_AreTwoSteps()
        {
            var editor = Open();
            var original = Splash(editor).AoeShape;

            DragAoeShape(editor, 11);
            DragAoeShape(editor, 21);

            editor.UndoCommand.Execute(null);
            Assert.That(Splash(editor).AoeShape, Is.EqualTo(15));
            editor.UndoCommand.Execute(null);
            Assert.That(Splash(editor).AoeShape, Is.EqualTo(original));
        }

        [Test]
        public void ADelete_Undoes_AndANewEditDropsTheRedo()
        {
            var editor = Open();
            var count = editor.ParsedFile!.Attributes.Count;
            editor.Tags[4].IsSelected = true;
            editor.DeleteActionCommand.Execute(null);
            Assert.That(editor.ParsedFile!.Attributes, Has.Count.EqualTo(count - 1));

            editor.UndoCommand.Execute(null);
            Assert.That(editor.ParsedFile!.Attributes, Has.Count.EqualTo(count));
            Assert.That(editor.ParsedFile!.Attributes[4], Is.InstanceOf<SplashAttack_v10>());
            Assert.That(editor.HasUnsavedChanges, Is.True, "an undo is a change from what is saved");

            DragAoeShape(editor, 11);
            Assert.That(editor.RedoCommand.CanExecute(null), Is.False);
        }
    }
}
