using Editors.VfxEditor.Document;
using System.Text;
using Editors.VfxEditor;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.TestUtility;

namespace Test.VfxEditor
{
    class VfxFileSessionTests
    {
        PackFileService _packFileService = null!;
        IPackFileContainer _output = null!;
        VfxFileSession _session = null!;

        [SetUp]
        public void Setup()
        {
            _packFileService = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
            var source = _packFileService.CreateNewPackFileContainer("source", PackFileVersion.PFH5, PackFileCAType.MOD);
            _packFileService.AddFilesToPack(source, [
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default))),
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("lib.xml", Fixtures.Utf16(Fixtures.Effect(emitters: Fixtures.Emitters("t1", "t2"))))),
                new NewPackFileEntry("vfx\\materials", PackFile.CreateFromBytes("m.xml", "<PROPERTY_SET/>"u8.ToArray())),
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("broken.xml", "<vfx>"u8.ToArray()))]);
            _output = _packFileService.CreateNewPackFileContainer("output", PackFileVersion.PFH5, PackFileCAType.MOD, setEditablePack: true);
            _session = new VfxFileSession(_packFileService, new FileSaveService(_packFileService, new Mock<IStandardDialogs>().Object, MockScopedLogger.Create()));
        }

        PackFile File(string path) => _packFileService.FindFile(path)!;

        [Test]
        public void Save_writes_the_edited_bytes_into_the_editable_pack_in_the_files_encoding()
        {
            _session.Load(File("vfx\\fx.xml"));
            var document = _session.Document!;
            document.Apply([document.SetAttribute(Fixtures.Emitter(document), "type", "continuous_rigid")]);
            Assert.That(_session.HasUnsavedChanges, Is.True);

            Assert.That(_session.Save(), Is.True);

            var saved = _packFileService.FindFile("vfx\\fx.xml", _output)!;
            Assert.That(saved.DataSource.ReadData(), Is.EqualTo(document.Save()));
            Assert.That(saved.DataSource.ReadData()[..2], Is.EqualTo(new byte[] { 0xFF, 0xFE }));
            Assert.That(_session.HasUnsavedChanges, Is.False);
            Assert.That(_session.File, Is.SameAs(saved));
        }

        [Test]
        public void A_second_save_updates_the_same_file()
        {
            _session.Load(File("vfx\\fx.xml"));
            _session.Save();
            var document = _session.Document!;
            document.Apply([document.SetAttribute(Fixtures.Emitter(document), "type", "x")]);
            _session.Save();
            Assert.That(_output.GetFileCount(), Is.EqualTo(1));
            Assert.That(_packFileService.FindFile("vfx\\fx.xml", _output)!.DataSource.ReadData(), Is.EqualTo(document.Save()));
        }

        [Test]
        public void A_material_and_a_broken_file_open_with_a_reason_and_no_document()
        {
            _session.Load(File("vfx\\materials\\m.xml"));
            Assert.That(_session.Document, Is.Null);
            Assert.That(_session.LoadError, Does.Contain("not an effect"));
            _session.Load(File("vfx\\broken.xml"));
            Assert.That(_session.Document, Is.Null);
            Assert.That(_session.LoadError, Does.Contain("line 1"));
        }

        [Test]
        public void The_effect_name_and_a_targets_emitters_come_from_the_packs()
        {
            _session.Load(File("vfx\\fx.xml"));
            Assert.That(_session.EffectName, Is.EqualTo("fx"));
            Assert.That(_session.EmitterIdsOf("lib"), Is.EqualTo(new[] { "t1", "t2" }));
            Assert.That(_session.EmitterIdsOf("missing"), Is.Null);
        }

        [Test]
        public void A_target_whose_pack_cannot_be_read_has_no_emitter_list()
        {
            var unreadable = new Mock<IDataSource>();
            unreadable.Setup(s => s.ReadData()).Throws(new System.IO.IOException("the pack moved"));
            File("vfx\\lib.xml").DataSource = unreadable.Object;
            Assert.That(_session.EmitterIdsOf("lib"), Is.Null);
        }

        // An effect that only references `target`, switching these of its emitters on.
        static string Uses(string target, params string[] rows) =>
            Fixtures.Effect(emitters: "", chains: "", stacks: "", references: Fixtures.Reference(target, [.. rows.Select(r => Fixtures.Row(r))]));

        void Add(IPackFileContainer pack, string name, string effect) =>
            _packFileService.AddFilesToPack(pack, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes(name, Fixtures.Utf16(effect)))]);

        string Text(string path)
        {
            var bytes = File(path).DataSource.ReadData();
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        void Rename(string id, string newId)
        {
            var document = _session.Document!;
            document.Apply(VfxEdits.RenameEmitter(document, id, newId));
            _session.Renamed(id, newId);
        }

        // Another effect's override rows name the library's emitters by id, so a rename left them switching nothing. Saving the
        // library now moves the rows in the editable pack; a file in another pack is named instead, as only its pack can change it.
        [Test]
        public void A_saved_rename_moves_the_references_in_the_editable_pack_and_names_the_rest()
        {
            var source = _packFileService.GetAllPackfileContainers().Single(c => c.Name == "source");
            Add(_output, "user.xml", Uses("lib", "t1", "t2"));
            Add(_output, "other.xml", Uses("lib2", "t1"));
            Add(source, "theirs.xml", Uses("lib", "t1"));
            _session.Load(File("vfx\\lib.xml"));
            Rename("t1", "t9");
            Assert.That(Text("vfx\\user.xml"), Does.Contain("name=\"t1\""), "not before the save");

            _session.Save();

            Assert.That(Text("vfx\\user.xml"), Does.Contain("name=\"t9\"").And.Contain("name=\"t2\"").And.Not.Contain("name=\"t1\""));
            Assert.That(Text("vfx\\other.xml"), Does.Contain("name=\"t1\""), "a reference to another effect");
            Assert.That(Text("vfx\\theirs.xml"), Does.Contain("name=\"t1\""), "another pack's file");
            Assert.That(_session.Carried, Is.EqualTo("; references moved to the new ids in vfx\\user.xml; outside your pack, vfx\\theirs.xml still names an old id"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_rename_undone_or_renamed_back_moves_no_reference(bool undo)
        {
            Add(_output, "user.xml", Uses("lib", "t1"));
            _session.Load(File("vfx\\lib.xml"));
            Rename("t1", "t9");
            if (undo)
                _session.Document!.Undo();
            else
                Rename("t9", "t1");

            _session.Save();

            Assert.That((Text("vfx\\user.xml").Contains("name=\"t1\""), _session.Carried), Is.EqualTo((true, "")));
        }

        // Two emitters swapping ids through a third: each row follows its own emitter, once. A row naming an id the library
        // never had, as 485 vanilla rows do, names it still.
        [Test]
        public void A_swap_moves_each_reference_to_its_own_emitters_new_id()
        {
            Add(_output, "user.xml", Uses("lib", "t1", "t2", "tx"));
            _session.Load(File("vfx\\lib.xml"));
            Rename("t1", "tx");
            Rename("t2", "t1");
            Rename("tx", "t2");

            _session.Save();

            var user = Text("vfx\\user.xml");
            Assert.That(user.IndexOf("name=\"t2\""), Is.GreaterThan(0).And.LessThan(user.IndexOf("name=\"t1\"")), "the first row, t1's, now names t2");
            Assert.That(user, Does.Contain("name=\"tx\""));
        }

        // A rename in one file, never saved, does not follow the tab to the next file it opens.
        [Test]
        public void An_unsaved_rename_is_forgotten_when_another_file_opens()
        {
            Add(_output, "lib2.xml", Fixtures.Effect(emitters: Fixtures.Emitters("t9")));
            Add(_output, "user.xml", Uses("lib2", "t1"));
            _session.Load(File("vfx\\lib.xml"));
            Rename("t1", "t9");
            _session.Load(File("vfx\\lib2.xml"));

            _session.Save();

            Assert.That(Text("vfx\\user.xml"), Does.Contain("name=\"t1\""));
        }

        // CA's files can only reference CA's effects, so they are read only when the saved effect is one of CA's.
        [TestCase(false)]
        [TestCase(true)]
        public void Cas_files_are_read_only_for_an_effect_that_is_one_of_cas(bool casEffect)
        {
            var ca = _packFileService.CreateNewPackFileContainer("ca", PackFileVersion.PFH5, PackFileCAType.MOD);
            Add(ca, "cas_user.xml", Uses("lib", "t1"));
            if (casEffect)
                Add(ca, "lib.xml", Fixtures.Effect(emitters: Fixtures.Emitters("t1", "t2")));
            ca.IsCaPackFile = true;
            _session.Load(File("vfx\\lib.xml"));
            Rename("t1", "t9");

            _session.Save();

            Assert.That(_session.Carried, Is.EqualTo(casEffect ? "; outside your pack, vfx\\cas_user.xml still names an old id" : ""));
        }
    }
}
