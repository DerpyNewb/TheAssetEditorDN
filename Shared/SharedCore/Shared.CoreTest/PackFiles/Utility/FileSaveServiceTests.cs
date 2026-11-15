using Moq;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.TestUtility;

namespace Shared.CoreTest.PackFiles.Utility
{
    internal class FileSaveServiceTests
    {
        IPackFileService _pfs;
        IPackFileContainer _container;
        Mock<IStandardDialogs> _uiProvider = new();
        Mock<IGlobalEventHub> _eventHub = new();
        IScopedLogger _scopedLogger;
        PackFile _fileHandle;

        [SetUp]
        public void Setup()
        {
            _scopedLogger = MockScopedLogger.Create();

            var dialogProvider = new Mock<ISimpleMessageBox>();
            _pfs = new PackFileService(_eventHub.Object);
            (_pfs as PackFileService).MessageBoxProvider = dialogProvider.Object;
            var container = PackFileContainer.CreateCaPackFile("MyTest", "SystemPath");
  
            _pfs.AddContainer(container);
            _container = _pfs.CreateNewPackFileContainer("Output", PackFileVersion.PFH5, PackFileCAType.MOD, true);

            List<NewPackFileEntry> files = [
                new NewPackFileEntry("folder\\subfolder", PackFile.CreateFromBytes("File0.test", [0])),
                new NewPackFileEntry("folder\\subfolder", PackFile.CreateFromBytes("File1.test", [1]))];
            _pfs.AddFilesToPack(_container, files);

            _fileHandle = _pfs.FindFile("folder\\subfolder\\File1.test");
            _eventHub.Invocations.Clear();
            _uiProvider.Invocations.Clear();
        }

        [Test]
        public void Save_NoExistingFile_DontPromptOnConflict()
        {
            // Arrange
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);

            // Act
            var result = saveService.Save("folder\\subfolder2\\File1.test", [3], false);

