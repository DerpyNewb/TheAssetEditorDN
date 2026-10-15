using System.Text;
using Moq;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.CoreTest.PackFiles.Utility;
using Test.TestingUtility.TestUtility;

namespace Shared.CoreTest.PackFiles.Utility
{
    internal class PackBackupsTests
    {
        static readonly GameInformation Wh3 = GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3);
        string _dir = null!;
        string _path = null!;
        ApplicationSettings _settings = null!;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "AE_Backups_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "karl.pack");
            File.Copy(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), _path);
            _settings = new ApplicationSettings { BackupFolder = Path.Combine(_dir, "backups"), BackupsKept = 3 };
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_dir, true);

        PackFileContainer Open(string path)
        {
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(d => d.ShowWaitCursor()).Returns(new Mock<IWaitCursor>().Object);
            var loader = new PackFileContainerLoader(new ApplicationSettingsService(GameTypeEnum.Warhammer3), dialogs.Object, new LocalizationManager(), new InMemoryPackFileContainerCacheHelper(), new SimpleSystemFolderContainerFactory());
            return (PackFileContainer)loader.CreateFromPackFile(PackFileContainerType.Normal, path, false);
        }

        static Dictionary<string, byte[]> Contents(PackFileContainer pack) => pack.FileList.ToDictionary(x => x.Key, x => x.Value.DataSource.ReadData());

        [Test]
        public void Autosave_HoldsTheUnsavedEdit_AndLeavesThePackAndTheEditorAlone()
        {
            var pack = Open(_path);
            var onDisk = File.ReadAllBytes(_path);
            pack.AddOrUpdateFile(@"text\edit.txt", new PackFile("edit.txt", new MemorySource(Encoding.UTF8.GetBytes("unsaved"))));
            var expected = Contents(pack);

            var copy = PackBackups.WriteAutosave(pack, _settings, Wh3);

            Assert.That(Path.GetDirectoryName(copy), Is.EqualTo(_settings.BackupFolder));
            Assert.That(Contents(Open(copy!)), Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(onDisk));
            Assert.That(pack.SystemFilePath, Is.EqualTo(_path));
            Assert.That(pack.FileList.Values.Select(x => x.DataSource).OfType<PackedFileSource>().All(x => x.Parent.FilePath == _path));
        }

        [Test]
        public void PastTheKeptCount_TheOldestGo_AndAnotherPacksBackupsStay()
        {
            var other = Path.Combine(_dir, "karl.two.pack");
            File.Copy(_path, other);
            var otherCopy = PackBackups.CopyBeforeSave(other, _settings);

            var made = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                made.Add(PackBackups.CopyBeforeSave(_path, _settings)!);
                Thread.Sleep(5); // the name is stamped to the millisecond
            }

            Assert.That(PackBackups.List(_path, _settings), Is.EqualTo(made.TakeLast(3).Reverse()));
            Assert.That(made.Take(2).Any(File.Exists), Is.False);
            Assert.That(File.Exists(otherCopy), Is.True);
            Assert.That(File.ReadAllBytes(made[^1]), Is.EqualTo(File.ReadAllBytes(_path)));
        }

        [Test]
        public void NothingOnDisk_NothingCopied() =>
            Assert.That(PackBackups.CopyBeforeSave(Path.Combine(_dir, "new.pack"), _settings), Is.Null);
    }
}
