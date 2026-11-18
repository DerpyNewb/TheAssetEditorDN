using System.IO;
using System.Linq;
using Editors.CampaignAnimationSetEditor.ViewModels;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Test.CampaignAnimationSetEditor
{
    // The set editor's undo: a row added or removed is a step of its own, a typed edit not yet checkpointed becomes one when
    // Undo is pressed, and undo and redo put back the set and the status that was selected.
    [TestFixture]
    public class CampaignAnimationSetUndoTests
    {
        static PackFile Fixture(string name) => PackFile.CreateFromBytes(name, File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name)));

        static CampaignAnimationSetEditorViewModel Open(string name)
        {
            var vm = new CampaignAnimationSetEditorViewModel(new Mock<IPackFileService>().Object, new Mock<IFileSaveService>().Object, new Mock<IStandardDialogs>().Object, null!);
            vm.LoadFile(Fixture(name));
            return vm;
        }

        [Test]
        public void RowsAndTypedEdits_UndoAndRedo()
        {
            var vm = Open("cam_hu6_gor_axe.bin");
            var status = vm.Statuses.Last(x => x.Idle.Count > 1);
            Assert.That(status, Is.Not.SameAs(vm.Statuses[0]), "the selection has to be one undo could lose");
            var name = status.Name;
            var (rows, first, second) = (status.Idle.Count, status.Idle[0].Animation, status.Idle[1].Animation);
            vm.SelectedStatus = status;
            Assert.That(vm.HasUnsavedChanges, Is.False);

            status.Idle.RemoveAt(0);
            vm.SelectedStatus.Idle[0].Animation = "typed";
            Assert.That(vm.HasUnsavedChanges, Is.True);

            IdleRow0 Now() => new(vm.SelectedStatus!.Name, vm.SelectedStatus.Idle.Count, vm.SelectedStatus.Idle[0].Animation);

            vm.UndoAction();
            Assert.That(Now(), Is.EqualTo(new IdleRow0(name, rows - 1, second)), "the typed edit is undone on its own; the removed row stays removed");
            vm.UndoAction();
            Assert.That(Now(), Is.EqualTo(new IdleRow0(name, rows, first)), "the removed row comes back");
            vm.UndoAction();
            Assert.That(Now(), Is.EqualTo(new IdleRow0(name, rows, first)), "nothing left to undo");

            vm.RedoAction();
            Assert.That(Now(), Is.EqualTo(new IdleRow0(name, rows - 1, second)));
            vm.RedoAction();
            Assert.That(Now(), Is.EqualTo(new IdleRow0(name, rows - 1, "typed")));
        }

        // Undo must never bring back the previous file under the name of one that failed to open: Save would write it there.
        [Test]
        public void AFileThatFailsToOpen_LeavesNothingToUndo()
        {
            var vm = Open("cam_hu6_gor_axe.bin");
            vm.Statuses.First(x => x.Idle.Count > 0).Idle.RemoveAt(0);

            vm.LoadFile(PackFile.CreateFromBytes("broken.bin", [1, 2, 3]));
            Assert.That(vm.LoadError, Is.Not.Null);
            vm.UndoAction();

            Assert.That((vm.Bin, vm.Statuses.Count), Is.EqualTo(((Shared.GameFormats.AnimationPack.CampaignAnimationBin?)null, 0)));
        }

        record IdleRow0(string Status, int Rows, string Animation);
    }
}
