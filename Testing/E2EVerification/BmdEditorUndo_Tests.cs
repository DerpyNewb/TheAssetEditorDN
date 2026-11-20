using Editors.BmdEditor.ViewModels;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Bmd;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification
{
    // The BMD editor's undo: an added element is a step of its own, an edit not yet checkpointed is what Undo takes back, and
    // undo and redo rebuild the file and select the element the step was made in.
    internal class BmdEditorUndo_Tests
    {
        [Test]
        public void AddAndEdit_UndoAndRedo()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var pack = runner.CreateOutputPack();
            var empty = BmdWriter.Write(new BmdFile { Header = { FastBinVersion = 27 } });
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry("prefabs", PackFile.CreateFromBytes("undo.bmd", empty))]);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(@"prefabs\undo.bmd")!);
            var vm = runner.GetRequiredServiceInCurrentEditorScope<BmdEditorViewModel>();

            vm.AddPointLightCommand.Execute(null);
            vm.AddPointLightCommand.Execute(null);
            vm.Undo();
            Assert.That((vm.PointLights.Count, vm.SelectedComponent), Is.EqualTo((1, (BmdElementViewModel?)null)), "undoing an add selects nothing, not whatever now holds its place");
            vm.Redo();
            Assert.That(vm.SelectedComponent, Is.SameAs(vm.PointLights[1]), "redo selects the light it brings back");

            var light = (PointLightInfoViewModel)vm.PointLights[0];
            var x = light.PositionX;
            var place = vm.AllElements.IndexOf(light);
            vm.SelectComponent(light);
            light.PositionX = x + 5;

            (int, float, int) Now() => (vm.PointLights.Count, vm.PointLights.Count > 0 ? ((PointLightInfoViewModel)vm.PointLights[0]).PositionX : float.NaN,
                vm.SelectedComponent == null ? -1 : vm.AllElements.IndexOf(vm.SelectedComponent));

            vm.Undo();
            Assert.That(Now(), Is.EqualTo((2, x, place)), "the edit is undone on its own, and the moved light is selected");
            vm.Undo();
            Assert.That(Now().Item1, Is.EqualTo(1), "the second light is gone");
            vm.Undo();
            Assert.That(Now().Item1, Is.EqualTo(0), "the first light is gone");
            vm.Undo();
            Assert.That(Now().Item1, Is.EqualTo(0), "nothing left to undo");

            vm.Redo();
            vm.Redo();
            vm.Redo();
            Assert.That(Now(), Is.EqualTo((2, x + 5, place)));
            Assert.That(vm.HasUnsavedChanges, Is.True);
        }

        // A delete is a step of its own: undoing the add after it leaves the deleted light deleted.
        [Test]
        public void Delete_IsAStepOfItsOwn()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var pack = runner.CreateOutputPack();
            var empty = BmdWriter.Write(new BmdFile { Header = { FastBinVersion = 27 } });
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry("prefabs", PackFile.CreateFromBytes("delete.bmd", empty))]);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(@"prefabs\delete.bmd")!);
            var vm = runner.GetRequiredServiceInCurrentEditorScope<BmdEditorViewModel>();

            vm.AddPointLightCommand.Execute(null);
            vm.AddPointLightCommand.Execute(null);
            vm.SelectComponent(vm.PointLights[0]);
            vm.DeleteSelectedCommand.Execute(null);
            Assert.That(vm.PointLights.Count, Is.EqualTo(1), "deleted");

            vm.AddSpotLightCommand.Execute(null);
            vm.Undo();
            Assert.That((vm.PointLights.Count, vm.SpotLights.Count), Is.EqualTo((1, 0)), "the add is undone and the delete stays");
            vm.Undo();
            Assert.That(vm.PointLights.Count, Is.EqualTo(2), "then the delete is undone");
            vm.Redo();
            Assert.That(vm.PointLights.Count, Is.EqualTo(1), "and redone");
        }
    }
}
