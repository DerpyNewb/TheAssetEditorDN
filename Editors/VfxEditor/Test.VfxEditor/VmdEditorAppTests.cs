using System.Text;
using Editors.VfxEditor.VariantMesh;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.VfxEditor
{
    // Opens a variant mesh file the way the app does (its DI container and editor database) and drives the grid's view model.
    class VmdEditorAppTests
    {
        const string Folder = "variantmeshes\\variantmeshdefinitions";
        internal const string FilePath = Folder + "\\unit.variantmeshdefinition";

        internal static (AssetEditorTestRunner Runner, VmdEditorViewModel Editor) Open(string text)
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry(Folder, PackFile.CreateFromBytes("unit.variantmeshdefinition", Encoding.UTF8.GetBytes(text)))]);
            runner.CreateOutputPack();
            var editor = (VmdEditorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(FilePath, EditorEnums.VariantMesh_Editor);
            return (runner, editor);
        }

        static string Text(VmdEditorViewModel editor) => Encoding.UTF8.GetString(editor.Document!.Save());

        [Test]
        public void The_editor_database_offers_the_grid_for_a_variant_mesh()
        {
            var runner = new AssetEditorTestRunner();
            var database = (IEditorDatabase)runner.ServiceProvider.GetService(typeof(IEditorDatabase))!;
            var info = database.GetEditorInfos().Single(x => x.EditorEnum == EditorEnums.VariantMesh_Editor);
            Assert.That(info.Extensions.Select(x => (x.Extention, x.Priority)), Is.EqualTo(new[] { (".variantmeshdefinition", EditorPriorites.High + 1) }));
        }

        // A double-click asks which editor; the grid is the one picked already, so Enter opens it. The app's own editors, in a
        // database whose chooser records what it was offered.
        [Test]
        public void The_chooser_offers_the_grid_first()
        {
            var runner = new AssetEditorTestRunner();
            var registered = ((IEditorDatabase)runner.ServiceProvider.GetService(typeof(IEditorDatabase))!).GetEditorInfos();
            List<EditorEnums>? offered = null;
            var chooser = new Mock<IToolSelectorUiProvider>();
            chooser.Setup(x => x.CreateAndShow(It.IsAny<IEnumerable<EditorEnums>>()))
                .Callback<IEnumerable<EditorEnums>>(x => offered = x.ToList()).Returns(EditorEnums.None);
            var database = new EditorDatabase(Mock.Of<IScopeRepository>(), chooser.Object);
            foreach (var info in registered)
                database.Register(info);

            database.Create(FilePath, null);

            Assert.That(offered, Is.EqualTo(new[] { EditorEnums.VariantMesh_Editor, EditorEnums.XML_VariantMesh_Editor, EditorEnums.Kitbash_Editor, EditorEnums.SuperView_Editor }));
        }

        [Test]
        public void A_cell_edit_saves_to_the_editable_pack()
        {
            var (runner, editor) = Open(VmdFixtures.Unit);
            Assert.That(editor.Rows.Select(r => r.Slot), Is.EqualTo(new[] { "body", "weapon_1", "weapon_2" }));
            Assert.That((editor.DisplayName, editor.HasUnsavedChanges, editor.LoadError), Is.EqualTo(("unit.variantmeshdefinition", false, (string?)null)));

            editor.Rows[1].Path = "b\\axe.wsmodel";
            Assert.That(editor.HasUnsavedChanges, Is.True);
            Assert.That(editor.Save(), Is.True);

            var saved = runner.PackFileService.FindFile(FilePath, runner.PackFileService.GetEditablePack())!.DataSource.ReadData();
            Assert.That(Encoding.UTF8.GetString(saved), Is.EqualTo(VmdFixtures.Unit.Replace("a\\sword.wsmodel", "b\\axe.wsmodel")));
            Assert.That(editor.HasUnsavedChanges, Is.False);
        }

        [Test]
        public void Undo_restores_the_file_and_the_rows()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.AddSlotCommand.Execute(null);
            Assert.That(editor.Rows, Has.Count.EqualTo(4));

            editor.UndoCommand.Execute(null);
            Assert.That(editor.Rows.Select(r => r.Slot), Is.EqualTo(new[] { "body", "weapon_1", "weapon_2" }));
            Assert.That(Text(editor), Is.EqualTo(VmdFixtures.Unit));

            editor.RedoCommand.Execute(null);
            Assert.That(editor.Rows, Has.Count.EqualTo(4));
        }

        [Test]
        public void Add_slot_goes_after_the_selected_slot_and_selects_the_new_row()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.SelectedRows = [editor.Rows[1]];
            editor.AddSlotCommand.Execute(null);

            Assert.That(editor.Rows.Select(r => r.Slot), Is.EqualTo(new[] { "body", "weapon_1", "slot_1", "weapon_2" }));
            Assert.That(editor.SelectedRows, Is.EqualTo(new[] { editor.Rows[2] }));
        }

        [Test]
        public void The_selection_follows_its_rows_through_an_edit_elsewhere()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.SelectedRows = [editor.Rows[2]];
            editor.RemoveEntryCommand.Execute(editor.Rows[1]);   // weapon_1 keeps its row, as an empty slot

            Assert.That(editor.Rows.Select(r => r.Kind), Is.EqualTo(new[] { "model", "", "" }));
            Assert.That(editor.SelectedRows.Select(r => r.Slot), Is.EqualTo(new[] { "weapon_2" }));
        }

        [Test]
        public void A_bad_probability_is_marked_and_not_written()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            var row = editor.Rows[2];
            row.Probability = "0,6";
            Assert.That((row.ProbabilityInvalid, row.Probability, editor.HasUnsavedChanges), Is.EqualTo((true, "0,6", false)));

            row.Probability = "0.6";
            Assert.That((editor.Rows[2].ProbabilityInvalid, editor.Rows[2].Probability), Is.EqualTo((false, "0.6")));
            Assert.That(Text(editor), Does.Contain("probability=\"0.6\""));
        }

        [Test]
        public void Tags_are_typed_as_a_comma_list()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            Assert.That(editor.Rows[0].Tags, Is.EqualTo("audio_entity_type:x, audio_armour_type:leather"));
            Assert.That((editor.Rows[0].HasTags, editor.Rows[2].HasTags), Is.EqualTo((true, false)));

            editor.Rows[0].Tags = " audio_entity_type:x,,equipment ";
            Assert.That(Text(editor), Does.Contain("<META_DATA>audio_entity_type:x</META_DATA>\t\t\t<META_DATA>equipment</META_DATA></VARIANT_MESH>"));
        }

        [Test]
        public void Remove_of_a_slot_with_a_slot_inside_it_removes_it_once()
        {
            var (_, editor) = Open(VmdFixtures.Gyrobomber);
            editor.SelectedRows = [editor.Rows[1], editor.Rows[2], editor.Rows[4], editor.Rows[5]];   // pilot, pilot / body, helmet twice
            editor.RemoveSlotsCommand.Execute(null);

            Assert.That(editor.Rows.Select(r => r.Slot), Is.EqualTo(new[] { "body", "weapon_1" }));
            Assert.That(editor.Status, Is.EqualTo("Removed 1 slot"));   // the others were inside it
        }

        [Test]
        public void Paste_of_text_that_is_not_xml_says_so_and_changes_nothing()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.PasteText("nope <");
            Assert.That(editor.Status, Does.Contain("not XML"));
            Assert.That(editor.HasUnsavedChanges, Is.False);
        }

        [Test]
        public void Copy_and_paste_a_slot_selects_the_copy()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.SelectedRows = [editor.Rows[1]];
            var copied = editor.CopyText();
            editor.PasteText(copied);

            Assert.That(editor.Rows.Select(r => r.Slot), Is.EqualTo(new[] { "body", "weapon_1", "weapon_1", "weapon_2" }));
            Assert.That(editor.SelectedRows, Is.EqualTo(new[] { editor.Rows[2] }));
            Assert.That(editor.Rows[1].Findings, Does.Contain("also named"));
        }

        // Final review: a block selected from a slot down through its inner slots copied the inner ones twice.
        [Test]
        public void Copy_of_a_slot_with_its_inner_slots_selected_copies_it_once()
        {
            var (_, editor) = Open(VmdFixtures.Gyrobomber);
            editor.SelectedRows = editor.Rows.Skip(1).Take(6).ToList();   // pilot down to the last pilot / helmet

            var copied = editor.CopyText();

            Assert.That(copied, Does.StartWith("<SLOT name=\"pilot\" >").And.EndWith("</SLOT>"));
            Assert.That(copied.Split("<SLOT name=\"helmet\"").Length, Is.EqualTo(2));
        }

        // Final review: a whole file copied out of the text editor went into the selected slot as one model-less mesh.
        [Test]
        public void Paste_of_a_whole_file_pastes_its_slots()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.SelectedRows = [editor.Rows[2]];
            editor.PasteText("<VARIANT_MESH>\r\n\t<SLOT name=\"cape\" >\r\n\t\t<VARIANT_MESH model=\"c.wsmodel\" />\r\n\t</SLOT>\r\n</VARIANT_MESH>");

            Assert.That(editor.Rows.Select(r => (r.Slot, r.Path)), Is.EqualTo(new[] {
                ("body", "a\\body.wsmodel"), ("weapon_1", "a\\sword.wsmodel"), ("weapon_2", ""), ("cape", "c.wsmodel") }));
        }

        // Final review: the next slot took the removed slot's place and its selection, so a second click removed it too.
        [Test]
        public void Remove_slot_leaves_nothing_selected()
        {
            var (_, editor) = Open(VmdFixtures.Unit.Replace("<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" />",
                "<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" ><VARIANT_MESH model=\"a\\gun.wsmodel\" /></SLOT>"));
            editor.SelectedRows = [editor.Rows[1]];
            editor.RemoveSlotsCommand.Execute(null);

            Assert.That(editor.Rows.Select(r => r.Slot), Is.EqualTo(new[] { "body", "weapon_2" }));
            Assert.That(editor.SelectedRows, Is.Empty);
        }

        // Final review: the first tag on a mesh that holds slots goes before them, which moved their places in the file, so the
        // rows were rebuilt inside the grid's commit of the Tags cell. Tags are values: the rows stay.
        [Test]
        public void A_tag_edit_keeps_the_rows()
        {
            var (_, editor) = Open(VmdFixtures.Gyrobomber);
            var before = editor.Rows.ToList();
            editor.SelectedRows = [editor.Rows[3]];

            editor.Rows[1].Tags = "equipment";

            Assert.That(editor.Rows, Is.EqualTo(before));
            Assert.That(editor.SelectedRows, Is.EqualTo(new[] { before[3] }));
            Assert.That(editor.Rows[3].Path, Is.EqualTo("warmachines/pilot_head.wsmodel"));
            editor.UndoCommand.Execute(null);
            Assert.That(editor.Rows, Is.EqualTo(before));
        }

        [Test]
        public void Replace_runs_over_every_row_when_one_is_selected()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            editor.SelectedRows = [editor.Rows[0]];
            editor.FindText = "A\\";
            editor.ReplaceText = "chd\\";
            editor.ReplaceCommand.Execute(null);

            Assert.That(editor.Rows.Take(2).Select(r => r.Path), Is.EqualTo(new[] { "chd\\body.wsmodel", "chd\\sword.wsmodel" }));
            Assert.That(editor.Status, Does.Contain("2"));
        }

        [Test]
        public void Plus_adds_what_browse_picked_and_a_definition_becomes_a_reference()
        {
            var (runner, editor) = Open(VmdFixtures.Unit);
            runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>()))
                .Returns(new BrowseDialogResultFile(true, runner.PackFileService.FindFile(FilePath)!));
            editor.AddEntryCommand.Execute(editor.Rows[2]);

            Assert.That((editor.Rows[2].Kind, editor.Rows[2].Path), Is.EqualTo(("reference", FilePath)));
            Assert.That(editor.Rows[2].Findings, Does.Contain("itself"));
        }

        [Test]
        public void A_missing_file_is_marked_on_its_row()
        {
            var (_, editor) = Open(VmdFixtures.Unit);
            Assert.That(editor.Rows[0].HasWarning, Is.True);
            Assert.That(editor.Rows[0].Findings, Does.Contain("a\\body.wsmodel"));
            Assert.That(editor.Problems, Is.EqualTo("0 errors, 2 warnings"));
        }

        [Test]
        public void A_file_that_does_not_parse_opens_with_its_load_error()
        {
            var (_, editor) = Open("<VARIANT_MESH>\r\n\t<SLOT name=\"a\">\r\n</VARIANT_MESH>");
            Assert.That(editor.LoadError, Does.Contain("line 3"));
            Assert.That(editor.Rows, Is.Empty);
            Assert.That(editor.Save(), Is.False);
        }
    }
}
