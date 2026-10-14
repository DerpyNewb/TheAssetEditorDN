using Moq;
using Shared.ByteParsing;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.CoreTest.PackFiles.Utility;
using Test.TestingUtility.TestUtility;

namespace Shared.CoreTest.PackFiles.Models.Containers
{
    // A pack opened from disk and saved back over itself, the way Open Pack edits one: its unedited files are read out of the
    // very file the save replaces.
    internal class PackFileContainerTests_SaveInPlace
    {
        static readonly GameInformation Wh3 = GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3);
        string _dir = null!;
        string _path = null!;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "AE_SaveInPlace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "karl.pack");
            File.Copy(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), _path);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_dir, true);

        PackFileContainer Open()
        {
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(d => d.ShowWaitCursor()).Returns(new Mock<IWaitCursor>().Object);
            var loader = new PackFileContainerLoader(new ApplicationSettingsService(GameTypeEnum.Warhammer3), dialogs.Object, new LocalizationManager(), new InMemoryPackFileContainerCacheHelper(), new SimpleSystemFolderContainerFactory());
            return (PackFileContainer)loader.CreateFromPackFile(PackFileContainerType.Normal, _path, false);
        }

        static Dictionary<string, byte[]> Contents(PackFileContainer pack) => pack.FileList.ToDictionary(x => x.Key, x => x.Value.DataSource.ReadData());

        [Test]
        public void SavedOverItselfTwice_EveryFileReadsBackAsItWas()
        {
            var pack = Open();
            var before = Contents(pack);

            pack.SaveToDisk(_path, false, Wh3);
            Assert.That(Contents(pack), Is.EqualTo(before));
            pack.SaveToDisk(_path, false, Wh3);
            Assert.That(Contents(pack), Is.EqualTo(before));
            Assert.That(Contents(Open()), Is.EqualTo(before));
        }

        // The last file written cannot be read, so the save fails after every other file is already in the new pack.
        [Test]
        public void ASaveThatFailsPartway_LeavesThePackOnDiskAndTheEditorAsTheyWere()
        {
            var pack = Open();
            var before = Contents(pack);
            var bytes = File.ReadAllBytes(_path);
            pack.AddOrUpdateFile(@"zzzz\unreadable.bin", new PackFile("unreadable.bin", new UnreadableSource()));

            Assert.Catch<IOException>(() => pack.SaveToDisk(_path, false, Wh3));

            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(bytes));
            Assert.That(File.Exists(_path + "_temp"), Is.False);
            Assert.That(before.Keys.ToDictionary(x => x, x => pack.FileList[x].DataSource.ReadData()), Is.EqualTo(before));
        }

        // Held open without delete sharing: the save writes and reads back the new pack, then cannot put it in place.
        [Test]
        public void ASaveThatCannotReplaceThePack_LeavesThePackOnDiskAndTheEditorAsTheyWere()
        {
            var pack = Open();
            var before = Contents(pack);
            var bytes = File.ReadAllBytes(_path);

            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                Assert.Catch<Exception>(() => pack.SaveToDisk(_path, false, Wh3));

            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(bytes));
            Assert.That(File.Exists(_path + "_temp"), Is.False);
            Assert.That(Contents(pack), Is.EqualTo(before));
        }

        [Test]
        public void APackRewrittenOnDiskAtTheSameSize_IsNotSavedOver()
        {
            var pack = Open();
            var bytes = File.ReadAllBytes(_path);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(_path, bytes);
            File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddHours(1));

            Assert.That(() => pack.SaveToDisk(_path, false, Wh3), Throws.Exception.With.Message.Contains("changed outside"));
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(bytes));
        }

        [Test]
        public void AFileTheSaveLeavesOut_StillReadsAsItWas_AfterThePackIsReplaced()
        {
            var pack = Open();
            var left = pack.FileList.Keys.Order().First();
            var before = pack.FileList[left].DataSource.ReadData();
            pack.PackFileSettings.IgnoredFilesWhenSerializing.Add(left);

            pack.SaveToDisk(_path, false, Wh3);

            Assert.That(pack.FileList[left].DataSource.ReadData(), Is.EqualTo(before));
        }

        [Test]
        public void Verify_NamesAFileWhoseBytesAreNotWhatWasWritten()
        {
            var pack = Open();
            var written = Path.Combine(_dir, "written.pack");
            List<WrittenFile> files;
            using (var stream = new FileStream(written, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
                files = PackFileSerializerWriter.SaveToByteArray(written, pack, writer, Wh3);
            Assert.That(() => PackFileSerializerWriter.Verify(written, files), Throws.Nothing);

            var last = files.MaxBy(x => x.Source.Offset)!;
            var bytes = File.ReadAllBytes(written);
            bytes[last.Source.Offset] ^= 0xFF;
            File.WriteAllBytes(written, bytes);

            Assert.That(() => PackFileSerializerWriter.Verify(written, files), Throws.InstanceOf<InvalidDataException>().With.Message.Contains(last.Path));
        }

        sealed class UnreadableSource : IDataSource
        {
            public long Size => 10;
            public CompressionFormat CompressionFormat => CompressionFormat.None;
            public byte[] ReadData() => throw new IOException("unreadable");
            public byte[] PeekData(int size) => throw new IOException("unreadable");
            public ByteChunk ReadDataAsChunk() => throw new IOException("unreadable");
        }
    }
}
