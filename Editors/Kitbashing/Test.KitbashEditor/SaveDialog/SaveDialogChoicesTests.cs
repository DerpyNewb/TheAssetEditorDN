using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Lod;
using KitbasherEditor.ViewModels.SaveDialog;
using Shared.Core.Commands;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.SaveDialog
{
    // Upstream #322: a second Kitbash editor's save started from the defaults again, whatever the first one's dialog said.
    public class SaveDialogChoicesTests
    {
        [Test]
        public void TheSaveDialogsChoices_AreOfferedByTheNextEditor()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            var karl = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!;

            runner.CommandFactory.Create<OpenEditorCommand>().Execute(karl);
            var dialog = runner.GetRequiredServiceInCurrentEditorScope<SaveDialogViewModel>();
            dialog.Initialize(runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>());
            dialog.OnlySaveVisible = false;
            dialog.SelectedLodStrategy = dialog.LodStrategies.First(x => x.Value == LodStrategy.Lod0ForAll);
            dialog.ApplySettings();

            // Another file: opening Karl again brings his editor back to the front.
            var general = runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.rigid_model_v2")!;
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(general);
            var editors = runner.ScopeRepository.GetEditorHandles();
            Assert.That(editors.Count(), Is.EqualTo(2));
            var next = runner.ScopeRepository.GetRequiredService<GeometrySaveSettings>(editors.Last());     // the runner's helper takes the first
            Assert.That(next.IsUserInitialized, Is.False, "the second editor's settings are the first one's object");
            Assert.That(next.OnlySaveVisible, Is.False);
            Assert.That(next.LodGenerationMethod, Is.EqualTo(LodStrategy.Lod0ForAll));
        }
    }
}
