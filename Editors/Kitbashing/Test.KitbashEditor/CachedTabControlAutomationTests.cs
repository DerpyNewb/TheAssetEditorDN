using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using AssetEditor.Views;

namespace Test.KitbashEditor
{
    // The main window's editor tabs keep each tab's content alive in PART_ItemsHolder, where a TabItem's automation peer
    // never looks, so UI Automation (screen readers, the MCP server, Appium) saw no editor at all - not even Kitbash's scene tree.
    public class CachedTabControlAutomationTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void UiAutomation_SeesTheSelectedTabsContent_AndNotTheHiddenOnes()
        {
            var holder = new FrameworkElementFactory(typeof(Grid)) { Name = "PART_ItemsHolder" };
            var tabs = new CachedTabControl
            {
                Template = new ControlTemplate(typeof(CachedTabControl)) { VisualTree = holder },
                ItemsSource = new[] { "first editor", "second editor" },
            };
            foreach (var index in new[] { 0, 1 })  // both get a content presenter; the first is then collapsed
            {
                tabs.SelectedIndex = index;
                tabs.Measure(new Size(400, 300));
                tabs.Arrange(new Rect(0, 0, 400, 300));
                tabs.UpdateLayout();
            }

            // The tab headers come back as TabItem peers named after the items; the content is the Text peers.
            var content = UIElementAutomationPeer.CreatePeerForElement(tabs).GetChildren()
                .Where(p => p.GetAutomationControlType() == AutomationControlType.Text).Select(p => p.GetName());
            Assert.That(content, Is.EqualTo(new[] { "second editor" }));
        }
    }
}
