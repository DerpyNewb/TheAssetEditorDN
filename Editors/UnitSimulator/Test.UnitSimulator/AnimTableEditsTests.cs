using Editors.UnitSimulator.Tables;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

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

        // AnimationBinWh3 writes every string lower-cased, and CA left capitals in two mounted tables' anim paths: Katarin's
        // (.../horse01/Katarin/...) and the Kislev Patriarch's (.../bear01/Patriarch/...). Every other byte of every table comes back as it was.
        [Test]
        public void EveryVanillaMountedTable_ReadAndWrittenBack_IsByteIdentical_ButForCapitals()
        {
            var pfs = VanillaGame.Load().PackFileService;
            var tested = new HashSet<string>();
            var recased = new HashSet<string>();
            foreach (var animPack in new[] { "animation_tables.animpack", "animation_tables__.animpack", "animation_tables__3.animpack" })
                foreach (var (name, data) in AnimTableEdits.ReadEntries(Bytes(pfs, "animations/database/battle/bin/" + animPack))
                             .Where(x => x.Name.Contains("/battle/bin/") && x.Name.EndsWith(".bin") && !x.Name.Contains("matched_combat")))
                {
                    var table = new AnimationBinWh3(name, data);
                    if (table.MountBin == "")
                        continue;
                    var written = table.ToByteArray();
                    Assert.That(written, Has.Length.EqualTo(data.Length), name);
                    var changed = Enumerable.Range(0, data.Length).Where(i => written[i] != data[i]).ToList();
                    Assert.That(changed.All(i => data[i] >= 'A' && data[i] <= 'Z' && written[i] == data[i] + 32), name);
                    if (changed.Count != 0)
                        recased.Add(name);
                    tested.Add(name);
                }
            Assert.That(tested, Has.Count.EqualTo(376));
            Assert.That(recased, Is.EquivalentTo(new[]
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
            AnimTableEdits.SetMountBin(knight, "derpy_test_mount");

            var after = AnimTableEdits.ReadEntries(AnimTableEdits.Splice(bytes, VanillaGame.KnightTable, knight.ToByteArray())).ToList();
            Assert.That(after.Select(x => x.Name), Is.EqualTo(before.Select(x => x.Name)));
            for (var i = 0; i < before.Count; i++)
                if (before[i].Name != VanillaGame.KnightTable)
                    Assert.That(after[i].Data, Is.EqualTo(before[i].Data), before[i].Name);
            Assert.That(new AnimationBinWh3(VanillaGame.KnightTable, after.Single(x => x.Name == VanillaGame.KnightTable).Data).MountBin, Is.EqualTo("derpy_test_mount"));
        }

        [Test]
        public void SetMountBin_LeavesEveryRowAlone()
        {
            var table = Rider();
            var rows = Rows(table);
            AnimTableEdits.SetMountBin(table, "other");
            Assert.That((table.MountBin, Rows(table)), Is.EqualTo(("other", rows)));
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

        [Test]
        public void Save_RefusesACaPacksTable_AndWritesNothing()
        {
            var pfs = VanillaGame.Load(asCaPacks: true).PackFileService;
            var saver = new Mock<IFileSaveService>();
            var problem = AnimTableEdits.Save(pfs, saver.Object, VanillaGame.Tables(pfs)[VanillaGame.KnightTable]);
            Assert.That(problem, Does.Contain("CA pack"));
            saver.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);
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

            AnimTableEdits.SetMountBin(tables[VanillaGame.KnightTable], "derpy_first");
            Assert.That(AnimTableEdits.Save(pfs, saver, tables[VanillaGame.KnightTable]), Is.Null);
            AnimTableEdits.SetMountBin(tables[VanillaGame.WarhorseTable], "derpy_second");
            Assert.That(AnimTableEdits.Save(pfs, saver, tables[VanillaGame.WarhorseTable]), Is.Null);

            var saved = AnimTableEdits.ReadEntries(pfs.FindFile(VanillaGame.TablesAnimPack, editable)!.DataSource.ReadData()).ToDictionary(x => x.Name, x => x.Data);
            Assert.That(new AnimationBinWh3(VanillaGame.KnightTable, saved[VanillaGame.KnightTable]).MountBin, Is.EqualTo("derpy_first"));
            Assert.That(new AnimationBinWh3(VanillaGame.WarhorseTable, saved[VanillaGame.WarhorseTable]).MountBin, Is.EqualTo("derpy_second"));
        }

        [Test]
        public void Save_WritesTheAnimpackIntoTheEditablePack()
        {
            var runner = VanillaGame.Load();
            var pfs = runner.PackFileService;
            var editable = runner.CreateOutputPack();
            var knight = VanillaGame.Tables(pfs)[VanillaGame.KnightTable];
            AnimTableEdits.SetMountBin(knight, "derpy_test_mount");

            Assert.That(AnimTableEdits.Save(pfs, runner.ServiceProvider.GetRequiredService<IFileSaveService>(), knight), Is.Null);

            var saved = pfs.FindFile(VanillaGame.TablesAnimPack, editable)!.DataSource.ReadData();
            var entry = AnimTableEdits.ReadEntries(saved).Single(x => x.Name == VanillaGame.KnightTable).Data;
            Assert.That(new AnimationBinWh3(VanillaGame.KnightTable, entry).MountBin, Is.EqualTo("derpy_test_mount"));
        }
    }
}
