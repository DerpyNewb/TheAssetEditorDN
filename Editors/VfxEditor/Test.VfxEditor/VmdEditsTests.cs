using Editors.VfxEditor.Document;
using Editors.VfxEditor.VariantMesh;
using static Test.VfxEditor.VmdFixtures;

namespace Test.VfxEditor
{
    // Each edit gives exactly the expected text, and one undo gives back exactly the original.
    class VmdEditsTests
    {
        static VfxDocument Doc(string text = "") => VfxDocument.FromText(text.Length == 0 ? Unit : text);
        static SpanElement Slot(VfxDocument d, string name) => VmdLayout.Top(d).ChildrenNamed("SLOT").Single(s => s.Attr("name") == name);
        static SpanElement Mesh(VfxDocument d, string slot) => VmdLayout.Entries(Slot(d, slot))[0];

        static void Check(VfxDocument d, IEnumerable<Splice> splices, string expected)
        {
            var original = d.Text;
            d.Apply(splices);
            Assert.That(d.Text, Is.EqualTo(expected));
            d.Undo();
            Assert.That(d.Text, Is.EqualTo(original));
        }

        static string Swap(string from, string to)
        {
            Assert.That(Unit, Does.Contain(from));
            return Unit.Replace(from, to);
        }

        [Test]
        public void Slot_name()
        {
            var d = Doc();
            Check(d, VmdEdits.SetAttribute(d, Slot(d, "weapon_2"), "name", "shield"), Swap("name=\"weapon_2\"", "name=\"shield\""));
        }

        [Test]
        public void Attach_point_set_changed_and_cleared()
        {
            var d = Doc();
            Check(d, VmdEdits.SetAttribute(d, Slot(d, "body"), "attach_point", "root"), Swap("<SLOT name=\"body\" >", "<SLOT name=\"body\" attach_point=\"root\" >"));
            Check(d, VmdEdits.SetAttribute(d, Slot(d, "weapon_1"), "attach_point", "be_prop_3"), Swap("\"be_prop_0\"", "\"be_prop_3\""));
            Check(d, VmdEdits.SetAttribute(d, Slot(d, "weapon_2"), "attach_point", ""), Swap("<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" />", "<SLOT name=\"weapon_2\" />"));
        }

        [Test]
        public void A_value_that_does_not_change_is_no_edit()
        {
            var d = Doc();
            Assert.That(VmdEdits.SetAttribute(d, Slot(d, "weapon_1"), "attach_point", "be_prop_0"), Is.Empty);
            Assert.That(VmdEdits.SetAttribute(d, Slot(d, "body"), "probability", ""), Is.Empty);
            Assert.That(VmdEdits.SetTags(d, Mesh(d, "body"), ["audio_entity_type:x", "audio_armour_type:leather"]), Is.Empty);
        }

        [Test]
        public void Probability_set_and_cleared()
        {
            var d = Doc();
            Check(d, VmdEdits.SetAttribute(d, Slot(d, "weapon_2"), "probability", "0.6"),
                Swap("attach_point=\"be_prop_1\" />", "attach_point=\"be_prop_1\" probability=\"0.6\" />"));
            var p = Doc(Swap("<SLOT name=\"body\" >", "<SLOT name=\"body\" probability=\"0.0\">"));
            Check(p, VmdEdits.SetAttribute(p, Slot(p, "body"), "probability", ""), Swap("<SLOT name=\"body\" >", "<SLOT name=\"body\">"));
        }

        [Test]
        public void Model_reference_and_imposter_paths()
        {
            var d = Doc();
            Check(d, VmdEdits.SetAttribute(d, Mesh(d, "weapon_1"), "model", "b/axe.wsmodel"), Swap("a\\sword.wsmodel", "b/axe.wsmodel"));
            Check(d, VmdEdits.SetAttribute(d, Mesh(d, "weapon_1"), "imposter_model", "b/i.rigid_model_v2"),
                Swap("model=\"a\\sword.wsmodel\" />", "model=\"a\\sword.wsmodel\" imposter_model=\"b/i.rigid_model_v2\" />"));
            var r = Doc("<VARIANT_MESH><SLOT name=\"h\"><VARIANT_MESH_REFERENCE definition=\"x.variantmeshdefinition\" /></SLOT></VARIANT_MESH>");
            Check(r, VmdEdits.SetAttribute(r, Mesh(r, "h"), "definition", "y.variantmeshdefinition"),
                "<VARIANT_MESH><SLOT name=\"h\"><VARIANT_MESH_REFERENCE definition=\"y.variantmeshdefinition\" /></SLOT></VARIANT_MESH>");
        }

