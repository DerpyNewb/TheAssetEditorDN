using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Editors.VfxEditor.ViewModels;
using Editors.VfxEditor.Views;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.VfxEditor
{
    // Opens a vfx file the way the app does (its DI container, editor database and game window) and drives the tab.
    class VfxEditorAppTests
    {
        [Test]
        public void A_vfx_file_opens_in_the_editor_takes_a_linked_edit_and_saves()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            var output = runner.CreateOutputPack();

            var editor = (VfxEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute("vfx\\fx.xml", EditorEnums.Vfx_Editor);
            Assert.That(editor.Tree.Select(n => n.Label), Is.EqualTo(new[] { "Effect", "Emitters", "References" }));

            editor.SelectedNode = editor.Tree[1].Children[0];
            string Value(string label) => editor.Rows.Single(r => r.Label == label).Value;
            Assert.That(Value("A"), Is.EqualTo("1"));
            editor.Rows.Single(r => r.Label == "A").Value = "2";   // Ultra and High held 1, Medium 5
            Assert.That(editor.HasUnsavedChanges, Is.True);
            editor.Tier = 1;
            Assert.That(Value("A"), Is.EqualTo("2"));
            editor.Tier = 2;
            Assert.That(Value("A"), Is.EqualTo("5"));

            Assert.That(editor.Save(), Is.True);
            var saved = runner.PackFileService.FindFile("vfx\\fx.xml", output)!.DataSource.ReadData();
            Assert.That(Encoding.Unicode.GetString(saved, 2, saved.Length - 2), Is.EqualTo(Fixtures.Default
                .Replace("\"A\" quality=\"0\" value=\"1\"", "\"A\" quality=\"0\" value=\"2\"")
                .Replace("\"A\" quality=\"1\" value=\"1\"", "\"A\" quality=\"1\" value=\"2\"")));
            Assert.That(editor.HasUnsavedChanges, Is.False);
        }

        // A text box writes its value when it loses focus, and the toolbar takes no focus from it (a ToolBar is a focus
        // scope), nor does Ctrl+S. Picking a tier or saving must still take the typed value, at the tier it was typed at.
        [Test, Apartment(ApartmentState.STA)]
        public void A_typed_value_lands_before_the_tier_changes_and_before_a_save()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            var output = runner.CreateOutputPack();
            var editor = (VfxEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute("vfx\\fx.xml", EditorEnums.Vfx_Editor);
            var view = new VfxEditorView { DataContext = editor };
            editor.SelectedNode = editor.Tree[1].Children[0];
            TextBox Box()
            {
                view.Measure(new Size(1600, 900));
                view.Arrange(new Rect(0, 0, 1600, 900));
                view.UpdateLayout();
                return Descendants(view).OfType<TextBox>().First(b => b.DataContext is PropertyRow { Label: "A" });
            }

            Box().Text = "2";
            Assert.That(editor.HasUnsavedChanges, Is.False);   // typed, not yet written
            editor.Tier = 1;                                     // what picking High does
            Assert.That(editor.Rows.Single(r => r.Label == "A").Value, Is.EqualTo("2"));

            Box().Text = "3";
            editor.SaveFileCommand.Execute(null);
            var saved = runner.PackFileService.FindFile("vfx\\fx.xml", output)!.DataSource.ReadData();
            Assert.That(Encoding.Unicode.GetString(saved, 2, saved.Length - 2),
                Does.Contain("<property name=\"A\" quality=\"0\" value=\"3\"/>\r\n\t\t\t<property name=\"A\" quality=\"1\" value=\"3\"/>"));
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
