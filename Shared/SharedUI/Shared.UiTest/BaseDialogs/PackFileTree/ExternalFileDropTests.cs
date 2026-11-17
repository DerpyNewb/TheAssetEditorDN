using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.UiTest.BaseDialogs.PackFileTree
{
    internal class ExternalFileDropTests : PackFileTreeTestBase
    {
        sealed class RecordingHandler : IExternalFileDropHandler
        {
            public List<(string Path, IPackFileContainer Container, string Folder)> Calls { get; } = [];
            public bool CanHandle(string diskPath) => diskPath.EndsWith(".blend");
            public void Handle(string diskPath, IPackFileContainer container, string packFolder) => Calls.Add((diskPath, container, packFolder));
        }

        [Test]
        public void DroppedModelOnAFolderGoesToTheHandlerWithThatFolder()
        {
            var container = AddPackFiles(false, "mod", "mod.pack", @"variantmeshes\a\thing.rigid_model_v2");
            var browser = PackFileBrowser();
            var handler = new RecordingHandler();
            browser.ExternalFileDropHandlers = [handler];
            var folder = TreeNodeHelper.FindInTree(browser.Files.First(), @"variantmeshes\a")!;

            var handled = browser.DropExternalFiles([@"C:\x\notes.txt", @"C:\x\model.blend"], folder);

            Assert.That(handled, Is.True);
            Assert.That(handler.Calls, Is.EqualTo(new[] { (@"C:\x\model.blend", container, @"variantmeshes\a") }));
        }

        [Test]
        public void DroppedOnAFileUsesTheFilesFolder()
        {
            AddPackFiles(false, "mod", "mod.pack", @"variantmeshes\a\thing.rigid_model_v2");
            var browser = PackFileBrowser();
            var handler = new RecordingHandler();
            browser.ExternalFileDropHandlers = [handler];
            var file = TreeNodeHelper.FindInTree(browser.Files.First(), @"variantmeshes\a\thing.rigid_model_v2")!;

            browser.DropExternalFiles([@"C:\x\model.blend"], file);

            Assert.That(handler.Calls.Single().Folder, Is.EqualTo(@"variantmeshes\a"));
        }

        [Test]
        public void DropOnACaPackIsRefused()
        {
            AddPackFiles(true, "ca", "ca.pack", @"variantmeshes\a\thing.rigid_model_v2");
            var browser = PackFileBrowser();
            var handler = new RecordingHandler();
            browser.ExternalFileDropHandlers = [handler];
            var folder = TreeNodeHelper.FindInTree(browser.Files.First(), @"variantmeshes\a")!;

            Assert.That(browser.DropExternalFiles([@"C:\x\model.blend"], folder), Is.False);
            Assert.That(handler.Calls, Is.Empty);
        }
    }
}
