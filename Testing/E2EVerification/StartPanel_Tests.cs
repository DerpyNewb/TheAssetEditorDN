using System.Xml.Linq;
using AssetEditor.ViewModels;
using Moq;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification
{
    // The panel the app opens with: a project (recommended) or a .pack edited in place, and a box that turns it off.
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
            var bindings = SettingsView_Tests.Bindings(XDocument.Load(file)).Select(b => b.Path).ToList();
            var keys = SettingsView_Tests.LocKeys(file);

            Assert.That(bindings, Has.Count.GreaterThan(4), "the panel binds too little to mean anything");
            Assert.That(bindings.Where(b => typeof(StartPanelViewModel).GetProperty(b) == null), Is.Empty);
            Assert.That(keys, Has.Count.GreaterThan(8), "the walk found too few keys to mean anything");
            Assert.That(SettingsView_Tests.MissingInALanguage(keys), Is.Empty);
        }
    }
}
