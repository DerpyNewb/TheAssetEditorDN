using System.IO;
using Shared.GameFormats.DB;

namespace Test.UnitSimulator
{
    public class DbTableReaderTests
    {
        const string Personalities = "battle_personalities_tables";
        const string CasketRider = "wh2_dlc09_tmb_casket_of_souls_rider1";

        // A v12 battle_personalities row: key, table, variant, battle_entity, battle_entity_stats and the three flags.
        static object[] Row(string key) => [key, "hu1_test_table", "derpy_test_variant", "", "", false, true, false];

        // The counts tools/read_vanilla_db.py gives for CA's 9.0 db.pack.
        [Test]
        public void VanillaDbPack_GivesTheRowCountsOfReadVanillaDb()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            Assert.That(DbTables.Read(pfs, Personalities), Has.Count.EqualTo(963));
            Assert.That(DbTables.Read(pfs, "land_units_to_battle_personalities_junctions_tables"), Has.Count.EqualTo(1507));
            Assert.That(DbTables.Read(pfs, "variants_tables"), Has.Count.EqualTo(3368));
        }

        [Test]
        public void TheCasketsRider_ReadsFieldByField()
        {
            var row = DbTables.Read(VanillaGame.Load(withDb: true).PackFileService, Personalities).Single(x => x.Text("key") == CasketRider);
            Assert.That((row.Text("man_animations_table"), row.Text("variant"), row.Text("battle_entity"), row.Text("battle_entity_stats")),
                Is.EqualTo(("hu1_liche_priest_staff_and_sword", "wh2_dlc09_tmb_liche_priest_casket", "wh_main_infantry_rider", "")));
            Assert.That((row.Flag("autonomous_rider_hero"), row.Flag("autonomous_rider_can_shoot_in_melee"), row.Flag("can_only_be_used_by_draught")),
                Is.EqualTo((false, true, false)));
            Assert.That((row.Version, row.File), Is.EqualTo((12, "db/battle_personalities_tables/data__")));
        }

        [Test]
        public void ATruncatedFile_IsRefused_NamingTableVersionAndFile()
        {
            var data = MadeUpDb.File(Personalities, 12, Row("derpy_a"));
            var e = Assert.Throws<InvalidDataException>(() => DbTableReader.Read(data[..^3], Personalities, "test/derpy_cut"));
            Assert.That(e!.Message, Does.Contain(Personalities).And.Contain("v12").And.Contain("test/derpy_cut"));
        }

        [Test]
        public void TrailingBytes_AreRefused()
        {
            var data = MadeUpDb.File(Personalities, 12, Row("derpy_a"));
            var e = Assert.Throws<InvalidDataException>(() => DbTableReader.Read([.. data, 0], Personalities, "test/derpy_long"));
            Assert.That(e!.Message, Does.Contain("test/derpy_long"));
        }

        [Test]
        public void AVersionNotInTheExtract_IsRefused()
        {
            var data = MadeUpDb.File(Personalities, 12, Row("derpy_a"));
            data[4] = 99;   // the version, after the four-byte marker
            var e = Assert.Throws<InvalidDataException>(() => DbTableReader.Read(data, Personalities, "test/derpy_v99"));
            Assert.That(e!.Message, Does.Contain("v99").And.Contain("test/derpy_v99"));
        }

        [Test]
        public void AKeyInTwoPacks_IsKeptOncePerFile_WithItsSource()
        {
            var runner = VanillaGame.Load(withDb: true);
            MadeUpDb.AddPack(runner, "derpy_test_mod", Personalities, "derpy_test_personalities", MadeUpDb.File(Personalities, 12, Row(CasketRider)));

            var copies = DbTables.Read(runner.PackFileService, Personalities).Where(x => x.Text("key") == CasketRider).ToList();

            Assert.That(copies.Select(x => x.File), Is.EqualTo(new[] { "db/battle_personalities_tables/data__", "db/battle_personalities_tables/derpy_test_personalities" }));
            Assert.That(copies[1].Pack, Is.EqualTo("derpy_test_mod"));
            Assert.That(copies[1].Text("variant"), Is.EqualTo("derpy_test_variant"));
        }
    }
}
