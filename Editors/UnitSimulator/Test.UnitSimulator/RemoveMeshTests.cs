using AnimationEditor.Common.ReferenceModel;
using Editors.UnitSimulator;
using Editors.Shared.Core.Common.ReferenceModel;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.UnitSimulator
{
    // The X beside a part's Browse button. Its Click is a method binding by name, {s:MethodBinding RemoveMesh}, and no part had
    // a RemoveMesh: a binding that names nothing does nothing, so the button never worked, in every 3D editor that shares the panel.
    public class RemoveMeshTests
    {
        [Test, Apartment(ApartmentState.STA)]
        public void TheXBesideBrowse_TakesThePartsModelOutOfTheScene_AndKeepsItsSkeleton()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var sim = UnitSimulatorAppTests.Open(runner);
            Assert.That(SceneObjectView.TakeModel(new System.Windows.DataObject(runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!), sim.Rider), Is.True);
            var model = sim.Rider.Data.ModelNode;
            var skeleton = sim.Rider.Data.SkeletonName.Value;
            Assert.That(sim.Rider.Data.ParentNode.Children, Does.Contain(model), "the model was never in the scene, so this proves nothing");

            // Called by name, as the button's binding calls it.
            var remove = typeof(SceneObjectViewModel).GetMethod("RemoveMesh", Type.EmptyTypes);
            Assert.That(remove, Is.Not.Null, "the X button calls RemoveMesh, which a part does not have");
            remove!.Invoke(sim.Rider, null);

            Assert.That(sim.Rider.Data.ParentNode.Children, Does.Not.Contain(model));
            Assert.That(sim.Rider.Data.ModelNode, Is.Null);
            Assert.That(sim.Rider.Data.MeshName.Value, Is.Empty);
            Assert.That(sim.Rider.Data.SkeletonName.Value, Is.EqualTo(skeleton).And.Not.Empty);
            Assert.DoesNotThrow(() => remove.Invoke(sim.Rider, null), "a second X, on a part with no model");
        }
    }
}
