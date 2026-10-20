using System.Text.Json;
using System.Xml.Linq;
using AssetEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification;

internal class CreatePack_Tests
{
    [TestCase("create")]
    [TestCase("cancel")]
    [TestCase("existing")]
    [TestCase("invalid")]
    public void FileMenu_CreatesAnEditablePack_WithoutReplacingExistingFiles(string action)
    {
        var directory = Directory.CreateTempSubdirectory("ae_new_pack_");
        try
        {
            var path = Path.Combine(directory.FullName, "new.pack");
            if (action == "existing")
                File.WriteAllBytes(path, [1, 2, 3]);
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var previous = runner.CreateOutputPack();
            runner.Dialogs.Setup(x => x.ShowSystemSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), "pack"))
                .Returns(new SystemSaveFileDialogResult(action != "cancel",
                    action == "invalid" ? Path.Combine(directory.FullName, "missing", "new.pack") : path));

            var menu = runner.ServiceProvider.GetRequiredService<MenuBarViewModel>();
            var command = typeof(MenuBarViewModel).GetProperty("CreateNewPackCommand");
            Assert.That(command, Is.Not.Null, "File menu needs a create pack command");
            ((System.Windows.Input.ICommand)command!.GetValue(menu)!).Execute(null);

            if (action == "create")
            {
                Assert.That(runner.PackFileService.GetEditablePack()!.SystemFilePath, Is.EqualTo(path));
                var loaded = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>()
                    .CreateFromPackFile(PackFileContainerType.Normal, path, true);
                Assert.That(loaded.GetAllFiles(), Is.Empty);
                Assert.That(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4), Is.EqualTo("PFH5"));
            }
            else
            {
                Assert.That(runner.PackFileService.GetEditablePack(), Is.SameAs(previous));
                Assert.That(runner.PackFileService.GetAllPackfileContainers(), Has.Count.EqualTo(2));
                if (action == "existing")
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
                else
                    Assert.That(File.Exists(path), Is.False);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void FileMenu_UsesTheCreateCommand_AndTranslatedLabel()
    {
        var root = SettingsView_Tests.Root();
        var menu = XDocument.Load(Path.Combine(root, "AssetEditor", "Views", "MenuBarView.xaml"));
        Assert.That(menu.Descendants(SettingsView_Tests.Wpf + "MenuItem").Any(x =>
            (string?)x.Attribute("Command") == "{Binding MenuBar.CreateNewPackCommand}" &&
            (string?)x.Attribute("Header") == "{loc:Loc MenuBar.File.CreateNewPack}"), Is.True);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "AssetEditor"), "Language_*.json"))
        {
            using var language = JsonDocument.Parse(File.ReadAllText(file));
            Assert.That(language.RootElement.TryGetProperty("MenuBar.File.CreateNewPack", out _), Is.True, file);
        }
    }
}