        [Test]
        public void Tags_changed_added_and_removed()
        {
            var d = Doc();
            Assert.That(VmdEdits.Tags(d, Mesh(d, "body")), Is.EqualTo(new[] { "audio_entity_type:x", "audio_armour_type:leather" }));
            Check(d, VmdEdits.SetTags(d, Mesh(d, "body"), ["audio_entity_type:y", "audio_armour_type:leather"]), Swap("audio_entity_type:x", "audio_entity_type:y"));
            Check(d, VmdEdits.SetTags(d, Mesh(d, "body"), ["audio_entity_type:x"]),
                Swap("</META_DATA>\t\t\t<META_DATA>audio_armour_type:leather</META_DATA>", "</META_DATA>"));
            Check(d, VmdEdits.SetTags(d, Mesh(d, "body"), []),
                Swap(" >\r\n\t\t\t<META_DATA>audio_entity_type:x</META_DATA>\t\t\t<META_DATA>audio_armour_type:leather</META_DATA></VARIANT_MESH>", " ></VARIANT_MESH>"));
            Check(d, VmdEdits.SetTags(d, Mesh(d, "body"), ["audio_entity_type:x", "audio_armour_type:leather", "equipment"]),
                Swap("audio_armour_type:leather</META_DATA>", "audio_armour_type:leather</META_DATA>\t\t\t<META_DATA>equipment</META_DATA>"));
        }

