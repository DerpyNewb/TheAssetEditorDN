using System.Text.RegularExpressions;
using System.Xml.Linq;
using AssetEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Services;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification;

internal class MenuBar_Tests
{
    // File > Exit had no command and did nothing. Every item the main menu shows, and that holds no submenu, must name a
    // command the view model has, or a Click handler.
    [Test]
    public void EveryMenuItemWithNoSubmenu_RunsACommandTheViewModelHas()
    {
        var root = SettingsView_Tests.Root();
        var menu = XDocument.Load(Path.Combine(root, "AssetEditor", "Views", "MenuBarView.xaml"));
        var items = menu.Descendants(SettingsView_Tests.Wpf + "MenuItem")
            .Where(x => !x.Elements(SettingsView_Tests.Wpf + "MenuItem").Any() && x.Attribute("ItemsSource") == null)
            .ToList();
        Assert.That(items, Is.Not.Empty);

        var dead = items.Where(x => x.Attribute("Command") == null && x.Attribute("Click") == null)
            .Select(x => (string?)x.Attribute("Header")).ToList();
        Assert.That(dead, Is.Empty, "menu items that do nothing");

        foreach (var item in items.Where(x => x.Attribute("Command") != null))
        {
            var match = Regex.Match((string)item.Attribute("Command")!, @"^\{Binding MenuBar\.(\w+)\s*\}$");
            if (match.Success == false)
                continue;    // bound elsewhere, such as the recent-pack items' own commands
            Assert.That(typeof(MenuBarViewModel).GetProperty(match.Groups[1].Value), Is.Not.Null,
                $"{(string?)item.Attribute("Header")} binds {match.Groups[1].Value}, which MenuBarViewModel does not have");
        }
    }

    // Touched files > Extract always wrote c:\temp\extractedPack.pack, with no dialog, and failed when c:\temp was missing.
    [TestCase(true)]
    [TestCase(false)]
    public void TouchedFilesExtract_SavesWhereTheDialogSays_AndNothingOnCancel(bool chosen)
    {
        var directory = Directory.CreateTempSubdirectory("ae_touched_");
        try
        {
            var path = Path.Combine(directory.FullName, "picked.pack");
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.Dialogs.Setup(x => x.ShowSystemSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), "pack"))
                .Returns(new SystemSaveFileDialogResult(chosen, path));

            var menu = runner.ServiceProvider.GetRequiredService<MenuBarViewModel>();
            menu.TouchedFileRecorderExtractCommand.Execute(null);

            Assert.That(File.Exists(path), Is.EqualTo(chosen));
            Assert.That(File.Exists(@"c:\temp\extractedPack.pack") && File.GetLastWriteTimeUtc(@"c:\temp\extractedPack.pack") > DateTime.UtcNow.AddMinutes(-1),
                Is.False, "still wrote the old fixed path");
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
