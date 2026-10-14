using System.Windows;
using System.Windows.Controls;
using AnimationEditor.Common.BaseControl;
using AnimationEditor.Common.ReferenceModel;
using Editors.UnitSimulator;
using Shared.Core.PackFiles.Models;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.UnitSimulator
{
    // A model dragged from the pack tree onto a part's panel or onto the 3D view, in the Unit Simulator, the editor with two parts.
    public class ModelDropTests
    {
        const string Model = "skv_throt.variantmeshdefinition";

        static (UnitSimulatorViewModel Sim, PackFile Model) Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            return (UnitSimulatorAppTests.Open(runner), runner.PackFileService.FindFile($@"variantmeshes\variantmeshdefinitions\{Model}")!);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void AModelDroppedOnAPanel_GoesIntoThatPart_AndNoOther()
        {
            var (sim, model) = Open();

            Assert.That(SceneObjectView.TakeModel(new DataObject(model), sim.Rider), Is.True);

            Assert.That(sim.Rider.Data.MeshName.Value, Is.EqualTo(Model));
            Assert.That(sim.Mount.Data.MeshName.Value, Is.Empty);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void AFileThatIsNoModel_DroppedOnAPanel_StopsThere_AndChangesNothing()
        {
            var (sim, _) = Open();

            Assert.That(SceneObjectView.TakeModel(new DataObject(PackFile.CreateFromASCII("notes.txt", "x")), sim.Rider), Is.True);
            Assert.That(SceneObjectView.TakeModel(new DataObject(DataFormats.FileDrop, new[] { @"c:\a.pack" }), sim.Rider), Is.False);

            Assert.That(sim.Rider.Data.MeshName.Value, Is.Empty);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void AModelDroppedOnTheView_AsksWhichPart_AndOnlyThePickedOneTakesIt()
        {
            var (sim, model) = Open();

            var items = EditorHostView.ModelDropMenu(sim.ModelDropTargets, model)!.Items.Cast<MenuItem>().ToList();
            Assert.That(items.Select(x => x.Header), Is.EqualTo(new[] { "Mount", "Rider" }));
            Assert.That(sim.Mount.Data.MeshName.Value, Is.Empty);

            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(sim.Rider.Data.MeshName.Value, Is.EqualTo(Model));
            Assert.That(sim.Mount.Data.MeshName.Value, Is.Empty);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheView_OffersOnlyPartsWhosePanelShows_AndOneGoesStraightIn()
        {
            var (sim, model) = Open();
            sim.Rider.IsControlVisible = false;

            Assert.That(sim.ModelDropTargets, Is.EqualTo(new[] { sim.Mount }));
            Assert.That(EditorHostView.ModelDropMenu(sim.ModelDropTargets, model), Is.Null);
            Assert.That(sim.Mount.Data.MeshName.Value, Is.EqualTo(Model));
        }
    }
}
