using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Emissive;
using GameWorld.Core.Rendering.Materials.Capabilities;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.Ui.Common;
using Shared.Ui.Common.Behaviors;
using Test.TestingUtility.TestUtility;

namespace Test.KitbashEditor
{
    // A material panel's sliders and colours go on the Kitbash undo stack: a whole drag is one step, and undo puts the value back
    // in the material and in the panel.
    internal class MaterialPanelUndoTests
    {
        [Test]
        public void TintVariationDrag_And_TintColour_Undo()
        {
            var commands = new CommandManager(new Mock<IEventHub>().Object, MockScopedLogger.Create());
            var tint = new TintCapability { DiffuseTintVariation = 0.5f, DiffuseTintColour = new Vector3(0.2f) };
            var panel = new TintViewModel(tint, new ValueUndo(commands));

            ScrubGesture.Start();
            foreach (var value in new[] { 0.6f, 0.7f, 0.8f })
                panel.TintVariation = value;
            ScrubGesture.Stop();
            panel.TintColour.PickedColor = System.Windows.Media.Color.FromRgb(255, 0, 0);
            panel.TintColour.HandleColourChangedCommand.Execute(null);
            Assert.That((tint.DiffuseTintVariation, tint.DiffuseTintColour), Is.EqualTo((0.8f, new Vector3(1, 0, 0))));

            commands.Undo();
            Assert.That(tint.DiffuseTintColour, Is.EqualTo(new Vector3(0.2f)));
            Assert.That(panel.TintColour.SelectedColour, Is.EqualTo(new Vector3(0.2f)), "the picker shows it");

            commands.Undo();
            Assert.That((tint.DiffuseTintVariation, panel.TintVariation, commands.CanUndo()), Is.EqualTo((0.5f, 0.5f, false)));
        }
    }
}