        [Test]
        public void The_first_tag_on_a_self_closing_mesh()
        {
            var d = Doc();
            Check(d, VmdEdits.SetTags(d, Mesh(d, "weapon_1"), ["audio_melee_weapon_type:sword"]),
                Swap("\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />",
                     "\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" >\r\n\t\t\t<META_DATA>audio_melee_weapon_type:sword</META_DATA>\r\n\t\t</VARIANT_MESH>"));
        }

        // A mesh that holds slots takes its tags before them, where CA puts a mesh's tags.
        [Test]
        public void The_first_tag_on_a_mesh_that_holds_slots_goes_before_them()
        {
            var d = Doc(Gyrobomber);
            var pilot = VmdLayout.Entries(Slot(d, "pilot"))[0];
            Check(d, VmdEdits.SetTags(d, pilot, ["equipment"]),
                Gyrobomber.Replace("\t\t<VARIANT_MESH>\r\n\t\t\t<SLOT name=\"body\" >", "\t\t<VARIANT_MESH>\r\n\t\t\t<META_DATA>equipment</META_DATA>\r\n\t\t\t<SLOT name=\"body\" >"));
        }

        [Test]
        public void Add_slot_after_a_slot_and_into_an_empty_file()
        {
            var d = Doc();
            var (splice, name) = VmdEdits.AddSlot(d, Slot(d, "weapon_1"));
            Assert.That(name, Is.EqualTo("slot_1"));
            Check(d, [splice], Swap("\t</SLOT>\r\n\t<SLOT name=\"weapon_2\"", "\t</SLOT>\r\n\t<SLOT name=\"slot_1\" />\r\n\t<SLOT name=\"weapon_2\""));

            var taken = Doc(Swap("name=\"weapon_2\"", "name=\"slot_1\""));
            Assert.That(VmdEdits.AddSlot(taken, null).Name, Is.EqualTo("slot_2"));

            var empty = Doc("<VARIANT_MESH>\r\n</VARIANT_MESH>");
            Check(empty, [VmdEdits.AddSlot(empty, null).Splice], "<VARIANT_MESH>\r\n\t<SLOT name=\"slot_1\" />\r\n</VARIANT_MESH>");
            var closed = Doc("<VARIANT_MESH/>");
            Check(closed, [VmdEdits.AddSlot(closed, null).Splice], "<VARIANT_MESH>\r\n\t<SLOT name=\"slot_1\" />\r\n</VARIANT_MESH>");
        }

        [Test]
        public void Add_a_model_and_a_reference_to_a_self_closing_slot()
        {
            var d = Doc();
            Check(d, [VmdEdits.AddEntry(d, Slot(d, "weapon_2"), "b\\gun.wsmodel")],
                Swap("\t<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" />",
                     "\t<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" >\r\n\t\t<VARIANT_MESH model=\"b\\gun.wsmodel\" />\r\n\t</SLOT>"));
            Check(d, [VmdEdits.AddEntry(d, Slot(d, "weapon_1"), "variantmeshes/variantmeshdefinitions/Guns.VariantMeshDefinition")],
                Swap("\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />",
                     "\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />\r\n\t\t<VARIANT_MESH_REFERENCE definition=\"variantmeshes/variantmeshdefinitions/Guns.VariantMeshDefinition\" />"));
        }

        [Test]
        public void Remove_an_entry_and_a_slot()
        {
            var d = Doc();
            Check(d, [d.Remove(Mesh(d, "weapon_1"))], Swap("\t<SLOT name=\"weapon_1\" attach_point=\"be_prop_0\" >\r\n\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />\r\n\t</SLOT>",
                "\t<SLOT name=\"weapon_1\" attach_point=\"be_prop_0\" >\r\n\t</SLOT>"));
            Check(d, [d.Remove(Slot(d, "weapon_2"))], Swap("\r\n\t<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" />", ""));
        }

        [Test]
        public void Copy_then_paste_a_slot_after_another()
        {
            var d = Doc();
            var copied = VmdEdits.Copy(d, [Slot(d, "weapon_1"), Slot(d, "weapon_1")]);
            Assert.That(copied, Is.EqualTo("<SLOT name=\"weapon_1\" attach_point=\"be_prop_0\" >\r\n\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />\r\n\t</SLOT>"));
            Check(d, VmdEdits.Paste(d, Slot(d, "weapon_2"), copied), Swap("attach_point=\"be_prop_1\" />", "attach_point=\"be_prop_1\" />\r\n\t" + copied));
        }

        [Test]
        public void Paste_a_mesh_into_the_selected_slot()
        {
            var d = Doc();
            Check(d, VmdEdits.Paste(d, Slot(d, "weapon_1"), "<VARIANT_MESH model=\"c.wsmodel\" />"),
                Swap("\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />", "\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />\r\n\t\t<VARIANT_MESH model=\"c.wsmodel\" />"));
        }

        [Test]
        public void Paste_of_text_with_nothing_to_take_throws_and_changes_nothing()
        {
            var d = Doc();
            Assert.That(() => VmdEdits.Paste(d, Slot(d, "body"), "not xml <"), Throws.ArgumentException.With.Message.Contains("not XML"));
            Assert.That(() => VmdEdits.Paste(d, Slot(d, "body"), "<foo/>"), Throws.ArgumentException.With.Message.Contains("SLOT"));
            Assert.That(() => VmdEdits.Paste(d, null, "<VARIANT_MESH model=\"c.wsmodel\" />"), Throws.ArgumentException.With.Message.Contains("Select"));
            Assert.That(d.Text, Is.EqualTo(Unit));
            Assert.That(d.CanUndo, Is.False);
        }

        [Test]
        public void Replace_in_paths_counts_the_cells()
        {
            var d = Doc();
            var splices = VmdEdits.ReplaceInPaths(d, [Mesh(d, "body"), Mesh(d, "weapon_1")], "A\\", "chd\\", out var cells);
            Assert.That(cells, Is.EqualTo(2));
            Check(d, splices, Unit.Replace("a\\body", "chd\\body").Replace("a\\sword", "chd\\sword"));
            Assert.That(VmdEdits.ReplaceInPaths(d, [Mesh(d, "body")], "", "x", out var none), Is.Empty);
            Assert.That(none, Is.Zero);
        }
    }
}
