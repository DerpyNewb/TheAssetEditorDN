using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Shared.CoreTest.PackFiles.Models.Containers
{
    // A project builds its .pack somewhere else, often straight into the game's data folder: the build must never leave that
    // pack half written, and must never be written over a pack the editor is still reading files from.
    internal class SystemFolderContainerTests_SaveProtection
    {
        static readonly GameInformation Wh3 = GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3);
        string _dir = null!;
        string _output = null!;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ProjectSave_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "project", "data"));
            Directory.CreateDirectory(Path.Combine(_dir, "out"));
            File.WriteAllText(Path.Combine(_dir, "project", "data", "seed.txt"), "seed");
            _output = Path.Combine(_dir, "out", "built.pack");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_dir, true);

        (PackFileService Pfs, SystemFolderContainer Project) Open()
        {
            var project = new SystemFolderContainer(Path.Combine(_dir, "project"), new FileSystemAccess(), new Mock<IFileSystemWatcher>().Object);
            project.PackFileSettings.GameVersion = GameTypeEnum.Warhammer3;
            var pfs = new PackFileService(new Mock<IGlobalEventHub>().Object)
            {
                MessageBoxProvider = new Mock<ISimpleMessageBox>().Object,
                EnforceGameFilesMustBeLoaded = false
            };
            pfs.AddContainer(project, true);
            return (pfs, project);
        }

        static void AddFile(PackFileService pfs, SystemFolderContainer project, string name) =>
            pfs.AddFilesToPack(project, [new NewPackFileEntry("data", PackFile.CreateFromASCII(name, name))]);

        static List<string> FilesIn(string pack)
        {
            using var stream = File.OpenRead(pack);
            using var reader = new BinaryReader(stream);
            return PackFileSerializerLoader.Load(pack, stream.Length, reader, new CaPackDuplicateFileResolver()).GetAllFiles().Keys.Order().ToList();
        }

        [Test]
        public void ARebuild_ReplacesTheBuiltPack_AndLeavesNoTempFileBehind()
        {
            var (pfs, project) = Open();
            pfs.SavePackContainer(project, _output, false, Wh3);
            AddFile(pfs, project, "added.txt");

            pfs.SavePackContainer(project, _output, false, Wh3);

            Assert.That(FilesIn(_output), Does.Contain(@"data\added.txt"));
            Assert.That(File.Exists(_output + "_temp"), Is.False);
        }

        // Held open without delete sharing, as a reader that allows writes holds it: the rebuild is written and read back, then
        // cannot be put in place. Copying it over the old pack would have written into the file under that reader.
        [Test]
        public void ARebuildThatCannotReplaceTheBuiltPack_LeavesItAsItWas_AndNoTempFile()
        {
            var (pfs, project) = Open();
            pfs.SavePackContainer(project, _output, false, Wh3);
            var bytes = File.ReadAllBytes(_output);
            AddFile(pfs, project, "added.txt");

            using (new FileStream(_output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                Assert.Catch<Exception>(() => pfs.SavePackContainer(project, _output, false, Wh3));

            Assert.That(File.ReadAllBytes(_output), Is.EqualTo(bytes));
            Assert.That(File.Exists(_output + "_temp"), Is.False);
        }

        // An open pack reads its files from its .pack as they are needed, by offset: a new pack under it reads back as other bytes.
        [Test]
        public void ABuildOverAPackOpenInTheEditor_IsRefusedByName_AndThatPackStillReadsItsFiles()
        {
            var (pfs, project) = Open();
            pfs.SavePackContainer(project, _output, false, Wh3);
            var bytes = File.ReadAllBytes(_output);
            PackFileContainer open;
            using (var stream = File.OpenRead(_output))
            using (var reader = new BinaryReader(stream))
                open = PackFileSerializerLoader.Load(_output, stream.Length, reader, new CaPackDuplicateFileResolver());
            pfs.AddContainer(open, false);
            AddFile(pfs, project, "added.txt");

            Assert.That(() => pfs.SavePackContainer(project, Path.Combine(_dir, "out", ".", "BUILT.pack"), false, Wh3),
                Throws.InstanceOf<IOException>().With.Message.Contains("built"));

            Assert.That(File.ReadAllBytes(_output), Is.EqualTo(bytes));
            Assert.That(open.FindFile(@"data\seed.txt")!.DataSource.ReadData(), Is.EqualTo("seed"u8.ToArray()));
        }
    }
}
