using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
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

        // Right-click the pack > Restore Backup lists its backups; choosing one puts the .pack back, keeps what it replaced, and
        // opens the restored pack as the editable one again.
        [Test]
        public void RestoreBackup_FromThePacksMenu_PutsTheSavedOverPackBack()
        {
            var before = ContentsOnDisk();
            var runner = OpenFromTheMenu();
            var pack = runner.PackFileService.GetEditablePack()!;
            runner.PackFileService.SaveFile(pack.GetAllFiles()[before.Keys.Order().First()], [1, 2, 3]);
            runner.CommandFactory.Create<SavePackFileContainerCommand>().ExecuteForEditablePack();
            var saved = ContentsOnDisk();
            Assert.That(saved, Is.Not.EqualTo(before));

            runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);
            var shown = new List<string>();
            runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((message, _) => shown.Add(message));
            var menu = runner.ServiceProvider.GetRequiredService<PackFileContextMenuComposer>().Build(ContextMenuType.MainApplication, new RootTreeNode("karl.pack", pack));
            var restore = menu.Single(x => x?.Name == "Restore Backup")!;
            Assert.That(restore.ContextMenu.Select(x => x!.Name), Has.Exactly(1).Contains("Before save"), "the backup the save took");

            restore.ContextMenu[0]!.Command!.Execute(null);

            Assert.That(shown, Is.Empty, "the restore reported an error");
            Assert.That(ContentsOnDisk(), Is.EqualTo(before));
            var reopened = runner.PackFileService.GetEditablePack()!;
            Assert.That((reopened.SystemFilePath, reopened == pack), Is.EqualTo((_path, false)));
            var settings = runner.ServiceProvider.GetRequiredService<ApplicationSettingsService>().CurrentSettings;
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var replaced = loader.CreateFromPackFile(PackFileContainerType.Normal, PackBackups.List(_path, settings)[0], true).GetAllFiles().ToDictionary(x => x.Key, x => x.Value.DataSource.ReadData());
            Assert.That(replaced, Is.EqualTo(saved), "the restore backed up the pack it replaced");
        }

        // File > Restore Backup needs no pack open: it lists the backups of the packs opened lately, and puts one back over a pack
        // that is closed, or that has been deleted since.
        [TestCase(false)]
        [TestCase(true)]
        public void RestoreBackup_FromTheFileMenu_WithThePackClosedOrDeleted(bool deleted)
        {
            var before = ContentsOnDisk();
            var runner = OpenFromTheMenu();
            var pack = runner.PackFileService.GetEditablePack()!;
            runner.PackFileService.SaveFile(pack.GetAllFiles()[before.Keys.Order().First()], [1, 2, 3]);
            runner.CommandFactory.Create<SavePackFileContainerCommand>().ExecuteForEditablePack();
            runner.PackFileService.UnloadPackContainer(pack);
            if (deleted)
                File.Delete(_path);
            runner.ServiceProvider.GetRequiredService<ApplicationSettingsService>().AddRecentlyOpenedPackFile(_path, PackFileContainerType.Normal, false);
            runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);
            var shown = new List<string>();
            runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((message, _) => shown.Add(message));

            var menu = runner.ServiceProvider.GetRequiredService<MenuBarViewModel>();
            menu.RefreshRestorableBackups();
            var backup = menu.RestorableBackups.Single(x => x.Header == _path).MenuItems.Single();
            Assert.That(backup.Header, Does.Contain("Before save"));
            backup.Command!.Execute(null);

            Assert.That(shown, Is.Empty, "the restore reported an error");
            Assert.That(ContentsOnDisk(), Is.EqualTo(before));
            Assert.That(runner.PackFileService.GetAllPackfileContainers().Any(x => x.SystemFilePath == _path), Is.False, "a closed pack stays closed");
        }
    }
}
