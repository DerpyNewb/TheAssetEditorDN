using Editors.UnitSimulator.Pairing;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Test.UnitSimulator
{
    public class RiderPairingTests
    {
        static readonly ClipLength Ten = new(10, 0.5f);

        static List<PairRow> Pair(AnimationBinWh3 mount, AnimationBinWh3 rider, Func<string, ClipLength?>? lengthOf = null) =>
            RiderPairing.Pair(mount, rider, lengthOf ?? (_ => Ten));

        [Test]
        public void AMountSlot_PairsWithItsRiderSlot()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"));
            var row = Pair(mount, rider).Single();
            Assert.That((row.MountSlot, row.RiderSlot, row.Status, row.RiderHasSlot), Is.EqualTo(("ATTACK_1", "RIDER_ATTACK_1", PairStatus.Paired, true)));
        }

        [Test]
        public void ARiderWithoutTheSlot_IsMissing()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_2", "m_a2.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"));
            var row = Pair(mount, rider).Single();
            Assert.That((row.Status, row.RiderHasSlot), Is.EqualTo((PairStatus.RiderSlotMissing, false)));
        }

        [Test]
        public void AnAnimNoPackHas_IsAnimMissing()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "gone.anim"));
            var row = Pair(mount, rider, path => path == "gone.anim" ? null : Ten).Single();
            Assert.That(row.Status, Is.EqualTo(PairStatus.AnimMissing));
        }

        // CA declares slots with no animation in them: the knight's RIDER_ATTACK_5, 73 tables' WALK_2. The rider has the slot; it plays nothing.
        [Test]
        public void ARiderSlotWithNoAnimation_IsAnimMissing_NotMissing()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"));
            rider.AnimationTableEntries[0].AnimationRefs.Clear();
            var row = Pair(mount, rider).Single();
            Assert.That((row.Status, row.RiderHasSlot), Is.EqualTo((PairStatus.AnimMissing, true)));
        }

        // The warhorse's ATTACK_6 and ATTACK_7: a slot of the mount's table, so a row, though it has nothing to play.
        [Test]
        public void AMountSlotWithNoAnimation_IsStillARow()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"), ("ATTACK_2", "m_a2.anim"));
            mount.AnimationTableEntries[1].AnimationRefs.Clear();
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"), ("RIDER_ATTACK_2", "r_a2.anim"));
            Assert.That(Pair(mount, rider).Select(x => (x.MountSlot, x.Status)), Is.EqualTo(new[] { ("ATTACK_1", PairStatus.Paired), ("ATTACK_2", PairStatus.AnimMissing) }));
        }

        [Test]
        public void DifferentFrameCounts_AreLengthsDiffer_AndShowBoth()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"));
            var row = Pair(mount, rider, path => path == "r_a1.anim" ? new ClipLength(12, 0.6f) : Ten).Single();
            Assert.That(row.Status, Is.EqualTo(PairStatus.LengthsDiffer));
            Assert.That(row.Detail, Does.Contain("10 frames").And.Contain("12 frames"));
        }

        [TestCase("STAND")]
        [TestCase("PORTHOLE_IDLE")]
        [TestCase("PERSISTENT_METADATA_ALIVE")]
        public void ACopyOnlySlot_IsCopyOnly_EvenWithNoRiderSlot(string slot)
        {
            var mount = MadeUpTables.Table("m", "horse01", "", (slot, "m.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m");
            Assert.That(Pair(mount, rider).Single().Status, Is.EqualTo(PairStatus.CopyOnly));
        }

        // A slot's variants are one row of the table and one row here.
        [Test]
        public void ASlotWithVariants_IsOneRow()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"));
            mount.AnimationTableEntries[0].AnimationRefs.Add(new() { AnimationFile = "m_a1_b.anim", AnimationMetaFile = "", AnimationSoundMetaFile = "" });
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"));
            Assert.That(Pair(mount, rider), Has.Count.EqualTo(1));
        }

        [Test]
        public void ARiderSlotWithNoMountSlot_IsRiderOnly()
        {
            var mount = MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim"));
            var rider = MadeUpTables.Table("r", "humanoid01", "m", ("RIDER_ATTACK_1", "r_a1.anim"), ("RIDER_MOVING_ATTACK_1", "r_ma1.anim"));
            Assert.That(RiderPairing.RiderOnly(mount, rider), Is.EqualTo(new[] { "RIDER_MOVING_ATTACK_1" }));
        }

        // The pairing as CA ships it. 13,406 is the count with 9.0's names; the dlc24 list gave 2,061, so this fails on the old list.
        [Test]
        public void EveryVanillaMountedTable_Pairs13406Slots()
        {
            var tables = VanillaGame.Tables(VanillaGame.Load().PackFileService);
            var byName = tables.Values.ToDictionary(x => x.Name);
            var mounted = tables.Values.Where(x => x.MountBin != "").ToList();
            Assert.That(mounted, Has.Count.EqualTo(376));
            Assert.That(mounted.Sum(r => Pair(byName[r.MountBin], r).Count(x => x.RiderHasSlot)), Is.EqualTo(13406));
        }

        // Of the 55 slots the knight has, all play at the horse's length but ATTACK_5: CA left his RIDER_ATTACK_5 empty.
        [Test]
        public void TheVanillaKnight_Has8CopyOnly16Missing54Paired_And8RiderOnly()
        {
            var tables = VanillaGame.Tables(VanillaGame.Load().PackFileService);
            var rider = tables[VanillaGame.KnightTable];
            var mount = tables[VanillaGame.WarhorseTable];
            var rows = Pair(mount, rider);
            Assert.That(rows, Has.Count.EqualTo(79));
            Assert.That(rows.Count(x => x.Status == PairStatus.CopyOnly), Is.EqualTo(8));
            Assert.That(rows.Count(x => x.Status == PairStatus.RiderSlotMissing), Is.EqualTo(16));
            Assert.That(rows.Count(x => x.Status == PairStatus.Paired), Is.EqualTo(54));
            var odd = rows.Single(x => x.Status is PairStatus.AnimMissing or PairStatus.LengthsDiffer);
            Assert.That((odd.MountSlot, odd.Status), Is.EqualTo(("ATTACK_5", PairStatus.AnimMissing)));
            Assert.That(RiderPairing.RiderOnly(mount, rider), Is.EquivalentTo(new[]
            {
                "RIDER_CAST_SPELL_FORWARD_LONG", "RIDER_CAST_SPELL_FORWARD_MEDIUM", "RIDER_CAST_SPELL_FORWARD_SHORT",
                "RIDER_CAST_SPELL_UP_LONG", "RIDER_CAST_SPELL_UP_MEDIUM", "RIDER_CAST_SPELL_UP_SHORT", "RIDER_MOVING_ATTACK_1", "RIDER_SHIELD_UP",
            }));
        }
    }
}
