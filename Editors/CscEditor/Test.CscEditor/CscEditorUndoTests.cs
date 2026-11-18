using Editors.CscEditor.ViewModels;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Test.TestingUtility.Shared;

namespace Test.CscEditor
{
    // The CSC editor's undo: an added element and a re-parent are steps of their own, an edit not yet checkpointed is what Undo
    // takes back, and undo and redo rebuild the scene and select the element the step was made in.
    public class CscEditorUndoTests
    {
        [Test]
        public void AddReparentAndEdit_UndoAndRedo()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry("composite_scene", PackFile.CreateFromBytes("undo.csc", CscNestedElementTests.BuildSceneBytes()))]);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(@"composite_scene\undo.csc")!);
            var vm = runner.GetRequiredServiceInCurrentEditorScope<CscEditorViewModel>();
            var scene = () => vm.SceneData!;
            var elements = scene().Elements.Count;
            var begin = scene().Elements.First(x => x.Id == 0).Begin;

            vm.SelectedElement = vm.RootElements[0];
            vm.AddLocatorCommand.Execute(null);
            Assert.That(scene().Elements, Has.Count.EqualTo(elements + 1));
            var locator = vm.SelectedElement!.Element.Id;
            Assert.That(vm.SelectedElement.Element.Parent?.Id, Is.EqualTo(0), "added under the selected element");

            vm.ReparentElement(vm.SelectedElement, null);
            var first = vm.FindViewModel(scene().Elements.First(x => x.Id == 0))!;
            vm.SelectedElement = first;
            first.Begin = begin + 2;

            // Element count, element 0's Begin, the locator's parent (-1 for top level, -2 for none) and the selected element.
            (int, float, int, int?) Now() => (scene().Elements.Count, scene().Elements.First(x => x.Id == 0).Begin,
                scene().Elements.FirstOrDefault(x => x.Id == locator) is { } l ? l.Parent?.Id ?? -1 : -2, vm.SelectedElement?.Element.Id);

            vm.Undo();
            Assert.That(Now(), Is.EqualTo((elements + 1, begin, -1, (int?)0)), "the edit is undone on its own, and the edited element is selected");
            vm.Undo();
            Assert.That(Now().Item3, Is.EqualTo(0), "the locator is back under element 0");
            vm.Undo();
            Assert.That(Now().Item3, Is.EqualTo(-2), "the locator is gone");
            vm.Undo();
            Assert.That(Now().Item1, Is.EqualTo(elements), "nothing left to undo");

            vm.Redo();
            vm.Redo();
            vm.Redo();
            Assert.That(Now(), Is.EqualTo((elements + 1, begin + 2, -1, (int?)0)));
            Assert.That(vm.HasUnsavedChanges, Is.True);
        }
    }
}
