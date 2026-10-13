namespace Test.UnitSimulator
{
    // A part's row edited in the panel: each edit takes effect at once (spec section 4).
    public class PersonalityEditTests
    {
        const string WarsphinxTech = @"variantmeshes\wh_variantmodels\bc1\tmb\tmb_warsphinx\tech\tmb_warsphinx_01.anim";

        [Test]
        public void ABonePicked_MovesThePart_ToThatBone()
        {
            var (runner, sim) = PersonalityTests.OpenOn(PersonalityTests.WarsphinxModel);
            sim.Personalities.AddSet(PersonalityTests.Warsphinx);
            var crew = sim.Personalities.Parts[0];
            sim.Personalities.PlaceAll();
            var before = crew.Part.Data.Offset;

            crew.Bone = "ap_crewposition_0";
            sim.Personalities.PlaceAll();

            Assert.That(AttachBonesTests.Off(crew.Part.Data.Offset, PersonalityTests.OnMount(runner, sim, WarsphinxTech, "ap_crewposition_0")), Is.LessThan(0.001f));
            Assert.That(AttachBonesTests.Off(crew.Part.Data.Offset, before), Is.GreaterThan(0.1f));
            Assert.That(crew.Status, Is.Empty);
        }

        [Test]
        public void ABoneTheMountLacks_IsNamedWhenPicked()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            priest.Bone = "ap_derpy_nowhere";
            Assert.That(priest.Status, Does.Contain("ap_derpy_nowhere"));
        }

        [Test]
        public void ATableChanged_ReloadsThePart()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            priest.Table = "hu1_tomb_skeleton_sword_and_shield";
            Assert.That(priest.Part.FragAndSlotSelection.FragmentList.SelectedItem?.Name, Is.EqualTo("hu1_tomb_skeleton_sword_and_shield"));
            Assert.That(priest.Status, Is.Empty);
        }

        [Test]
        public void AVariantChanged_ReloadsTheModel()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            var before = priest.Part.Data.MeshName.Value;
            priest.Variant = "wh2_dlc09_tmb_skeleton_sword_shield";
            Assert.That(priest.Part.Data.MeshName.Value, Is.Not.EqualTo(before));
            Assert.That(priest.Status, Is.Empty);
        }

        [Test]
        public void AVanillaKey_NamesItsFile_AndAnEditedKey_IsNew()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            Assert.That(priest.Copies, Does.Contain("db/battle_personalities_tables/data__"));
            priest.Key = "derpy_test_priest";
            Assert.That(priest.Copies, Is.EqualTo("new key"));
        }
    }
}
