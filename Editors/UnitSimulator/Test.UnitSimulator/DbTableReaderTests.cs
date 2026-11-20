using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.DB;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    public class DbTableReaderTests
    {
        const string Personalities = "battle_personalities_tables";
        const string CasketRider = "wh2_dlc09_tmb_casket_of_souls_rider1";

        // A v12 battle_personalities row: key, table, variant, battle_entity, battle_entity_stats and the three flags.
        static object[] Row(string key, string variant = "derpy_test_variant") => [key, "hu1_test_table", variant, "", "", false, true, false];

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

        // A mod's row beats CA's although derpy_ sorts after data__ (measured in game 2026-11-18).
        [Test]
        public void AKeyInTwoPacks_IsKeptOncePerFile_WithItsSource_TheModsCopyFirst()
        {
            var runner = VanillaGame.Load(asCaPacks: true, withDb: true);
            MadeUpDb.AddPack(runner, "derpy_test_mod", Personalities, "derpy_test_personalities", MadeUpDb.File(Personalities, 12, Row(CasketRider)));

            var copies = DbTables.Read(runner.PackFileService, Personalities).Where(x => x.Text("key") == CasketRider).ToList();

            Assert.That(copies.Select(x => x.File), Is.EqualTo(new[] { "db/battle_personalities_tables/derpy_test_personalities", "db/battle_personalities_tables/data__" }));
            Assert.That(copies[0].Pack, Is.EqualTo("derpy_test_mod"));
            Assert.That(copies[0].Text("variant"), Is.EqualTo("derpy_test_variant"));
        }

        // The in-game test of 2026-11-18 (docs/MOD_STRUCTURE.md, "Which copy of a DB row wins"), with the variant column marking
        // each copy. The packs load in the opposite order to their names: the mod list is not known, so the names rank them.
        [Test]
        public void TheCopyTheGameKeeps_ComesFirst()
        {
            var runner = new AssetEditorTestRunner();
            Pack(runner, "zzz_dbtest_z", ("aaa_dbtest_cross", [Row("u2", "z_aaa")]), ("dbtest_samepath", [Row("u4", "z_samepath"), Row("u5", "z_samepath")]));
            Pack(runner, "!!dbtest_a", ("aaa_dbtest_samepack", [Row("u1", "a_aaa")]), ("zzz_dbtest_samepack", [Row("u1", "a_zzz")]),
                ("zzz_dbtest_cross", [Row("u2", "a_zzz")]), ("dbtest_samepath", [Row("u4", "a_samepath")]));

            var rows = DbTables.Read(runner.PackFileService, Personalities);
            string[] Copies(string key) => [.. rows.Where(x => x.Text("key") == key).Select(x => x.Text("variant"))];

            Assert.Multiple(() =>
            {
                Assert.That(Copies("u1"), Is.EqualTo(new[] { "a_aaa", "a_zzz" }), "in one pack, the file whose name sorts first");
                Assert.That(Copies("u2"), Is.EqualTo(new[] { "z_aaa", "a_zzz" }), "the file's name decides, not its pack's");
                Assert.That(Copies("u4"), Is.EqualTo(new[] { "a_samepath" }), "one path in two packs: the first pack keeps its whole file");
                Assert.That(Copies("u5"), Is.Empty, "and the other pack's rows at that path are gone");
            });
        }

        // The extract's only F64 column is persistent_one_shot_duration_override (audio_metadata_tags_tables v21 and v22); v22 has a text column after it.
        // A reader that took the double for four bytes would land the next cell in the middle of it and fail the length check.
        [Test]
        public void ReadsAnF64Column()
        {
            const string Tags = "audio_metadata_tags_tables";
            object[] Tag(string name) => [.. DbSchema.Versions(Tags)[22].Select<DbField, object>(x => x.Name switch
            {
                "key" => "derpy_tag",
                "persistent_one_shot_duration_override" => 1.25d,
                "vocalisation_bank_split" => "derpy_bank",
                _ => x.Type switch { "Boolean" => false, "I32" => 0, "F32" => 0f, _ => "" },
            })];

            var (version, rows) = DbTableReader.Read(MadeUpDb.File(Tags, 22, Tag("derpy_tag")), Tags, "test/derpy_f64");

            Assert.That(version, Is.EqualTo(22));
            Assert.That(rows.Single()["persistent_one_shot_duration_override"], Is.EqualTo(1.25d));
            Assert.That(rows.Single()["vocalisation_bank_split"], Is.EqualTo("derpy_bank"));
        }

        static void Pack(AssetEditorTestRunner runner, string name, params (string File, object[][] Rows)[] files)
        {
            var pack = runner.CreateEmptyPackFile(name, false);
            runner.PackFileService.AddFilesToPack(pack,
                [.. files.Select(x => new NewPackFileEntry(@"db\" + Personalities, PackFile.CreateFromBytes(x.File, MadeUpDb.File(Personalities, 12, x.Rows))))]);
        }
    }
}
