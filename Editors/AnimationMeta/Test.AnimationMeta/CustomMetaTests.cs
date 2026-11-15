using Editors.AnimationMeta.MetaEditor;
using Editors.AnimationMeta.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // A meta read from CA's packs or another mod's and saved at its own path overrides that file for every bin that names it, so
    // the save asks first. These run the standalone meta editor; SuperView's half is in SuperViewOpenFileTests.
    public class CustomMetaTests
    {
        const string Attack = "animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";

        AssetEditorTestRunner _runner = null!;
        IPackFileContainer _throt = null!;
        IPackFileContainer _output = null!;
        MetaDataEditorViewModel _editor = null!;
        List<IReadOnlyList<string>> _offered = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _output = _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true)!;
            _throt = _runner.PackFileService.GetAllPackfileContainers().Single(x => x.Name.Contains("Throt"));
            _offered = [];
        }

        void Open(bool vanilla)
        {
            _throt.IsCaPackFile = vanilla;
            _editor = _runner.CommandFactory.Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(_runner.PackFileService.FindFile(Attack)!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
            var field = _editor.Tags.SelectMany(x => x.Variables).First(x => !x.IsReadOnly && x.Scrub == Shared.Ui.Common.Behaviors.ScrubMode.Integer);
            field.ValueAsString = (int.Parse(field.ValueAsString) + 1).ToString();
        }

        void Answer(int? choice) => _runner.Dialogs
            .Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Callback<string, string, IReadOnlyList<string>>((_, _, choices) => _offered.Add(choices))
            .Returns(choice);

        PackFile? Saved(string path) => _runner.PackFileService.FindFile(path, _output);

        [Test]
        public void Cancel_WritesNothing_AndKeepsTheEdit()
        {
            Open(vanilla: true);
            Answer(2);
            _editor.SaveActionCommand.Execute(null);

            Assert.That(_offered, Is.EqualTo(new[] { new[] { "Save as custom meta", "Override vanilla file", "Cancel" } }));
            Assert.That((Saved(Attack), _editor.HasUnsavedChanges), Is.EqualTo(((PackFile?)null, true)));
        }

        [Test]
        public void Override_WritesAtTheVanillaPath_AndTheNextSaveDoesNotAsk()
        {
            Open(vanilla: true);
            Answer(1);
            _editor.SaveActionCommand.Execute(null);
            _editor.SaveActionCommand.Execute(null);

            Assert.That((Saved(Attack) != null, _offered.Count, _editor.HasUnsavedChanges), Is.EqualTo((true, 1, false)));
        }

        [Test]
        public void SaveAsCustom_OffersThePrefixedName_WritesTheTypedPath_OpensIt_AndLearnsThePrefix()
        {
            Open(vanilla: true);
            Answer(0);
            string? start = null;
            _runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>(), It.IsAny<string>()))
                .Callback<IPackFileService, List<string>, string>((_, _, path) => start = path)
                .Returns(new SaveDialogResult(true, null, @"Animations\Battle\Humanoid17\Throt_Whip_Catcher\Attacks\Derpy_HU17_Whip_Catcher_Attack_05"));
            _editor.SaveActionCommand.Execute(null);

            const string Custom = "animations/battle/humanoid17/throt_whip_catcher/attacks/derpy_hu17_whip_catcher_attack_05.anm.meta";
            Assert.That(start, Is.EqualTo("animations/battle/humanoid17/throt_whip_catcher/attacks/custom_hu17_whip_catcher_attack_05.anm.meta"));
            Assert.That((Saved(Custom) != null, Saved(Attack)), Is.EqualTo((true, (PackFile?)null)));
            Assert.That((_runner.PackFileService.GetFullPath(_editor.CurrentFile).Replace('\\', '/'), _editor.HasUnsavedChanges), Is.EqualTo((Custom, false)));
            Assert.That(_runner.ServiceProvider.GetRequiredService<ApplicationSettingsService>().CurrentSettings.CustomMetaPrefix, Is.EqualTo("derpy"));
        }

        [Test]
        public void SaveAsCustom_Cancelled_WritesNothing()
        {
            Open(vanilla: true);
            Answer(0);
            _runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>(), It.IsAny<string>()))
                .Returns(new SaveDialogResult(false, null, null));
            _editor.SaveActionCommand.Execute(null);

            Assert.That((_output.GetFileCount(), _editor.HasUnsavedChanges), Is.EqualTo((0, true)));
        }

        [Test]
        public void AnotherModsMeta_OffersToOverrideThatPacksFile()
        {
            Open(vanilla: false);
            Answer(2);
            _editor.SaveActionCommand.Execute(null);

            Assert.That(_offered.Single()[1], Is.EqualTo($"Override {_throt.Name}'s file"));
        }

        [Test]
        public void NoEditablePack_DoesNotAsk()
        {
            Open(vanilla: true);
            Answer(2);
            _runner.PackFileService.SetEditablePack(null);
            try
            {
                _editor.SaveActionCommand.Execute(null);
            }
            catch (Exception)
            {
                // Saving with no editable pack fails as it always has; the point is that nobody was asked first.
            }

            Assert.That(_offered, Is.Empty);
        }

        [Test]
        public void Source_SaysVanillaYourPackOrThePacksName()
        {
            Open(vanilla: true);
            var pfs = _runner.PackFileService;
            var vanilla = CustomMeta.Source(pfs, Attack);
            _throt.IsCaPackFile = false;
            var mod = CustomMeta.Source(pfs, Attack);
            Answer(1);
            _editor.SaveActionCommand.Execute(null);

            Assert.That((vanilla, mod, CustomMeta.Source(pfs, Attack), CustomMeta.Source(pfs, "animations/no/such.anm.meta"), CustomMeta.Source(pfs, "")),
                Is.EqualTo(("vanilla", _throt.Name, "your pack", "", "")));
        }

        [TestCase(@"Animations\A\B.anm.meta", "derpy", "animations/a/derpy_b.anm.meta")]
        [TestCase("animations/a/derpy_b.anm.meta", "derpy", "animations/a/derpy_b.anm.meta")]
        [TestCase("b.anm.meta", "derpy", "derpy_b.anm.meta")]
        public void Offered_PrefixesTheFileNameOnce(string path, string prefix, string expected) =>
            Assert.That(CustomMeta.Offered(path, prefix), Is.EqualTo(expected));
    }

    public static class CustomMetaTestAnswers
    {
        /// <summary>For a test that saves a meta back where it was read from, Throt's pack and not the editable one: the save asks
        /// first, and this answers Override.</summary>
        public static void AnswersOverride(this AssetEditorTestRunner runner) => runner.Dialogs
            .Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(1);
    }
}
