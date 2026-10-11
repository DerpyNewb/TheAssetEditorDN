using Editors.VfxEditor.Document;

namespace Test.VfxEditor
{
    class VfxEditsTests
    {
        static bool[] HasModifier(VfxDocument d, string fragment) =>
            Fixtures.Chain(d).ChildrenNamed("quality").Select(b => TierEdits.Modifier(b, fragment, 0) != null).ToArray();

        [Test]
        public void A_modifier_is_added_to_every_block()
        {
            var d = Fixtures.Document();
            d.Apply(VfxEdits.AddModifier(d, Fixtures.Chain(d), 0, "vs_flip_uv", ["<property name=\"u\" value=\"1\"/>"], thisTierOnly: false));
            Assert.That(HasModifier(d, "vs_flip_uv"), Is.EqualTo(new[] { true, true, true }));
            Assert.That(d.Text, Does.Contain("\t\t\t\t</modifier>\r\n\t\t\t\t<modifier fragment=\"vs_flip_uv\">\r\n\t\t\t\t\t<property name=\"u\" value=\"1\"/>\r\n\t\t\t\t</modifier>\r\n\t\t\t</quality>"));
        }

        [Test]
        public void This_tier_only_adds_a_modifier_to_one_block()
        {
            var d = Fixtures.Document();
            d.Apply(VfxEdits.AddModifier(d, Fixtures.Chain(d), 1, "vs_flip_uv", [], thisTierOnly: true));
            Assert.That(HasModifier(d, "vs_flip_uv"), Is.EqualTo(new[] { false, true, false }));
            d.Apply(VfxEdits.RemoveModifier(d, Fixtures.Chain(d), 0, "vs_colour", 1, thisTierOnly: false));
            Assert.That(Fixtures.Chain(d).ChildrenNamed("quality").Select(b => b.ChildrenNamed("modifier").Count(m => m.Attr("fragment") == "vs_colour")),
                Is.All.EqualTo(1));
        }

        [Test]
        public void Ultras_block_is_copied_to_a_tier_without_one()
        {
            var d = Fixtures.Document(Fixtures.Effect(chains: Fixtures.ChainOf(Fixtures.Block(0, "true", "1", "1"))));
            Assert.Throws<InvalidOperationException>(() => VfxEdits.AddModifier(d, Fixtures.Chain(d), 2, "vs_flip_uv", [], true));
            d.Apply([VfxEdits.CopyBlock(d, Fixtures.Chain(d), 0, 2)]);
            Assert.That(TierEdits.Modifier(TierEdits.Block(Fixtures.Chain(d), 2), "ps_texture", 0), Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => VfxEdits.CopyBlock(d, Fixtures.Chain(d), 0, 2));
        }

        [Test]
        public void A_duplicated_emitter_has_all_three_records_under_its_new_id()
        {
            var d = Fixtures.Document();
            d.Apply(VfxEdits.DuplicateEmitter(d, "e1", "e2"));
            foreach (var section in new[] { "emitters", "behaviour_chains", "effect_render_stacks" })
                Assert.That(d.Section(section)!.Children.Select(c => c.Attr("id")), Is.EqualTo(new[] { "e1", "e2" }), section);
            Assert.That(d.RawOf(d.ById("emitters", "e2")!), Is.EqualTo(d.RawOf(d.ById("emitters", "e1")!).Replace("id=\"e1\"", "id=\"e2\"")));
        }

        [TestCase("e1")]
        [TestCase("")]
        [TestCase(" e3")]
        public void A_bad_new_id_is_refused(string id)
        {
            var d = Fixtures.Document();
            Assert.Throws<ArgumentException>(() => VfxEdits.DuplicateEmitter(d, "e1", id));
        }

        [Test]
        public void Removing_an_emitter_removes_its_chain_and_stack()
        {
            var d = Fixtures.Document();
            d.Apply(VfxEdits.RemoveEmitter(d, "e1"));
            Assert.That(new[] { "emitters", "behaviour_chains", "effect_render_stacks" }.Sum(s => d.Section(s)!.Children.Count), Is.Zero);
        }

