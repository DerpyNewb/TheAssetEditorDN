using System.Text.RegularExpressions;
using System.Xml.Linq;
using AssetEditor.ViewModels;

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
}
