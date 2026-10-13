using Shared.GameFormats.AnimationPack;

namespace Shared.CoreTest.AnimationSlots
{
    // WH3's slot names id for id as 9.0 numbers them (RPFM's anim_ids_warhammer_3.csv, 2026-09-25). The dlc24 list the fork loaded
    // before named every id from 1183 up after a slot two or more below it: 1183 read WAVERING_STAND_LOOK_FORWARD_1.
    public class Wh3SlotNamesTests
    {
        [TestCase(0, "MISSING_ANIM")]
        [TestCase(1182, "RIDER_REBIRTH_FLYING")]
        [TestCase(1183, "TELEPORT_ARRIVAL")]
        [TestCase(1217, "HIT_REACTION_COMBAT_READY_1")]
        [TestCase(1901, "RIDER_ATTACK_1")]
        [TestCase(2057, "RIDER_SHOOT_READY")]
        [TestCase(2291, "BEAM_TEST_LOOPED")]
        public void AnId_HasIts9_0Name(int id, string name) =>
            Assert.That(AnimationSlotTypeHelperWh3.GetFromId(id).Value, Is.EqualTo(name));

        [Test]
        public void TheListHasEvery9_0Id() => Assert.That(AnimationSlotTypeHelperWh3.Values, Has.Count.EqualTo(2292));

        [Test]
        public void ANameFindsItsId() => Assert.That(AnimationSlotTypeHelperWh3.GetfromValue("RIDER_ATTACK_1").Id, Is.EqualTo(1901));
    }
}
