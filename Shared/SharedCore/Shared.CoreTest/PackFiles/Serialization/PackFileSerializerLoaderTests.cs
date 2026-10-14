using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Shared.CoreTest.PackFiles.Serialization
{
    // A damaged pack: what the loader keeps, what it leaves out, and that it never hands back a pack that can be saved over.
    [TestFixture]
    internal class PackFileSerializerLoaderTests
    {
        const string PackName = @"c:\mods\damaged.pack";

        // Random bytes, so compression cannot shrink a file and move the cut into the one before it.
        static byte[] Content(int seed)
        {
            var bytes = new byte[300];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        static byte[] Pack(params string[] paths)
        {
            var container = PackFileContainer.CreatePackFile("damaged", "damaged.pack", PackFileVersion.PFH5);
            for (var i = 0; i < paths.Length; i++)
                container.AddOrUpdateFile(paths[i], new PackFile(Path.GetFileName(paths[i]), new MemorySource(Content(i))));
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            PackFileSerializerWriter.SaveToByteArray(PackName, container, writer, GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3));
            return ms.ToArray();
        }

        static PackFileContainer Load(byte[] bytes)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            return PackFileSerializerLoader.Load(PackName, bytes.LongLength, reader, new CustomPackDuplicateFileResolver());
        }

        [Test]
        public void ATruncatedPack_KeepsTheFilesBeforeTheCut_LeavesOutTheRest_AndStaysReadOnly()
        {
            var cut = Pack(@"a\first.bin", @"b\second.bin", @"c\third.bin")[..^100];

            var pack = Load(cut);

            Assert.That(pack.DamagedFiles, Is.EqualTo(new[] { @"c\third.bin" }));
            Assert.That(pack.FindFile(@"c\third.bin"), Is.Null);
            using var stream = new MemoryStream(cut);
            Assert.That(((PackedFileSource)pack.FindFile(@"a\first.bin")!.DataSource).ReadData(stream), Is.EqualTo(Content(0)));
            Assert.That(((PackedFileSource)pack.FindFile(@"b\second.bin")!.DataSource).ReadData(stream), Is.EqualTo(Content(1)));
            pack.IsReadOnly = false;
            Assert.That(pack.IsReadOnly, Is.True);
        }

        [Test]
        public void AWholePack_HasNoDamagedFiles_AndCanBeEditable()
        {
            var pack = Load(Pack(@"a\first.bin", @"b\second.bin"));

            Assert.That(pack.DamagedFiles, Is.Empty);
            Assert.That(pack.GetFileCount(), Is.EqualTo(2));
            pack.IsReadOnly = false;
            Assert.That(pack.IsReadOnly, Is.False);
        }

        [Test]
        public void APackCutInsideItsFileList_IsRefused_ByName()
        {
            var bytes = Pack(@"a\first.bin", @"b\second.bin");

            var e = Assert.Throws<InvalidDataException>(() => Load(bytes[..40]));
            Assert.That(e!.Message, Does.Contain("damaged.pack"));
        }

        // The file count is the u32 at byte 16. One too many reads the first file's data as a file list entry.
        [Test]
        public void AFileCountOneTooHigh_IsRefused_NotReadFromTheData()
        {
            var bytes = Pack(@"a\first.bin");
            BitConverter.GetBytes(2).CopyTo(bytes, 16);

            var e = Assert.Throws<InvalidDataException>(() => Load(bytes));
            Assert.That(e!.Message, Does.Contain("damaged.pack"));
        }

        // A garbage one must not size anything by it.
        [Test]
        public void AFileCountTheFileListCannotHold_IsRefused()
        {
            var bytes = Pack(@"a\first.bin");
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 16);

            var e = Assert.Throws<InvalidDataException>(() => Load(bytes));
            Assert.That(e!.Message, Does.Contain("damaged.pack"));
        }
    }
}
