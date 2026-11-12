using Editors.VfxEditor.Document;
using Editors.VfxEditor.VariantMesh;

namespace Test.VfxEditor
{
    // Each check fires on a file that trips it alone: the finding's row, its severity, and the value named in its message.
    class VmdChecksTests
    {
        const string Own = "variantmeshes/variantmeshdefinitions/unit.variantmeshdefinition";

        static List<VmdFinding> Run(string body, Func<string, bool>? exists = null)
        {
            var document = VfxDocument.FromText("<VARIANT_MESH>" + body + "</VARIANT_MESH>");
            return VmdChecks.Run(document, VmdLayout.Rows(document), exists ?? (_ => true), Own);
        }

        static void One(List<VmdFinding> findings, int row, bool isError, string named)
        {
            Assert.That(findings, Has.Count.EqualTo(1), string.Join("\n", findings));
            Assert.That((findings[0].Row, findings[0].IsError), Is.EqualTo((row, isError)));
            Assert.That(findings[0].Message, Does.Contain(named));
        }

        [Test]
        public void A_clean_file_has_no_findings() =>
            Assert.That(VmdChecks.Run(VfxDocument.FromText(VmdFixtures.Gyrobomber), VmdLayout.Rows(VfxDocument.FromText(VmdFixtures.Gyrobomber)), _ => true, Own), Is.Empty);

        [Test]
        public void A_slot_with_no_name_is_an_error() =>
            One(Run("<SLOT name=\"a\"/><SLOT attach_point=\"root\"><VARIANT_MESH model=\"m.wsmodel\"/><VARIANT_MESH model=\"n.wsmodel\"/></SLOT>"), 1, true, "name");

        [Test]
        public void A_probability_that_is_not_a_number_is_an_error() =>
            One(Run("<SLOT name=\"a\"/><SLOT name=\"b\" probability=\"0,6\"/>"), 1, true, "0,6");

        [Test]
        public void A_path_not_in_the_loaded_packs_is_a_warning()
        {
            One(Run("<SLOT name=\"a\"><VARIANT_MESH model=\"m.wsmodel\"/><VARIANT_MESH model=\"gone.wsmodel\"/></SLOT>", p => p != "gone.wsmodel"), 1, false, "gone.wsmodel");
            One(Run("<SLOT name=\"a\"><VARIANT_MESH model=\"m.wsmodel\" imposter_model=\"gone.rigid_model_v2\"/></SLOT>", p => p != "gone.rigid_model_v2"), 0, false, "gone.rigid_model_v2");
            One(Run("<SLOT name=\"a\"><VARIANT_MESH_REFERENCE definition=\"gone.variantmeshdefinition\"/></SLOT>", p => p != "gone.variantmeshdefinition"), 0, false, "gone.variantmeshdefinition");
        }

        [Test]
        public void A_path_of_the_wrong_kind_is_a_warning()
        {
            One(Run("<SLOT name=\"a\"><VARIANT_MESH model=\"m.dds\"/></SLOT>"), 0, false, "m.dds");
            One(Run("<SLOT name=\"a\"><VARIANT_MESH_REFERENCE definition=\"m.wsmodel\"/></SLOT>"), 0, false, "m.wsmodel");
        }

        [Test]
        public void A_reference_to_this_file_is_a_warning() =>
            One(Run("<SLOT name=\"a\"><VARIANT_MESH_REFERENCE definition=\"VariantMeshes\\VariantMeshDefinitions\\Unit.variantmeshdefinition\"/></SLOT>"), 0, false, "itself");

        [Test]
        public void A_mesh_with_no_model_and_no_slots_is_a_warning() =>
            One(Run("<SLOT name=\"a\"><VARIANT_MESH model=\"m.wsmodel\"/><VARIANT_MESH /></SLOT>"), 1, false, "no model");

        [Test]
        public void Two_slots_with_one_name_at_one_level_are_both_marked()
        {
            var findings = Run("<SLOT name=\"head\"/><SLOT name=\"body\"><VARIANT_MESH><SLOT name=\"head\"/></VARIANT_MESH></SLOT><SLOT name=\"Head\"/>");
            Assert.That(findings.Select(f => (f.Row, f.IsError)), Is.EqualTo(new[] { (0, false), (3, false) }));
            Assert.That(findings[0].Message, Does.Contain("head"));
        }

        [Test]
        public void A_tag_key_CA_does_not_use_is_a_warning() =>
            One(Run("<SLOT name=\"a\"><VARIANT_MESH model=\"m.wsmodel\"><META_DATA>equipment</META_DATA><META_DATA>audio_armor_type:plate</META_DATA></VARIANT_MESH></SLOT>"), 0, false, "audio_armor_type");
    }
}
