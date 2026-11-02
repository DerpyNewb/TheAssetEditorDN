using System.Xml.Linq;
using AssetEditor.ViewModels;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification
{
    // The panel the app opens with: a project (recommended), a .pack edited in place, or one opened lately, and a box that turns
    // it off.
    internal class StartPanel_Tests
    {
        const string OpenPackFilter = "Pack files (*.pack)|*.pack";

        static (StartPanelViewModel Panel, AssetEditorTestRunner Runner, ApplicationSettings Settings) Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.Dialogs.Setup(x => x.ShowSystemOpenFileDialog(It.IsAny<bool>(), It.IsAny<string>())).Returns(new SystemOpenFileDialogResult(false, []));
            var settings = (ApplicationSettingsService)runner.ServiceProvider.GetService(typeof(ApplicationSettingsService))!;
            settings.CurrentSettings.ShowStartPanel = true;
            return (new StartPanelViewModel(settings, runner.CommandFactory), runner, settings.CurrentSettings);
        }

        // Open Packfile's own picker, so the command that ran is the one the button names.
        static void OpenPackPickerShown(AssetEditorTestRunner runner, Times times) =>
            runner.Dialogs.Verify(x => x.ShowSystemOpenFileDialog(It.IsAny<bool>(), OpenPackFilter), times);

        [Test]
        public void APick_ClosesThePanel_AndItsCommandRunsOnlyOnceThePanelHasClosed()
        {
            var (panel, runner, _) = Open();
            var closeAsked = 0;
            panel.CloseRequested += () => closeAsked++;

            panel.OpenPackCommand.Execute(null);
            Assert.That(closeAsked, Is.EqualTo(1));
            OpenPackPickerShown(runner, Times.Never());

            panel.Closed();
            OpenPackPickerShown(runner, Times.Once());
        }

        [Test]
        public void Skip_ClosesThePanel_RunsNothing_AndAnUntickedBoxLeavesThePanelOn()
        {
            var (panel, runner, settings) = Open();
            var closeAsked = 0;
            panel.CloseRequested += () => closeAsked++;

            panel.SkipCommand.Execute(null);
            panel.Closed();

            Assert.That(closeAsked, Is.EqualTo(1));
            runner.Dialogs.Verify(x => x.ShowSystemOpenFileDialog(It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            Assert.That(settings.ShowStartPanel, Is.True);
        }

        // File > Open Recent's list, the latest first. A pick opens it the way the menu does, and only once the panel has closed.
        [Test]
        public void ARecentPick_IsTheLatestFirst_AndOpensOnlyOnceThePanelHasClosed()
        {
            var older = Directory.CreateTempSubdirectory("ae_recent_").FullName;
            var latest = Directory.CreateTempSubdirectory("ae_recent_").FullName;
            File.WriteAllText(Path.Combine(latest, "a.txt"), "a");
            try
            {
                var (panel, runner, settings) = Open();
                runner.CreateCaContainer(); // the game's packs, which a pack of one's own is refused without
                settings.RecentPackFiles.Add(new RecentPackFileInfo(older, PackFileContainerType.SystemFolder, false));
                settings.RecentPackFiles.Add(new RecentPackFileInfo(latest, PackFileContainerType.SystemFolder, false));
                var closeAsked = 0;
                panel.CloseRequested += () => closeAsked++;
                bool Loaded() => runner.PackFileService.GetAllPackfileContainers().Any(c => c.SystemFilePath == latest);

                Assert.That(panel.Recent.Select(x => x.Path), Is.EqualTo(new[] { latest, older }));
                panel.Recent[0].Command.Execute(null);
                Assert.That((closeAsked, Loaded()), Is.EqualTo((1, false)));

                panel.Closed();
                Assert.That(Loaded(), Is.True);
            }
            finally
            {
                Directory.Delete(older, true);
                Directory.Delete(latest, true);
            }
        }

        // Closed with the window's X: no pick at all.
        [Test]
        public void ATickedBox_TurnsThePanelOff_HoweverThePanelCloses()
        {
            var (panel, _, settings) = Open();
            panel.DontShowAgain = true;

            panel.Closed();

            Assert.That(settings.ShowStartPanel, Is.False);
        }

        [Test]
        public void EveryBindingThePanelUses_NamesAPropertyOfIt_AndEveryTextIsInEveryLanguage()
        {
            var file = Path.Combine(SettingsView_Tests.Root(), "AssetEditor", "Views", "StartPanel", "StartPanelWindow.xaml");
            var bindings = SettingsView_Tests.Bindings(XDocument.Load(file)).ToList();
            var keys = SettingsView_Tests.LocKeys(file);

            // A recent row binds to its item; Recent.Count to the list.
            static bool Names(Type type, string path) => path.Split('.').Aggregate((Type?)type, (t, p) => t?.GetProperty(p)?.PropertyType) != null;
            Assert.That(bindings, Has.Count.GreaterThan(4), "the panel binds too little to mean anything");
            Assert.That(bindings.Where(b => !Names(SettingsView_Tests.InTemplate(b.Element) ? typeof(RecentPackFileItem) : typeof(StartPanelViewModel), b.Path))
                .Select(b => b.Path), Is.Empty);
            Assert.That(keys, Has.Count.GreaterThan(8), "the walk found too few keys to mean anything");
            Assert.That(SettingsView_Tests.MissingInALanguage(keys), Is.Empty);
        }
    }
}
