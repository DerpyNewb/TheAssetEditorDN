using Editors.VfxEditor.VariantMesh;

namespace Test.VfxEditor
{
    class VmdLayoutTests
    {
        [Test]
        public void Rows_follow_the_file_with_nested_slots_inline()
        {
            var (document, error) = VmdLayout.Load(VmdFixtures.Utf8(VmdFixtures.Gyrobomber));
            Assert.That(error, Is.Null);

            var rows = VmdLayout.Rows(document!);

            Assert.That(rows.Select(r => r.Label), Is.EqualTo(new[] { "body", "pilot", "pilot / body", "pilot / head", "pilot / helmet", "pilot / helmet", "pilot / helmet", "weapon_1" }));
            Assert.That(rows.Select(r => r.Entry), Is.EqualTo(new[] { 0, 0, 0, 0, 0, 1, 2, -1 }));
            Assert.That(rows.Select(r => r.IsFirst), Is.EqualTo(new[] { true, true, true, true, true, false, false, true }));
            Assert.That(rows.Select(r => r.Depth), Is.EqualTo(new[] { 0, 0, 1, 1, 1, 1, 1, 0 }));
            Assert.That(VmdLayout.EntryOf(document!, rows[5])!.Attr("model"), Is.EqualTo("warmachines/helmet_02.wsmodel"));
            Assert.That(VmdLayout.EntryOf(document!, rows[6])!.Name, Is.EqualTo("VARIANT_MESH_REFERENCE"));
            Assert.That(document!.At(rows[2].Slot).Attr("name"), Is.EqualTo("body"));
        }

        [Test]
        public void An_empty_slot_is_one_row()
        {
            var (document, _) = VmdLayout.Load(VmdFixtures.Utf8("<VARIANT_MESH>\r\n\t<SLOT name=\"weapon_3\" attach_point=\"be_prop_2\" />\r\n</VARIANT_MESH>"));

            var row = VmdLayout.Rows(document!).Single();

            Assert.That((row.Label, row.Entry, row.IsFirst), Is.EqualTo(("weapon_3", -1, true)));
            Assert.That(VmdLayout.EntryOf(document!, row), Is.Null);
        }

        // Two CA files put a META_DATA straight under a SLOT; it is kept, and it is not a row.
        [Test]
        public void Meta_data_under_a_slot_is_not_an_entry()
        {
            var (document, _) = VmdLayout.Load(VmdFixtures.Utf8(
                "<VARIANT_MESH><SLOT name=\"a\"><META_DATA>equipment</META_DATA><VARIANT_MESH model=\"m.wsmodel\"/></SLOT></VARIANT_MESH>"));

            var row = VmdLayout.Rows(document!).Single();

            Assert.That(row.Entry, Is.EqualTo(0));
            Assert.That(VmdLayout.EntryOf(document!, row)!.Attr("model"), Is.EqualTo("m.wsmodel"));
        }

        [Test]
        public void A_file_that_does_not_parse_says_where()
        {
            var (document, error) = VmdLayout.Load(VmdFixtures.Utf8("<VARIANT_MESH>\r\n\t<SLOT name=\"a\">\r\n</VARIANT_MESH>"));

            Assert.That(document, Is.Null);
            Assert.That(error, Does.Contain("line 3"));
        }

        [Test]
        public void A_root_that_is_not_VARIANT_MESH_is_refused()
        {
            var (document, error) = VmdLayout.Load(VmdFixtures.Utf8("<vfx/>"));

            Assert.That(document, Is.Null);
            Assert.That(error, Does.Contain("VARIANT_MESH"));
        }
    }
}
