using Editors.Shared.Core.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    public class AnimTableEditsTests
    {
        // Every row as text, to see what an edit left alone.
        static string Rows(AnimationBinWh3 t) => string.Join("\n", t.AnimationTableEntries.Select(r =>
            $"{r.AnimationId} {r.BlendIn} {r.SelectionWeight} {r.WeaponBools} {r.Unk} " +
            string.Join("|", r.AnimationRefs.Select(a => $"{a.AnimationFile},{a.AnimationMetaFile},{a.AnimationSoundMetaFile}"))));

        static AnimationBinWh3 Rider() =>
            MadeUpTables.Table("r", "humanoid01", "m", ("STAND", "r_stand.anim"), ("RIDER_ATTACK_1", "r_a1.anim"), ("RIDER_ATTACK_3", "r_a3.anim"));

        static byte[] Bytes(IPackFileService pfs, string path) => pfs.FindFile(path)!.DataSource.ReadData();

        [Test]
        public void AllByName_HoldsEachTableOnce_UnderItsName()
        {
            var tables = AnimTableEdits.AllByName(VanillaGame.Load().PackFileService);
            Assert.That(tables["hu1_empire_hr1_warhorse_lance_sword_shield"].FullPath, Is.EqualTo(VanillaGame.KnightTable));
            Assert.That(tables["hr1_warhorse"].FullPath, Is.EqualTo(VanillaGame.WarhorseTable));
        }

        // Every mounted table comes back byte for byte, the two with capitals in their anim paths included: Katarin's
        // (.../horse01/Katarin/...) and the Kislev Patriarch's (.../bear01/Patriarch/...), which the writer used to lower-case.
        [Test]
        public void EveryVanillaMountedTable_ReadAndWrittenBack_IsByteIdentical()
        {
            var pfs = VanillaGame.Load().PackFileService;
            var tested = new HashSet<string>();
            foreach (var animPack in new[] { "animation_tables.animpack", "animation_tables__.animpack", "animation_tables__3.animpack" })
                foreach (var (name, data) in AnimTableEdits.ReadEntries(Bytes(pfs, "animations/database/battle/bin/" + animPack))
                             .Where(x => x.Name.Contains("/battle/bin/") && x.Name.EndsWith(".bin") && !x.Name.Contains("matched_combat")))
                {
                    var table = new AnimationBinWh3(name, data);
                    if (table.MountBin == "")
                        continue;
                    Assert.That(table.ToByteArray(), Is.EqualTo(data), name);
                    tested.Add(name);
                }
            Assert.That(tested, Has.Count.EqualTo(376));
            Assert.That(tested, Is.SupersetOf(new[]
            {
                "animations/database/battle/bin/hu1_wh3_katarin_hr1_warhorse_staff_and_sword.bin",
                "animations/database/battle/bin/hu1_wh3_kislev_be1_warbear_mace_and_satchel.bin",
            }));
        }

        [Test]
        public void Splicing_AnEntrysOwnBytes_GivesTheAnimpackBack()
        {
            var bytes = Bytes(VanillaGame.Load().PackFileService, VanillaGame.TablesAnimPack);
            var knight = AnimTableEdits.ReadEntries(bytes).Single(x => x.Name == VanillaGame.KnightTable).Data;
            Assert.That(AnimTableEdits.Splice(bytes, VanillaGame.KnightTable, knight), Is.EqualTo(bytes));
        }

        [Test]
        public void Splicing_AnEditedTable_ChangesThatEntryAlone()
        {
            var bytes = Bytes(VanillaGame.Load().PackFileService, VanillaGame.TablesAnimPack);
            var before = AnimTableEdits.ReadEntries(bytes).ToList();
            var knight = new AnimationBinWh3(VanillaGame.KnightTable, before.Single(x => x.Name == VanillaGame.KnightTable).Data);
            AnimTableEdits.SetBlendAndWeight(knight, 1901, 0.5f, 3);

            var after = AnimTableEdits.ReadEntries(AnimTableEdits.Splice(bytes, VanillaGame.KnightTable, knight.ToByteArray())).ToList();
            Assert.That(after.Select(x => x.Name), Is.EqualTo(before.Select(x => x.Name)));
            for (var i = 0; i < before.Count; i++)
                if (before[i].Name != VanillaGame.KnightTable)
                    Assert.That(after[i].Data, Is.EqualTo(before[i].Data), before[i].Name);
            Assert.That(AnimTableEdits.Row(new AnimationBinWh3(VanillaGame.KnightTable, after.Single(x => x.Name == VanillaGame.KnightTable).Data), 1901).BlendIn, Is.EqualTo(0.5f));
        }

        [Test]
        public void SetFiles_ChangesThatSlotsFilesAlone()
        {
            var table = Rider();
            var slot = ((IAnimationBinGenericFormat)table).Entries.Single(x => x.SlotName == "RIDER_ATTACK_1");
            var others = Rows(table).Split('\n').Where(x => !x.StartsWith("1901 ")).ToList();

            AnimTableEdits.SetFiles(table, slot, "new.anim", "new.anm.meta", "new.snd.meta");

            var row = AnimTableEdits.Row(table, 1901).AnimationRefs.Single();
            Assert.That((row.AnimationFile, row.AnimationMetaFile, row.AnimationSoundMetaFile), Is.EqualTo(("new.anim", "new.anm.meta", "new.snd.meta")));
            Assert.That(Rows(table).Split('\n').Where(x => !x.StartsWith("1901 ")), Is.EqualTo(others));
        }

        [Test]
        public void SetBlendAndWeight_ChangesThatRowAlone()
        {
            var table = Rider();
            AnimTableEdits.SetBlendAndWeight(table, 1901, 0.5f, 3);
            Assert.That((AnimTableEdits.Row(table, 1901).BlendIn, AnimTableEdits.Row(table, 1901).SelectionWeight), Is.EqualTo((0.5f, 3f)));
            Assert.That(AnimTableEdits.Row(table, 1).BlendIn, Is.EqualTo(0.2f));
        }

        [Test]
        public void AddSlot_CopiesTheRow_InIdOrder()
        {
            var table = Rider();
            AnimTableEdits.AddSlot(table, "RIDER_ATTACK_2", AnimTableEdits.Row(table, 1901));
            Assert.That(table.AnimationTableEntries.Select(x => x.AnimationId), Is.Ordered);
            var added = AnimTableEdits.Row(table, AnimationSlotTypeHelperWh3.GetfromValue("RIDER_ATTACK_2").Id);
            Assert.That(added.AnimationRefs.Single().AnimationFile, Is.EqualTo("r_a1.anim"));
            Assert.That(added.AnimationRefs, Is.Not.SameAs(AnimTableEdits.Row(table, 1901).AnimationRefs));
        }

        [Test]
        public void AddSlot_RefusesASlotTheTableHas() =>
            Assert.Throws<ArgumentException>(() => { var t = Rider(); AnimTableEdits.AddSlot(t, "RIDER_ATTACK_1", AnimTableEdits.Row(t, 1901)); });

        // A CA table is neither refused nor saved with its whole animpack: it goes alone into an animpack of the editable pack's own,
        // named after the pack, and a second CA table joins it there with the first kept as saved. CA's animpack is not copied.
        [Test]
        public void Save_PutsCaTablesAloneIntoAnAnimpackOfTheEditablePacksOwn()
        {
            var runner = new AssetEditorTestRunner();
            var pfs = runner.PackFileService;
            const string CaAnimPack = "animations/database/battle/bin/animation_tables.animpack";
            var ca = runner.CreateEmptyPackFile("ca_anim", false);
            pfs.AddFilesToPack(ca, [new NewPackFileEntry(@"animations\database\battle\bin",
                PackFile.CreateFromBytes("animation_tables.animpack", MadeUpTables.AnimPack(Rider(), MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim")))))]);
            ca.IsCaPackFile = true;
            var editable = runner.CreateEmptyPackFile("!!My Mod", true);
            var saver = runner.ServiceProvider.GetRequiredService<IFileSaveService>();
            var tables = AnimationPackSerializer.Load(pfs.FindFile(CaAnimPack)!, pfs).Files.OfType<AnimationBinWh3>().ToDictionary(x => x.Name);
            var (rider, mount) = (tables["r"], tables["m"]);

            AnimTableEdits.SetBlendAndWeight(rider, 1901, 0.5f, 3);
            Assert.That(AnimTableEdits.Save(pfs, saver, rider), Is.Null);
            AnimTableEdits.SetBlendAndWeight(mount, (int)mount.AnimationTableEntries[0].AnimationId, 0.75f, 2);
            Assert.That(AnimTableEdits.Save(pfs, saver, mount), Is.Null);

            var own = pfs.FindFile("animations/database/battle/bin/!!my_mod_animation_tables.animpack", editable);
            Assert.That((own != null, pfs.FindFile(CaAnimPack, editable)), Is.EqualTo((true, (PackFile?)null)));
            var entries = AnimTableEdits.ReadEntries(own!.DataSource.ReadData()).ToList();
            Assert.That(entries.Select(x => (x.Name, x.Data)), Is.EqualTo(new[] { (rider.FileName, rider.ToByteArray()), (mount.FileName, mount.ToByteArray()) }));
        }

        [Test]
        public void Save_WithNoEditablePack_SaysSo()
        {
            var pfs = VanillaGame.Load().PackFileService;
            Assert.That(AnimTableEdits.Save(pfs, new Mock<IFileSaveService>().Object, VanillaGame.Tables(pfs)[VanillaGame.KnightTable]), Is.EqualTo("No editable pack is selected."));
        }

        // Two tables of one animpack saved one after the other, with a pack that also holds the animpack loaded after the editable one
        // (an animation mod carrying CA's animpack): the second save has to start from the editable pack's copy, which holds the first.
        [Test]
        public void Save_StartsFromTheEditablePacksCopy_WhenAPackLoadedLaterHasTheAnimpack()
        {
            var runner = VanillaGame.Load();
            var pfs = runner.PackFileService;
            var editable = runner.CreateOutputPack();
            var mod = runner.CreateEmptyPackFile("derpy_test_animation_mod", false);
            pfs.AddFilesToPack(mod, [new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes("animation_tables__.animpack", Bytes(pfs, VanillaGame.TablesAnimPack)))]);
            var saver = runner.ServiceProvider.GetRequiredService<IFileSaveService>();
            var tables = VanillaGame.Tables(pfs);

            AnimTableEdits.SetBlendAndWeight(tables[VanillaGame.KnightTable], 1901, 0.5f, 3);
            Assert.That(AnimTableEdits.Save(pfs, saver, tables[VanillaGame.KnightTable]), Is.Null);
            var horseSlot = (int)tables[VanillaGame.WarhorseTable].AnimationTableEntries[0].AnimationId;
            AnimTableEdits.SetBlendAndWeight(tables[VanillaGame.WarhorseTable], horseSlot, 0.75f, 2);
            Assert.That(AnimTableEdits.Save(pfs, saver, tables[VanillaGame.WarhorseTable]), Is.Null);

            var saved = AnimTableEdits.ReadEntries(pfs.FindFile(VanillaGame.TablesAnimPack, editable)!.DataSource.ReadData()).ToDictionary(x => x.Name, x => x.Data);
            Assert.That(AnimTableEdits.Row(new AnimationBinWh3(VanillaGame.KnightTable, saved[VanillaGame.KnightTable]), 1901).BlendIn, Is.EqualTo(0.5f));
            Assert.That(AnimTableEdits.Row(new AnimationBinWh3(VanillaGame.WarhorseTable, saved[VanillaGame.WarhorseTable]), horseSlot).BlendIn, Is.EqualTo(0.75f));
        }

        [Test]
        public void Save_WritesTheAnimpackIntoTheEditablePack()
        {
            var runner = VanillaGame.Load();
            var pfs = runner.PackFileService;
            var editable = runner.CreateOutputPack();
            var knight = VanillaGame.Tables(pfs)[VanillaGame.KnightTable];
            AnimTableEdits.SetBlendAndWeight(knight, 1901, 0.5f, 3);

            Assert.That(AnimTableEdits.Save(pfs, runner.ServiceProvider.GetRequiredService<IFileSaveService>(), knight), Is.Null);

            var saved = pfs.FindFile(VanillaGame.TablesAnimPack, editable)!.DataSource.ReadData();
            var entry = AnimTableEdits.ReadEntries(saved).Single(x => x.Name == VanillaGame.KnightTable).Data;
            Assert.That(AnimTableEdits.Row(new AnimationBinWh3(VanillaGame.KnightTable, entry), 1901).BlendIn, Is.EqualTo(0.5f));
        }

        // The check before an animpack replaces the pack's: it passes a good splice and stops one that changed another entry or
        // lost one, so a bad write never goes in.
        [Test]
        public void Verify_PassesAGoodSplice_AndStopsABadOne()
        {
            var tables = MadeUpTables.AnimPack(Rider(), MadeUpTables.Table("m", "horse01", "", ("ATTACK_1", "m_a1.anim")));
            var rider = Rider();
            AnimTableEdits.SetBlendAndWeight(rider, 1901, 0.7f, 2);
            var data = rider.ToByteArray();
            var good = AnimTableEdits.Splice(tables, rider.FileName, data);
            Assert.DoesNotThrow(() => AnimTableEdits.Verify(tables, good, rider.FileName, data));

            var otherEntryChanged = (byte[])good.Clone();
            otherEntryChanged[^1] ^= 1;
            Assert.Throws<System.IO.InvalidDataException>(() => AnimTableEdits.Verify(tables, otherEntryChanged, rider.FileName, data));
            var entryLost = AnimTableEdits.Splice(MadeUpTables.AnimPack(Rider()), rider.FileName, data);
            Assert.Throws<System.IO.InvalidDataException>(() => AnimTableEdits.Verify(tables, entryLost, rider.FileName, data));
        }
    }
}
