using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.LoadAndSave
{
    // Ctrl+S saves the open tab through ISaveableEditor.Save, which in Kitbash did nothing and said it had saved.
    internal class KitbashSaveAsEditorTests
    {
        [Test]
        public void Save_RunsTheToolbarsSave()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(TestFiles.KarlPackFile, true);
            var editor = (ISaveableEditor)runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var saveSettings = runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            saveSettings.IsUserInitialized = true;
            saveSettings.DisplayDialogOnNextSave = false;

            Assert.That(editor.Save(), Is.True);
            Assert.That(outputPackFile!.GetFileCount(), Is.EqualTo(2), "the model and its wsmodel, as the toolbar's Save writes them");
        }
    }
}
