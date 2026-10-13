using System.IO;
using Editors.UnitSimulator.Personalities;

namespace Test.UnitSimulator
{
    public class PersonalityExportTests
    {
        // Copied from RPFM's own exports in the workspace, .skilltree_cache/extract/db/<table>/derpy_chd_ll_mount.tsv, with the name changed.
        const string PersonalitiesHeader = "key\tman_animations_table\tvariant\tbattle_entity\tbattle_entity_stats\tautonomous_rider_hero\tautonomous_rider_can_shoot_in_melee\tcan_only_be_used_by_draught\n" +
            "#battle_personalities_tables;12;db/battle_personalities_tables/derpy_test\t\t\t\t\t\t\t\n";
        const string JunctionsHeader = "battle_personality\tland_unit\tattach\triders_attachment_point\tengine_articulation_index\tjettison_rider\n" +
            "#land_units_to_battle_personalities_junctions_tables;2;db/land_units_to_battle_personalities_junctions_tables/derpy_test\t\t\t\t\t\n";

        static string NewFolder() => Path.Combine(TestContext.CurrentContext.WorkDirectory, "export_" + Guid.NewGuid().ToString("N"));
        static string Personalities(string folder) => File.ReadAllText(Path.Combine(folder, PersonalityRows.Personalities, "derpy_test.tsv"));
        static string Junctions(string folder) => File.ReadAllText(Path.Combine(folder, PersonalityRows.Junctions, "derpy_test.tsv"));
        static string? Export(Editors.UnitSimulator.UnitSimulatorViewModel sim, string folder) =>
            PersonalityExport.Write(folder, "derpy_test", sim.Personalities.Parts, sim.Personalities.Rows.ExportVersion(PersonalityRows.Personalities), sim.Personalities.Rows.ExportVersion(PersonalityRows.Junctions));

        [Test]
        public void TheCasketsPriest_IsWrittenAsRpfmWritesIt()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            var folder = NewFolder();

            Assert.That(Export(sim, folder), Is.Null);

            Assert.That(Personalities(folder), Is.EqualTo(PersonalitiesHeader +
                "wh2_dlc09_tmb_casket_of_souls_rider1\thu1_liche_priest_staff_and_sword\twh2_dlc09_tmb_liche_priest_casket\twh_main_infantry_rider\t\tfalse\ttrue\tfalse\n"));
            Assert.That(Junctions(folder), Is.EqualTo(JunctionsHeader +
                "wh2_dlc09_tmb_casket_of_souls_rider1\twh2_dlc09_tmb_art_casket_of_souls_0\tautonomous_rider\tap_riderposition_0\t-1\ttrue\n"));
        }

        [Test]
        public void AnEditedRow_IsWrittenWithItsEdits()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            var priest = PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            (priest.Key, priest.LandUnit, priest.Attach, priest.JettisonRider, priest.EngineArticulationIndex) = ("derpy_test_priest", "derpy_test_unit", "officer", false, 2);
            var folder = NewFolder();

            Assert.That(Export(sim, folder), Is.Null);

            Assert.That(Personalities(folder), Does.EndWith("derpy_test_priest\thu1_liche_priest_staff_and_sword\twh2_dlc09_tmb_liche_priest_casket\twh_main_infantry_rider\t\tfalse\ttrue\tfalse\n"));
            Assert.That(Junctions(folder), Does.EndWith("derpy_test_priest\tderpy_test_unit\tofficer\tap_riderposition_0\t2\tfalse\n"));
        }

        [Test]
        public void AJunctionRowWithNoLandUnit_IsRefused_AndNothingIsWritten()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider).LandUnit = " ";
            var folder = NewFolder();
            Assert.That(Export(sim, folder), Does.Contain(PersonalityTests.CasketRider).And.Contain("land_unit"));
            Assert.That(Directory.Exists(folder), Is.False);
        }

        [Test]
        public void OnePersonalityTwiceOnOneLandUnit_IsRefused()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.WarsphinxModel);
            sim.Personalities.AddSet(PersonalityTests.Warsphinx);
            sim.Personalities.Parts[1].Key = sim.Personalities.Parts[0].Key;
            Assert.That(Export(sim, NewFolder()), Does.Contain(sim.Personalities.Parts[0].Key).And.Contain(PersonalityTests.Warsphinx));
        }

        [Test]
        public void AVersionTheExtractLacks_IsRefused()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel);
            PersonalityTests.AddByKey(sim, PersonalityTests.CasketRider);
            Assert.That(PersonalityExport.Write(NewFolder(), "derpy_test", sim.Personalities.Parts, 99, 2), Does.Contain("v99"));
        }

        // Review Focus 1: a v9 row (with type and rider_attachment_point, without the last two flags) written at v12.
        [Test]
        public void ARowFromAnOlderVersion_IsWrittenAtTheNewest_TheMissingFlagsFalse()
        {
            var runner = VanillaGame.Load(withDb: true);
            MadeUpDb.AddPack(runner, "derpy_test_old", PersonalityRows.Personalities, "derpy_test_old",
                MadeUpDb.File(PersonalityRows.Personalities, 9, ["derpy_test_old", "hu1_liche_priest_staff_and_sword", "", "wh2_dlc09_tmb_liche_priest_casket", "", "ap_riderposition_0", "", true]));
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.CasketModel, runner);
            PersonalityTests.AddByKey(sim, "derpy_test_old").LandUnit = "derpy_test_unit";
            var folder = NewFolder();

            Assert.That(Export(sim, folder), Is.Null);

            Assert.That(Personalities(folder), Is.EqualTo(PersonalitiesHeader +
                "derpy_test_old\thu1_liche_priest_staff_and_sword\twh2_dlc09_tmb_liche_priest_casket\t\t\ttrue\tfalse\tfalse\n"));
        }

        // Review Focus 5.
        [Test]
        public void ARemovedPersonality_IsNotWritten()
        {
            var (_, sim) = PersonalityTests.OpenOn(PersonalityTests.WarsphinxModel);
            sim.Personalities.AddSet(PersonalityTests.Warsphinx);
            var gone = sim.Personalities.Parts[0];
            sim.Personalities.Remove(gone);
            var folder = NewFolder();

            Assert.That(Export(sim, folder), Is.Null);

            Assert.That(Junctions(folder), Does.Not.Contain(gone.Key));
            Assert.That(Junctions(folder).Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(2 + 3));
        }
    }
}