        [Test]
        public void Removing_a_property_linked_removes_every_tier()
        {
            var d = Fixtures.Document();
            d.Apply(VfxEdits.RemoveProperty(d, Fixtures.Emitter(d), "A", 0, thisTierOnly: false));
            Assert.That(TierEdits.Tiers(Fixtures.Emitter(d), "A"), Is.Empty);
        }

        [Test]
        public void Removing_ultras_value_alone_keeps_it_on_the_tiers_that_inherited_it()
        {
            var d = Fixtures.Document();
            d.Apply(VfxEdits.RemoveProperty(d, Fixtures.Emitter(d), "B", 0, thisTierOnly: true));
            Assert.That(TierEdits.Tiers(Fixtures.Emitter(d), "B").ToDictionary(x => x.Key, x => x.Value.Attr("value")),
                Is.EqualTo(new Dictionary<int, string?> { [1] = "7", [2] = "7" }));
            Assert.Throws<InvalidOperationException>(() => VfxEdits.RemoveProperty(d, Fixtures.Emitter(d), "LifeTime", 1, thisTierOnly: true));
        }

        [Test]
        public void An_added_property_goes_after_the_last()
        {
            var d = Fixtures.Document();
            d.Apply([VfxEdits.AddProperty(d, Fixtures.Emitter(d), "<property name=\"Z\" quality=\"0\" value=\"1\"/>")]);
            Assert.That(d.Text, Does.Contain("</property>\r\n\t\t\t<property name=\"Z\" quality=\"0\" value=\"1\"/>\r\n\t\t</emitter>"));
        }

        [Test]
        public void A_modifier_property_is_added_where_it_is_missing_and_removed_by_tier()
        {
            var d = Fixtures.Document();
            bool[] HasQ() => Fixtures.Chain(d).ChildrenNamed("quality")
                .Select(b => TierEdits.PropertyIn(TierEdits.Modifier(b, "vs_colour", 0), "q") != null).ToArray();
            d.Apply(VfxEdits.AddModifierProperty(d, Fixtures.Chain(d), 0, "vs_colour", 0, "<property name=\"q\" value=\"2\"/>", false));
            Assert.That(HasQ(), Is.EqualTo(new[] { true, true, true }));
            d.Apply(VfxEdits.RemoveModifierProperty(d, Fixtures.Chain(d), 2, "vs_colour", 0, "q", true));
            Assert.That(HasQ(), Is.EqualTo(new[] { true, true, false }));
        }

        [Test]
        public void A_new_reference_lists_every_emitter_of_its_target()
        {
            var d = Fixtures.Document();
            d.Apply([VfxEdits.AddReference(d, "wh_main_lib_trail", ["t1", "t2"])]);
            var reference = d.Section("vfx_references")!.Children[^1];
            Assert.That(reference.Attributes.Select(a => a.Name), Is.EqualTo(VfxEdits.ReferenceDefaults.Select(a => a.Name)));
            Assert.That(reference.Attr("vfx_ref"), Is.EqualTo("wh_main_lib_trail"));
            Assert.That(reference.ChildrenNamed("emitter").Select(r => (r.Attr("name"), r.Attr("enable"))), Is.EqualTo(new[] { ("t1", "true"), ("t2", "true") }));
            Assert.That(d.Text, Does.Contain("\r\n\t\t<emitter name=\"t1\" enable=\"true\" tint_min_r=\"1.00\""));
        }

        [Test]
        public void Overrides_are_switched_together_and_added_singly()
        {
            var d = Fixtures.Document();
            SpanElement Reference() => d.Section("vfx_references")!.Children[0];
            d.Apply(VfxEdits.SetAllOverrides(d, Reference(), false));
            Assert.That(Reference().ChildrenNamed("emitter").Select(r => r.Attr("enable")), Is.All.EqualTo("false"));
            d.Apply([VfxEdits.AddOverride(d, Reference(), "c")]);
            Assert.That(Reference().ChildrenNamed("emitter").Select(r => r.Attr("name")), Is.EqualTo(new[] { "a", "b", "c" }));
        }
    }
}
