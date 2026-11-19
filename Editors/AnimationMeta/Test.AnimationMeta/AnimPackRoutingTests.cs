using System.IO;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.DB;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // default__.animpack keeps clips and metas in matched_combat folders. Read as matched-combat tables, each one sized a list
    // to whatever its bytes said before failing, and opening the pack took a minute and 20 GB (2026-11-19).
    public class AnimPackRoutingTests
    {
        // Version 1, no entries: a valid, empty matched-combat table, and eight bytes a clip could start with.
        static readonly byte[] EmptyTable = [1, 0, 0, 0, 0, 0, 0, 0];

        [Test]
        public void OnlyABinInAMatchedCombatFolder_IsReadAsAMatchedCombatTable()
        {
            var built = new AnimationPackFileDatabase("x");
            built.AddFile(new UnknownAnimFile(@"animations\matched_combat\attila_generated.bin", EmptyTable));
            built.AddFile(new UnknownAnimFile(@"animations\matched_combat\hu1\hu1_kill_01.anim", EmptyTable));
            built.AddFile(new UnknownAnimFile(@"animations\matched_combat\hu1\hu1_kill_01.anm.meta", EmptyTable));

            var runner = new AssetEditorTestRunner();
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"animations\animation_culture_packs", PackFile.CreateFromBytes("test.animpack", AnimationPackSerializer.ConvertToBytes(built)))]);
            var loaded = AnimationPackSerializer.Load(runner.PackFileService.FindFile(@"animations\animation_culture_packs\test.animpack", pack)!, runner.PackFileService).Files.ToList();

            Assert.That(loaded.Select(x => x.GetType()), Is.EqualTo(new[] { typeof(MatchedAnimFile), typeof(UnknownAnimFile), typeof(UnknownAnimFile) }));
            Assert.That(loaded.Select(x => x.ToByteArray()), Is.All.EqualTo(EmptyTable), "every entry saves back as it came");
        }

        [TestCase(-1)]
        [TestCase(1_000_000)]
        public void AnArrayCount_ThatTheBytesLeftCannotHold_IsRefusedBeforeAnythingIsAllocated(int count)
        {
            var bytes = BitConverter.GetBytes(count).Concat(new byte[16]).ToArray();
            Assert.That(() => new IntArrayTable(new ByteChunk(bytes)), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => new StringArrayTable(new ByteChunk(bytes)), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void AnArrayCount_ThatFits_StillReads()
        {
            var ints = new IntArrayTable(new ByteChunk([2, 0, 0, 0, 5, 0, 0, 0, 6, 0, 0, 0]));
            Assert.That(ints.Data, Is.EqualTo(new[] { 5, 6 }));
            var strings = new StringArrayTable(new ByteChunk(new StringArrayTable("a", "").ToByteArray()));
            Assert.That(strings.Values, Is.EqualTo(new[] { "a", "" }));
        }
    }
}