            // Assert
            Assert.That(_container.GetFileCount(), Is.EqualTo(3));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSource.ReadData(), Is.EqualTo(new byte[] { 3 }));

            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileContainerFilesAddedEvent>()), Times.Once);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>()), Times.Never);
        }

        [Test]
        public void Save_ExistingFile_DontPromptOnConflict()
        {
            // Arrange
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);

            // Act
            var result = saveService.Save("folder\\subfolder\\File1.test", [3], false);

            // Assert
            Assert.That(_container.GetFileCount(), Is.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(_fileHandle));
            Assert.That(result!.DataSource.ReadData(), Is.EqualTo(new byte[] { 3 }));

            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileContainerFilesUpdatedEvent>()), Times.Once);
            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileSavedEvent>()), Times.Once);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>()), Times.Never);
        }

        [Test]
        public void Save_NoExistingFile_PromptOnConflict()
        {
            // Arrange
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);

            // Act
            var result = saveService.Save("folder\\subfolder2\\File1.test", [3], true);

            // Assert
            Assert.That(_container.GetFileCount(), Is.EqualTo(3));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSource.ReadData(), Is.EqualTo(new byte[] { 3 }));

            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileContainerFilesAddedEvent>()), Times.Once);
            _uiProvider.Verify(x=>x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>()), Times.Never);
        }

        [Test]
        public void Save_ExistingFile_PromptOnConflict_NewPath()
        {
            // Arrange
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);
            AnswerConflict(SaveAs);
            _uiProvider.Setup(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), "folder\\subfolder\\File1.test")).Returns(new SaveDialogResult(true, null, "folder\\subfolder3\\File1.test"));

            // Act
            var result = saveService.Save("folder\\subfolder\\File1.test", [3], true);

            // Assert
            Assert.That(_container.GetFileCount(), Is.EqualTo(3));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSource.ReadData(), Is.EqualTo(new byte[] { 3 }));
            var lookup = _pfs.FindFile("folder\\subfolder3\\File1.test");
            Assert.That(lookup, Is.Not.Null);

            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileContainerFilesAddedEvent>()), Times.Once);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), "folder\\subfolder\\File1.test"), Times.Once, "Save as starts at the path being saved, not an empty dialog");
        }


        [Test]
        public void Save_ExistingFile_PromptOnConflict_ExistingPath()
        {
            // Arrange
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);
            AnswerConflict(SaveAs);
            _uiProvider.Setup(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), It.IsAny<string>())).Returns(new SaveDialogResult(true, null, "folder\\subfolder\\File0.test"));

            // Act
            var result = saveService.Save("folder\\subfolder\\File1.test", [3], true);

            // Assert
            Assert.That(_container.GetFileCount(), Is.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSource.ReadData(), Is.EqualTo(new byte[] { 3 }));

            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileSavedEvent>()), Times.Once);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void Save_ExistingFile_PromptOnConflict_Exit()
        {
            // Arrange
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);
            AnswerConflict(SaveAs);
            _uiProvider.Setup(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), It.IsAny<string>())).Returns(new SaveDialogResult(false, null, null));

            // Act
            var result = saveService.Save("folder\\subfolder\\File1.test", [3], true);

            // Assert
            Assert.That(_container.GetFileCount(), Is.EqualTo(2));
            Assert.That(result, Is.Null);

            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileSavedEvent>()), Times.Never);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), It.IsAny<string>()), Times.Once);
        }

        // A file already in the pack used to open the save dialog empty, at the pack's root, with no way to just overwrite it.
        const int Overwrite = 0, SaveAs = 1, Cancel = 2;
        void AnswerConflict(int? choice) => _uiProvider
            .Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(choice);

        [Test]
        public void Save_ExistingFile_PromptOnConflict_Overwrite()
        {
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);
            AnswerConflict(Overwrite);

            var result = saveService.Save("folder\\subfolder\\File1.test", [3], true);

            Assert.That(result, Is.EqualTo(_fileHandle));
            Assert.That(result!.DataSource.ReadData(), Is.EqualTo(new byte[] { 3 }));
            Assert.That(_container.GetFileCount(), Is.EqualTo(2));
            _uiProvider.Verify(x => x.ShowChoiceBox(It.Is<string>(m => m.Contains("folder\\subfolder\\File1.test")), It.IsAny<string>(), It.Is<IReadOnlyList<string>>(c => c.Count == 3)), Times.Once);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [TestCase(Cancel)]
        [TestCase(null)]
        public void Save_ExistingFile_PromptOnConflict_Cancel(int? choice)
        {
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);
            AnswerConflict(choice);

            var result = saveService.Save("folder\\subfolder\\File1.test", [3], true);

            Assert.That(result, Is.Null);
            Assert.That(_fileHandle.DataSource.ReadData(), Is.EqualTo(new byte[] { 1 }));
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        // SaveAs

        [Test]
        public void Save_RootPathWithLeadingSlash_CreatesRootLevelFile()
        {
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);

            var result = saveService.Save("\\RootFile.test", [9], false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Name, Is.EqualTo("RootFile.test"));
            Assert.That(_pfs.FindFile("RootFile.test", _container), Is.Not.Null);
            Assert.That(_pfs.FindFile("\\RootFile.test", _container), Is.Null);
            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileContainerFilesAddedEvent>()), Times.Once);
        }

        [Test]
        public void SaveAs_RootPathWithLeadingSlash_CreatesRootLevelFile()
        {
            var saveService = new FileSaveService(_pfs, _uiProvider.Object, _scopedLogger);
            _uiProvider
                .Setup(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>()))
                .Returns(new SaveDialogResult(true, null, "\\RootFile.test"));

            var result = saveService.SaveAs(".test", [9]);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Name, Is.EqualTo("RootFile.test"));
            Assert.That(_pfs.FindFile("RootFile.test", _container), Is.Not.Null);
            Assert.That(_pfs.FindFile("\\RootFile.test", _container), Is.Null);
            _eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileContainerFilesAddedEvent>()), Times.Once);
            _uiProvider.Verify(x => x.DisplaySaveDialog(_pfs, It.IsAny<List<string>>()), Times.Once);
        }
    }
}
