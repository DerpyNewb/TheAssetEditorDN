namespace Test.E2EVerification
{
    // The 3D editors' shared view: its pane titles and menu items. A missing key prints as the key itself, without a word.
    internal class EditorHostView_LocTests
    {
        [Test]
        public void EveryTextTheEditorHostShows_IsInEveryLanguage()
        {
            var keys = SettingsView_Tests.LocKeys(Path.Combine(SettingsView_Tests.Root(), "Editors", "Shared", "Editors.Shared.Core", "Common", "BaseControl", "EditorHostView.xaml"));

            Assert.That(keys, Does.Contain("EditorHost.Menu.ResetLayout").And.Contain("EditorHost.Pane.GameView"), "the walk did not find the dock's keys");
            Assert.That(SettingsView_Tests.MissingInALanguage(keys), Is.Empty);
        }

        [Test]
        public void EveryUnitSimulatorPaneTitle_IsInEveryLanguage()
        {
            var source = File.ReadAllText(Path.Combine(SettingsView_Tests.Root(), "Editors", "UnitSimulator", "Editors.UnitSimulator", "UnitSimulatorViewModel.cs"));
            var keys = System.Text.RegularExpressions.Regex.Matches(source, @"""(UnitSimulator\.Pane\.[A-Za-z]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();

            Assert.That(keys, Has.Count.EqualTo(5), "the simulator's five pane titles are not loc keys");
            Assert.That(SettingsView_Tests.MissingInALanguage(keys), Is.Empty);
        }
    }
}
