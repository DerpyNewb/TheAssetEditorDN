using Editors.Shared.Core.Common;
using Editors.Shared.Core.Services.RiderBake;
using GameWorld.Core.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Test.UnitSimulator
{
    // The batch on CA's knight and warhorse, both ends the same mount so the answer is known: every baked rider clip must come back
    // as CA's, every row keep its slot id, and what is saved must read back as the table and clips that were baked.
    public class RiderBakeBatchTests
    {
        [Test]
        public void TheKnightsTable_BakedOntoHisOwnHorse_SavesATableWhoseClipsAreCas()
        {
            var runner = VanillaGame.Load();
            var pfs = runner.PackFileService;
            var editable = runner.CreateOutputPack();
            var tables = VanillaGame.Tables(pfs);
            var knight = tables[VanillaGame.KnightTable];
            var horse = tables[VanillaGame.WarhorseTable];
            var riderSkeleton = RiderBakeMeasurementTests.Skeleton(pfs, knight.SkeletonName);
            var horseSkeleton = RiderBakeMeasurementTests.Skeleton(pfs, horse.SkeletonName);
            var mount = new RiderBakeTable(horse, new RiderBakeMount(horseSkeleton, RiderSeat.Bone(horseSkeleton, horseSkeleton.GetBoneIndexByName("spine_1"))));
            AnimationClip? Clip(string path, GameSkeleton skeleton) => pfs.FindFile(path) is { } file ? new AnimationClip(AnimationFile.Create(file), skeleton) : null;

            var output = RiderBakeBatch.Bake(knight, riderSkeleton, mount, mount, Matrix.Identity, "Derpy_Test_Knight", Clip);
            TestContext.Out.WriteLine($"{output.Anims.Count} clips baked; {string.Join("; ", output.Report.Where(x => !x.Contains("baked onto")).Take(8))}");

            // The table: a copy of the knight's under the new, lower-cased name, every row in place with its id, the mount named.
            Assert.That(output.Table.FileName, Is.EqualTo("animations/database/battle/bin/derpy_test_knight.bin"));
            Assert.That(output.Table.MountBin, Is.EqualTo(horse.Name));
            Assert.That(output.Table.AnimationTableEntries.Select(x => x.AnimationId), Is.EqualTo(knight.AnimationTableEntries.Select(x => x.AnimationId)));
            Assert.That(output.Anims, Has.Count.GreaterThan(20));

            // Each baked row names a new file, and that file plays as CA's clip it replaced. A row not baked names CA's file still.
            var baked = output.Anims.ToDictionary(x => x.Path, x => x.Bytes);
            var worst = 0f;
            for (var r = 0; r < knight.AnimationTableEntries.Count; r++)
            {
                for (var v = 0; v < knight.AnimationTableEntries[r].AnimationRefs.Count; v++)
                {
                    var was = knight.AnimationTableEntries[r].AnimationRefs[v].AnimationFile;
                    var now = output.Table.AnimationTableEntries[r].AnimationRefs[v].AnimationFile;
                    if (!baked.TryGetValue(now, out var bytes))
                    {
                        Assert.That(now, Is.EqualTo(was));
                        continue;
                    }
                    var ca = Clip(was, riderSkeleton)!;
                    var mine = new AnimationClip(AnimationFile.Create(new Shared.ByteParsing.ByteChunk(bytes)), riderSkeleton);
                    Assert.That(mine.DynamicFrames, Has.Count.EqualTo(ca.DynamicFrames.Count), now);
                    for (var f = 0; f < ca.DynamicFrames.Count; f++)
                    {
                        var a = AnimationSampler.Sample(f, 0, riderSkeleton, ca);
                        var b = AnimationSampler.Sample(f, 0, riderSkeleton, mine);
                        for (var bone = 0; bone < riderSkeleton.BoneCount; bone++)
                            worst = Math.Max(worst, (a.GetSkeletonAnimatedWorld(riderSkeleton, bone).Translation - b.GetSkeletonAnimatedWorld(riderSkeleton, bone).Translation).Length());
                    }
                }
            }
            TestContext.Out.WriteLine($"worst bone over every baked clip and frame: {worst * 1000:0.00} mm");
            Assert.That(worst, Is.LessThan(0.002f));

            // Saved: the table reads back from the editable pack's own animpack as baked.
            var saver = runner.ServiceProvider.GetRequiredService<IFileSaveService>();
            Assert.That(AnimTableEdits.SaveNew(pfs, saver, output.Table), Is.Null);
            var animPack = pfs.FindFile(AnimTableEdits.OwnAnimPackPath(editable), editable)!.DataSource.ReadData();
            var entry = AnimTableEdits.ReadEntries(animPack).Single(x => x.Name == output.Table.FileName);
            Assert.That(entry.Data, Is.EqualTo(output.Table.ToByteArray()));
            // Saved twice, the table is replaced, not added again.
            Assert.That(AnimTableEdits.SaveNew(pfs, saver, output.Table), Is.Null);
            Assert.That(AnimTableEdits.ReadEntries(pfs.FindFile(AnimTableEdits.OwnAnimPackPath(editable), editable)!.DataSource.ReadData()).Count(x => x.Name == output.Table.FileName), Is.EqualTo(1));
        }

        // Spec section 3: the Mount Animation Creator named slots by the Warhammer 2 list. How many of the warhorse's slots that list
        // names wrongly or not at all: the reason the batch reads ids from the table and names from the 9.0 list.
        [Test]
        public void TheWarhammer2SlotList_MisnamesTheWarhorsesSlots()
        {
            var horse = VanillaGame.Tables(VanillaGame.Load().PackFileService)[VanillaGame.WarhorseTable];
            var ids = horse.AnimationTableEntries.Select(x => (int)x.AnimationId).Distinct().ToList();
            var wrong = ids.Count(id => DefaultAnimationSlotTypeHelper.GetInstance().TryGetFromId(id)?.Value != AnimationSlotTypeHelperWh3.GetFromId(id).Value);
            TestContext.Out.WriteLine($"{wrong} of the warhorse's {ids.Count} slot ids have another name, or none, in the Warhammer 2 list");
            Assert.That(wrong, Is.GreaterThan(0));
        }
    }
}
