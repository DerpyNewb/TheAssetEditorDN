using System.IO;
using Editors.Shared.Core.Common;
using Editors.UnitSimulator;
using Editors.UnitSimulator.Personalities;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    // Personalities on a vanilla Casket of Souls and Warsphinx (spec section 6, test 5). Keys from CA's 9.0 db.pack.
    public class PersonalityTests
    {
        internal const string CasketModel = @"variantmeshes\variantmeshdefinitions\tmb_casket_of_souls.variantmeshdefinition";
        internal const string WarsphinxModel = @"variantmeshes\variantmeshdefinitions\tmb_khemrian_warsphinx.variantmeshdefinition";
        const string PriestModel = @"variantmeshes\variantmeshdefinitions\tmb_liche_priest.variantmeshdefinition";
        const string CovenThroneModel = @"variantmeshes\variantmeshdefinitions\vmp_coven_throne.variantmeshdefinition";
        internal const string CasketTech = @"warmachines\engines\tmb_casket_of_souls\tech\tmb_casket_of_souls_01.anim";
        internal const string CasketRider = "wh2_dlc09_tmb_casket_of_souls_rider1";
        internal const string Warsphinx = "wh2_dlc09_tmb_mon_khemrian_warsphinx_0";

        internal static (AssetEditorTestRunner Runner, UnitSimulatorViewModel Sim) OpenOn(string mountModel, AssetEditorTestRunner? loaded = null)
        {
            var runner = loaded ?? VanillaGame.Load(withDb: true);
            var sim = UnitSimulatorAppTests.Open(runner);
            SetMount(runner, sim, mountModel);
            return (runner, sim);
        }

        internal static void SetMount(AssetEditorTestRunner runner, UnitSimulatorViewModel sim, string model) =>
            runner.ScopeRepository.GetRequiredService<SceneObjectEditor>(sim).SetMesh(sim.Mount.Data, runner.PackFileService.FindFile(model)!);

        internal static PersonalityPart AddByKey(UnitSimulatorViewModel sim, string key)
        {
            sim.Personalities.Add(sim.Personalities.Rows.Copies(key).Last());
            return sim.Personalities.Parts.Single(x => x.Key == key);
        }

        // Where the part belongs while the mount stands as it does now (AttachBonesTests.OnAnimation).
        internal static Matrix OnMount(AssetEditorTestRunner runner, UnitSimulatorViewModel sim, string tech, string bone) =>
            AttachBonesTests.OnAnimation(new GameSkeleton(AnimationFile.Create(runner.PackFileService.FindFile(tech)!), null), bone, sim.Mount.Data.Skeleton);

        // Review Focus 4: the mount plays nothing, so the bone is where the bind pose has it.
        [Test]
        public void TheCasketsPriest_SitsOnApRiderPosition0_PlayingItsStand()
        {
            var (runner, sim) = OpenOn(CasketModel);
            Assert.That(sim.Personalities.TechSkeletons, Is.EqualTo(new[] { CasketTech }));
            Assert.That(sim.Personalities.BoneNames, Does.Contain("ap_riderposition_0"));

            var priest = AddByKey(sim, CasketRider);
            Assert.That(priest.Status, Is.Empty);
            Assert.That(priest.Bone, Is.EqualTo("ap_riderposition_0"));
            Assert.That(priest.LandUnit, Is.EqualTo("wh2_dlc09_tmb_art_casket_of_souls_0"));
            Assert.That(priest.Part.FragAndSlotSelection.FragmentList.SelectedItem?.Name, Is.EqualTo("hu1_liche_priest_staff_and_sword"));
            Assert.That(priest.Part.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("STAND"));
            Assert.That(sim.SceneObjects, Does.Contain(priest.Part));

            sim.Personalities.PlaceAll();
            var expected = OnMount(runner, sim, CasketTech, "ap_riderposition_0");
            Assert.That(AttachBonesTests.Off(priest.Part.Data.Offset, expected), Is.LessThan(0.001f));
            Assert.That(expected.Translation.Length(), Is.GreaterThan(0.1f));
        }

        // Review Focus 3.
        [Test]
        public void TheWarsphinxsSet_PutsFourCrewOnApCrewPositions1To4_OnTheFirstOfItsTwoTechSkeletons()
        {
            var (_, sim) = OpenOn(WarsphinxModel);
            Assert.That(sim.Personalities.TechSkeletons.Select(Path.GetFileName), Is.EqualTo(new[] { "tmb_warsphinx_01.anim", "tmb_warsphinx_02.anim" }));
            Assert.That(sim.Personalities.SelectedTechSkeleton, Does.EndWith("tmb_warsphinx_01.anim"));

            sim.Personalities.AddSet(Warsphinx);

            Assert.That(sim.Personalities.Parts.Select(x => x.Bone), Is.EquivalentTo(new[] { "ap_crewposition_1", "ap_crewposition_2", "ap_crewposition_3", "ap_crewposition_4" }));
            Assert.That(sim.Personalities.Parts.Select(x => x.LandUnit).Distinct(), Is.EqualTo(new[] { Warsphinx }));
            Assert.That(sim.Personalities.Parts.Select(x => x.Status), Is.All.Empty);
        }

        // TechSkeletonTests 1a: a tech folder also holds destruction animations. They are not offered, and the panel says so.
        [Test]
        public void ATechFoldersDestructionAnimations_AreNotOffered_AndAreNamed()
        {
            var (_, sim) = OpenOn(CovenThroneModel);
            Assert.That(sim.Personalities.TechSkeletons.Select(Path.GetFileName), Is.EqualTo(new[] { "vmp_coven_throne_01.anim" }));
            Assert.That(sim.Personalities.TechStatus, Does.Contain("vmp_coven_throne_01_destroyed").And.Contain("vmp_coven_throne_01_destruct"));
        }

        // Review Focus 5.
        [Test]
        public void ThePersonalityOnAlready_IsRefused_AndOneRemoved_CanBeAddedAgain()
        {
            var (_, sim) = OpenOn(CasketModel);
            var priest = AddByKey(sim, CasketRider);
            sim.Personalities.Add(sim.Personalities.Rows.Copies(CasketRider).Last());
            Assert.That(sim.Personalities.Parts, Has.Count.EqualTo(1));
            Assert.That(sim.Personalities.Status, Does.Contain(CasketRider).And.Contain("on already"));

            sim.Personalities.Remove(priest);
            Assert.That(sim.Personalities.Parts, Is.Empty);
            Assert.That(sim.SceneObjects, Does.Not.Contain(priest.Part));

            AddByKey(sim, CasketRider);
            Assert.That(sim.Personalities.Parts, Has.Count.EqualTo(1));
            Assert.That(sim.Personalities.Status, Is.Empty);
        }

        [Test]
        public void TwoPersonalities_MayShareOneBone()
        {
            var (_, sim) = OpenOn(WarsphinxModel);
            sim.Personalities.AddSet(Warsphinx);
            var (first, second) = (sim.Personalities.Parts[0], sim.Personalities.Parts[1]);
            second.Bone = first.Bone;
            sim.Personalities.PlaceAll();
            Assert.That(second.Status, Is.Empty);
            Assert.That(second.Part.Data.Offset, Is.EqualTo(first.Part.Data.Offset));
        }

        // A mod's table file the extract cannot decode (cut short here) is skipped and named, and the mount still loads.
        [Test]
        public void AnUnreadableDbFile_IsSkippedAndNamed_AndTheMountStillLoads()
        {
            var runner = VanillaGame.Load(withDb: true);
            var file = MadeUpDb.File(PersonalityRows.Personalities, 12, ["derpy_test_cut", "hu1_liche_priest_staff_and_sword", "", "", "", false, false, false]);
            MadeUpDb.AddPack(runner, "derpy_test_cut", PersonalityRows.Personalities, "derpy_test_cut", file[..^3]);

            var (_, sim) = OpenOn(CasketModel, runner);

            Assert.That(sim.Personalities.DbStatus, Does.Contain("derpy_test_cut"));
            Assert.That(sim.Personalities.TechSkeletons, Is.EqualTo(new[] { CasketTech }));
            Assert.That(sim.Personalities.Rows.Copies(CasketRider), Is.Not.Empty);
        }

        [Test]
        public void AVariantATableAndABoneNoPackHas_AreEachNamed()
        {
            var runner = VanillaGame.Load(withDb: true);
            MadeUpDb.AddPack(runner, "derpy_test_lost", PersonalityRows.Personalities, "derpy_test_lost",
                MadeUpDb.File(PersonalityRows.Personalities, 12, ["derpy_test_lost_rider", "derpy_no_such_table", "derpy_no_such_variant", "", "", false, true, false]));
            MadeUpDb.AddPack(runner, "derpy_test_lost_junction", PersonalityRows.Junctions, "derpy_test_lost",
                MadeUpDb.File(PersonalityRows.Junctions, 2, ["autonomous_rider", "ap_derpy_nowhere", "derpy_test_lost_rider", "derpy_test_lost_unit", -1, true]));
            var (_, sim) = OpenOn(CasketModel, runner);

            var lost = AddByKey(sim, "derpy_test_lost_rider");
            sim.Personalities.PlaceAll();

            Assert.That(lost.Status, Does.Contain("derpy_no_such_variant").And.Contain("derpy_no_such_table").And.Contain("ap_derpy_nowhere"));
            Assert.That(lost.Part.Data.Offset, Is.EqualTo(Matrix.Identity));
        }

        [Test]
        public void AMountModelNoVariantsRowGivesATechFolder_SaysSo()
        {
            var (_, sim) = OpenOn(PriestModel);
            Assert.That(sim.Personalities.TechSkeletons, Is.Empty);
            Assert.That(sim.Personalities.TechStatus, Does.Contain("tmb_liche_priest"));
        }

        // Review Focus 2.
        [Test]
        public void AMountModelSwapped_NamesTheBoneItLacks_AndTheOldModelPutsThePartBack()
        {
            var (runner, sim) = OpenOn(CasketModel);
            var priest = AddByKey(sim, CasketRider);

            SetMount(runner, sim, WarsphinxModel);
            sim.Personalities.PlaceAll();
            Assert.That(priest.Status, Does.Contain("ap_riderposition_0"));
            Assert.That(priest.Part.Data.Offset, Is.EqualTo(Matrix.Identity));

            SetMount(runner, sim, CasketModel);
            sim.Personalities.PlaceAll();
            Assert.That(priest.Status, Is.Empty);
            Assert.That(AttachBonesTests.Off(priest.Part.Data.Offset, OnMount(runner, sim, CasketTech, "ap_riderposition_0")), Is.LessThan(0.001f));
        }

        [Test]
        public void ThePlacement_RunsBetweenThePlayersAndTheSceneObjects()
        {
            var (_, sim) = OpenOn(CasketModel);
            var priest = AddByKey(sim, CasketRider);
            var placement = new PersonalityPlacement(sim.Personalities);
            Assert.That(placement.UpdateOrder, Is.GreaterThan((int)ComponentUpdateOrderEnum.Animation).And.LessThan((int)ComponentUpdateOrderEnum.Default));
            placement.Update(new GameTime());
            Assert.That(priest.Part.Data.Offset, Is.Not.EqualTo(Matrix.Identity));
        }
    }
}
