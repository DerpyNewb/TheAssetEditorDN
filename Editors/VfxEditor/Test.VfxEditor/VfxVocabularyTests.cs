using Editors.VfxEditor.Document;
using Editors.VfxEditor.Vocabulary;

namespace Test.VfxEditor
{
    class VfxVocabularyTests
    {
        static VfxVocabulary Build() => VfxVocabulary.Build([
            ("vfx\\a.xml", Fixtures.Document()),
            ("vfx\\b.xml", Fixtures.Document(Fixtures.Effect(emitters: Fixtures.Emitters("t1")))),
            ("vfx\\materials\\m.xml", VfxDocument.FromText("<PROPERTY_SET/>"))]);

        [Test]
        public void Effect_names_skip_vfx_subfolders() => Assert.That(Build().EffectNames, Is.EqualTo(new[] { "a", "b" }));

        [Test]
        public void Property_names_and_templates_come_from_tier_zero()
        {
            var vocabulary = Build();
            Assert.That(vocabulary.PropertyNames("emitter:continuous_mesh"), Is.EqualTo(new[] { "A", "B", "Curve", "LifeTime" }));
            Assert.That(vocabulary.PropertyTemplate("emitter:continuous_mesh", "B"), Is.EqualTo("<property name=\"B\" quality=\"0\" value=\"7\"/>"));
            Assert.That(vocabulary.PropertyNames("stack"), Is.EqualTo(new[] { "scene_view_type" }));
        }

        [Test]
        public void A_modifier_template_is_its_fragments_most_common_shape()
        {
            var vocabulary = Build();
            Assert.That(vocabulary.Fragments, Is.EqualTo(new[] { "ps_texture", "vs_colour" }));
            Assert.That(vocabulary.ModifierTemplate("ps_texture"),
                Is.EqualTo(new[] { "<property name=\"pre_multiplied_alpha\" value=\"true\"/>", "<texture_property name=\"diffuse\" path=\"vfx/a.dds\"/>" }));
        }

        [Test]
        public void Choices_are_small_sets_of_words()
        {
            Assert.That(Build().Choices("scene_view_type", "value"), Is.Null);   // a single value is not a choice
            var more = VfxVocabulary.Build([
                ("vfx\\a.xml", Fixtures.Document()),
                ("vfx\\c.xml", Fixtures.Document(Fixtures.Default.Replace("FULLRES_DEFERRED_ALPHA", "DISTORTION")))]);
            Assert.That(more.Choices("scene_view_type", "value"), Is.EquivalentTo(new[] { "FULLRES_DEFERRED_ALPHA", "DISTORTION" }));
            Assert.That(more.Choices("B", "value"), Is.Null);   // numbers are typed, not picked
        }
    }
}
