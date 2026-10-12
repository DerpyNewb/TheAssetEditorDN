using Editors.AnimationMeta.Presentation;
using Shared.Core.Commands;
using Shared.Ui.Common.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Editors.AnimationMeta.Presentation.View;
using Microsoft.Xaml.Behaviors;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    public class MetaDataEditorViewModelTests
    {
        [Test]
        public void MetaDataEditor_OpenAndVerify()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
          
            Assert.That(editor.ParsedFile, Is.Not.Null);
            Assert.That(editor.ParsedFile.Attributes.Count, Is.EqualTo(7));
            Assert.That(editor.ParsedFile.Attributes[0], Is.InstanceOf<AnimatedProp_v14>());
            Assert.That(editor.ParsedFile.Attributes[4], Is.InstanceOf<SplashAttack_v10>());

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);
        }


        [Test]
        public void MetaDataEditor_OpenModifyAndSave()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);


            //SplashAttack_v10 - Filter - string
            var strValue = "customstr";
            editor.Tags[4].Variables[3].ValueAsString = strValue;

            //SplashAttack_v10 - AoeShape - int
            var intValue = 8;
            editor.Tags[4].Variables[5].ValueAsString = intValue.ToString();

            //SplashAttack_v10 - EndPosition - Vector 3
            var vectorValue = 120;
            (editor.Tags[4].Variables[7] as VectorAttributeViewModel)!.Value.X.Value = vectorValue;

            // HasUnsavedChanges should be signaled via events in superview, but editor itself doesn't track it automatically.
            // Ensure saving works and the saved file exists in output pack
            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);

            // Reload the file and verify
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);

            var splashAttack = parsedFile.Attributes[4] as SplashAttack_v10;
            Assert.That(splashAttack, Is.Not.Null);
            Assert.That(splashAttack.Filter, Is.EqualTo(strValue));
            Assert.That(splashAttack.AoeShape, Is.EqualTo(intValue));
            Assert.That(splashAttack.EndPosition.X, Is.EqualTo(vectorValue));
        }

        // A changed file shows red on its tab and asks before it closes, so every change counts until it is saved: an edited field,
        // and an entry added, deleted or moved. A field's mark lived on its row, which every add, delete, move and paste rebuilds,
        // so one of them forgot the edits before it, and the tab never heard that anything had changed.
        [Test]
        public void MetaDataEditor_EveryChangeIsUnsavedUntilSaved()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var metaPackFile = runner.PackFileService.FindFile(@"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta");
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
            // What the tab reads each time it is told to look.
            var said = new List<bool>();
            editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.HasUnsavedChanges)) said.Add(editor.HasUnsavedChanges); };
            Assert.That(editor.HasUnsavedChanges, Is.False, "as loaded");

            editor.Tags[4].Variables[3].ValueAsString = "customstr";
            Assert.That(said, Is.EqualTo(new[] { true }), "a field edited");

            editor.Tags[0].IsSelected = true;
            editor.MoveDownActionCommand.Execute(null);
            editor.MoveUpActionCommand.Execute(null);
            Assert.That(editor.HasUnsavedChanges, Is.True, "the edit outlives the rows a move and its undoing rebuild");

            editor.SaveActionCommand.Execute(null);
            Assert.That(editor.HasUnsavedChanges, Is.False, "saved");

            said.Clear();
            (editor.Tags[4].Variables[7] as VectorAttributeViewModel)!.Value.X.Value = 120;
            Assert.That(said, Is.Not.Empty.And.All.True, "a vector edited: " + string.Join(",", said));

            editor.SaveActionCommand.Execute(null);
            said.Clear();
            editor.Tags.Last().IsSelected = true;
            editor.DeleteActionCommand.Execute(null);
            Assert.That(said, Is.Not.Empty.And.All.True, "an entry deleted");
        }

        [Test]
        public void MetaDataEditor_DeleteAndSave()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);
            var initialCount = editor.Tags.Count;
            Assert.That(initialCount, Is.GreaterThan(0));

            // Select last tag and delete
            editor.Tags.Last().IsSelected = true;
            editor.DeleteActionCommand.Execute(null);

            Assert.That(editor.Tags.Count, Is.EqualTo(initialCount - 1));

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);
           
            // Reload the file and verify
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);
            Assert.That(parsedFile.Attributes.Count, Is.EqualTo(initialCount - 1));
        }

        [Test]
        public void MetaDataEditor_MoveUpAndSave()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);

            // Move second entry up
            editor.Tags[4].IsSelected = true;
            editor.MoveUpActionCommand.Execute(null);

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);

            // Reload the file and verify
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);
            Assert.That(parsedFile.Attributes[3], Is.InstanceOf<SplashAttack_v10>());
        }

        [Test]
        public void MetaDataEditor_CopyPaste_AddsNewTag()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);

            var initialCount = editor.Tags.Count;
            Assert.That(initialCount, Is.GreaterThan(0));

            // Select a known tag (SplashAttack at index 4) and copy
            var indexToCopy = 4;
            var originalType = editor.ParsedFile.Attributes[indexToCopy].GetType();

            editor.Tags[indexToCopy].IsSelected = true;
            editor.CopyActionCommand.Execute(null);

            // Paste
            editor.PasteActionCommand.Execute(null);

            // View should be updated
            Assert.That(editor.Tags.Count, Is.EqualTo(initialCount + 1));
            Assert.That(editor.ParsedFile.Attributes.Count, Is.EqualTo(initialCount + 1));

            var pasted = editor.ParsedFile.Attributes.Last();
            Assert.That(pasted, Is.InstanceOf(originalType));
        }

        [Test]
        public void MetaDataEditor_CopyPaste_EditPastedTagAndSave_UsesEditedValues()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);

            var sourceIndex = 4;
            editor.Tags[sourceIndex].IsSelected = true;
            editor.CopyActionCommand.Execute(null);
            editor.PasteActionCommand.Execute(null);

            var pastedIndex = editor.Tags.Count - 1;
            Assert.That(editor.ParsedFile.Attributes[pastedIndex], Is.InstanceOf<SplashAttack_v10>());

            // Edit the pasted tag values, not the original source tag.
            var editedFilter = "edited_after_paste";
            var editedAoeShape = 13;
            var editedEndPositionX = 777;

            editor.Tags[pastedIndex].Variables[3].ValueAsString = editedFilter;
            editor.Tags[pastedIndex].Variables[5].ValueAsString = editedAoeShape.ToString();
            (editor.Tags[pastedIndex].Variables[7] as VectorAttributeViewModel)!.Value.X.Value = editedEndPositionX;

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);

            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);

            var pastedSplashAttack = parsedFile.Attributes[pastedIndex] as SplashAttack_v10;
            Assert.That(pastedSplashAttack, Is.Not.Null);
            Assert.That(pastedSplashAttack.Filter, Is.EqualTo(editedFilter));
            Assert.That(pastedSplashAttack.AoeShape, Is.EqualTo(editedAoeShape));
            Assert.That(pastedSplashAttack.EndPosition.X, Is.EqualTo(editedEndPositionX));
        }

        // A field drags as a number only when it is one: whole for an int, decimal for a float, and not at all for a string.
        [Test]
        public void MetaDataEditor_OnlyNumberFieldsScrub()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var metaPackFile = runner.PackFileService.FindFile(@"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta");
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            var splash = editor.Tags[4].Variables.ToDictionary(x => x.FieldName, x => x.Scrub);
            Assert.That((splash["Filter"], splash["Group Sound"], splash["Aoe Shape"], splash["Start Time"], splash["Impact Mass In Kg"]),
                Is.EqualTo((ScrubMode.None, ScrubMode.None, ScrubMode.Integer, ScrubMode.Decimal, ScrubMode.Decimal)));
        }

        // ...and the panel's boxes drag that way: a drag on Start Time's box and on End Position's X box writes the tag, Aoe Shape's
        // moves in whole steps, and Filter's box does not drag at all.
        [Test, Apartment(ApartmentState.STA)]
        public void MetaDataEditor_DraggingANumberBoxWritesTheTag()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var metaPackFile = runner.PackFileService.FindFile(@"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta");
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
            editor.SelectedTag = editor.Tags[4];
            var splash = (SplashAttack_v10)editor.ParsedFile!.Attributes[4];
            var (startTime, endX, aoe) = (splash.StartTime, splash.EndPosition.X, splash.AoeShape);

            // The panel's one app-level resource, which the test has no App.xaml to give it.
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["InvBoolToHiddenConverter"] = new BooleanToVisibilityConverter();
            var view = new MetaDataAttributeView { DataContext = editor };
            view.Measure(new Size(900, 700));
            view.Arrange(new Rect(0, 0, 900, 700));
            view.UpdateLayout();
            var boxes = Descendants(view).OfType<TextBox>().ToList();
            NumberScrubBehavior Scrub(TextBox box) => Interaction.GetBehaviors(box).OfType<NumberScrubBehavior>().Single();
            NumberScrubBehavior Field(string name) => Scrub(boxes.First(b => b.DataContext is AttributeViewModel a && a.FieldName == name));
            void Drag(NumberScrubBehavior scrub)
            {
                Assert.That(scrub.Begin(0), Is.True);
                scrub.Move(100, ModifierKeys.None);
                scrub.End();
            }

            Drag(Field("Start Time"));
            Drag(Field("Aoe Shape"));
            // Vector3View's three boxes, X first, sit in End Position's row.
            Drag(Scrub(boxes.First(b => b.DataContext is VectorAttributeViewModel { FieldName: "End Position" })));
            Assert.Multiple(() =>
            {
                Assert.That(Field("Filter").Begin(0), Is.False);
                Assert.That(splash.StartTime, Is.EqualTo(startTime + Math.Max(0.01f, Math.Abs(startTime) * 0.01f) * 100).Within(0.001));
                Assert.That(splash.EndPosition.X, Is.EqualTo(endX + Math.Max(0.01f, Math.Abs(endX) * 0.01f) * 100).Within(0.001));
                Assert.That(splash.AoeShape, Is.EqualTo((int)Math.Round(aoe + Math.Max(0.1, Math.Abs(aoe) * 0.01) * 100, MidpointRounding.AwayFromZero)));
            });
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
