using Test.TestingUtility.TestUtility;
using Moq;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.UiTest.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    [TestFixture]
    internal class CopyToEditablePackCommandTests : ContextMenuCommandTestBase
    {
        [Test]
        public void ShouldAdd_ReturnsTrueWhenEditablePackExists()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", []);
            _packFileService.SetEditablePack(target);

            var viewModel = PackFileBrowser();
            var node = TreeNodeHelper.FindNode(viewModel, source, "folder\\file.txt");

            var command = new CopyToEditablePackCommand(_packFileService, new Mock<IStandardDialogs>().Object, MockScopedLogger.Create());

            Assert.That(command.ShouldAdd(node), Is.True);
        }

        [Test]
        public void IsEnabled_ReturnsTrue()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var viewModel = PackFileBrowser();
            var node = TreeNodeHelper.FindNode(viewModel, source, "folder\\file.txt");

            var command = new CopyToEditablePackCommand(_packFileService, new Mock<IStandardDialogs>().Object, MockScopedLogger.Create());

            Assert.That(command.IsEnabled(node), Is.True);
        }

        [Test]
        public void Execute_CopiesChildFilesToEditablePack()
        {
            // Arrange
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["folder\\file.txt"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", []);
            _packFileService.SetEditablePack(target);

            var viewModel = PackFileBrowser();
            var root = viewModel.Files.First(x => (x as RootTreeNode)!.Owner == source);
            var folder = root.Children.First(x => x.NodeType == NodeType.Directory);

            var dialogs = new Mock<IStandardDialogs>();
            var waitCursor = new Mock<IWaitCursor>();
            dialogs.Setup(x => x.ShowWaitCursor()).Returns(waitCursor.Object);

            // Act
            var command = new CopyToEditablePackCommand(_packFileService, dialogs.Object, MockScopedLogger.Create());
            command.Configure(folder);

            command.Execute();

            // Assert
            var copiedFile = target.FindFile("folder\\file.txt");
            Assert.That(copiedFile, Is.Not.Null);
        }

        // A model's textures come from the .wsmodel beside it, so a model copied alone read the paths stored in the model itself,
        // which can name folders that hold nothing (CA's sorcerer prophet weapons name a hu1c/chs folder).
        [Test]
        public void Execute_CopyingAModel_BringsItsWsModel()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["props\\gun.rigid_model_v2", "props\\gun.wsmodel", "props\\other.wsmodel"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", []);
            _packFileService.SetEditablePack(target);

            Copy(TreeNodeHelper.FindNode(PackFileBrowser(), source, "props\\gun.rigid_model_v2"));

            Assert.That((target.FindFile("props\\gun.wsmodel") != null, target.FindFile("props\\other.wsmodel") != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Execute_CopyingAModel_KeepsTheWsModelAlreadyInTheEditablePack()
        {
            var source = AddPackFiles(false, "source", "c:\\source.pack", ["props\\gun.rigid_model_v2", "props\\gun.wsmodel"]);
            var target = AddPackFiles(false, "target", "c:\\target.pack", ["props\\gun.wsmodel"]);
            var mine = target.FindFile("props\\gun.wsmodel");
            _packFileService.SetEditablePack(target);

            Copy(TreeNodeHelper.FindNode(PackFileBrowser(), source, "props\\gun.rigid_model_v2"));

            Assert.That(target.FindFile("props\\gun.wsmodel"), Is.SameAs(mine));
        }

        void Copy(TreeNode node)
        {
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowWaitCursor()).Returns(new Mock<IWaitCursor>().Object);
            var command = new CopyToEditablePackCommand(_packFileService, dialogs.Object, MockScopedLogger.Create());
            command.Configure(node);
            command.Execute();
        }
    }
}
