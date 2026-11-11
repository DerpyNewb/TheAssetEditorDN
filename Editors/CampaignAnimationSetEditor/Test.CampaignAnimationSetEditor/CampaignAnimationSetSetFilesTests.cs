using System;
using System.IO;
using System.Linq;
using Shared.ByteParsing;
using Shared.GameFormats.AnimationPack;

namespace Test.CampaignAnimationSetEditor
{
    // SuperView edits a campaign set through its slots, so a slot has to write back into the row it was read from. Each slot of
    // two vanilla sets is given new files in turn: that slot alone changes, and the set written and read back says so.
    [TestFixture]
    public class CampaignAnimationSetSetFilesTests
    {
        static (string, string, string) Files(AnimationBinEntryGenericFormat x) => (x.AnimationFile, x.MetaFile, x.SoundFile);

        [TestCase("cam_bi2_dlc27_preyton_boss.bin")]
        [TestCase("cam_hu6_gor_axe.bin")]
        public void EachSlot_WritesIntoItsOwnRow(string fixtureName)
        {
            var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixtureName));
            CampaignAnimationSet Read(byte[] data) => new("animations/campaign/database/bin/" + fixtureName, CampaignAnimationBinLoader.Load(new ByteChunk(data)));
            var original = Read(bytes).Entries;
            Assert.That(original.Select(x => x.SlotName).Distinct().Count(), Is.GreaterThan(3), "too few kinds of row to test");

            for (var k = 0; k < original.Count; k++)
            {
                var set = Read(bytes);
                set.SetFiles(set.Entries[k], $"a{k}", $"m{k}", $"s{k}");
                var written = Read(CampaignAnimationBinLoader.Write(set.Bin, Path.GetFileNameWithoutExtension(fixtureName))).Entries;
                var expected = original.Select((x, i) => i == k ? ($"a{k}", $"m{k}", $"s{k}") : Files(x));
                Assert.That(written.Select(Files), Is.EqualTo(expected), $"slot {k}, {original[k].DisplayName}");
            }
        }

        // A slot read before an edit no longer names what its row does, so it is refused rather than written somewhere else.
        [Test]
        public void ASlotTheSetNoLongerHas_IsRefused()
        {
            var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "cam_hu6_gor_axe.bin"));
            var set = new CampaignAnimationSet("x.bin", CampaignAnimationBinLoader.Load(new ByteChunk(bytes)));
            var slot = set.Entries[0];
            set.SetFiles(slot, "a", "m", "s");
            Assert.Throws<ArgumentException>(() => set.SetFiles(slot, "b", "n", "t"));
        }
    }
}
