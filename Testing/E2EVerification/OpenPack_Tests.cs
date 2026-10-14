using AssetEditor.UiCommands;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // File > Open Packfile, as v0.45 had it: the .pack itself is the editable pack and Save writes back into it.
    internal class OpenPack_Tests
    {
        string _dir = null!;
        string _path = null!;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "AE_OpenPack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "karl.pack");
            File.Copy(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), _path);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_dir, true);

        AssetEditorTestRunner OpenFromTheMenu()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.Dialogs.Setup(x => x.ShowSystemOpenFileDialog(It.IsAny<bool>(), It.IsAny<string>())).Returns(new SystemOpenFileDialogResult(true, [_path]));
            runner.CommandFactory.Create<OpenPackCommand>().Execute();
            return runner;
        }

        Dictionary<string, byte[]> ContentsOnDisk()
        {
            var loader = new AssetEditorTestRunner().ServiceProvider.GetService(typeof(IPackFileContainerLoader)) as IPackFileContainerLoader;
            return loader!.CreateFromPackFile(PackFileContainerType.Normal, _path, true).GetAllFiles().ToDictionary(x => x.Key, x => x.Value.DataSource.ReadData());
        }

        [Test]
        public void APackOpened_IsTheEditablePack_AndSaveWritesTheEditIntoTheSameFile()
        {
            var before = ContentsOnDisk();
            var runner = OpenFromTheMenu();

            var pack = runner.PackFileService.GetEditablePack()!;
            Assert.That(pack.SystemFilePath, Is.EqualTo(_path));
            var edited = before.Keys.Order().First();
            runner.PackFileService.SaveFile(pack.GetAllFiles()[edited], [1, 2, 3]);
            runner.CommandFactory.Create<SavePackFileContainerCommand>().ExecuteForEditablePack();

            var after = ContentsOnDisk();
            before[edited] = [1, 2, 3];
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void ADamagedPackOpened_IsAddedReadOnly_AndIsNotTheEditablePack()
        {
            using (var stream = new FileStream(_path, FileMode.Open))
                stream.SetLength(stream.Length - 100);

            var runner = OpenFromTheMenu();

            Assert.That(runner.PackFileService.GetEditablePack(), Is.Null);
            Assert.That(runner.PackFileService.GetAllPackfileContainers().Single(x => x.SystemFilePath == _path).IsReadOnly, Is.True);
        }
    }
}
