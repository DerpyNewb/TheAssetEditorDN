using System.Text;
using Editors.VfxEditor.ViewModels;
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
    }
}
