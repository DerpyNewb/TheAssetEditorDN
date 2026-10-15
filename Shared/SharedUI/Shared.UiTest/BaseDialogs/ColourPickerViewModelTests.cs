using Microsoft.Xna.Framework;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace Shared.UiTest.BaseDialogs
{
    internal class ColourPickerViewModelTests
    {
        // A sun dragon's emissive gradient, split into submeshes: each one whose panel was shown four times read 79/256, the rest
        // 84/256, and Combine refused them as different materials.
        [Test]
        public void ShownAgainAndAgain_TheColourIsLeftExactlyAsItWas()
        {
            var written = new List<Vector3>();
            var picker = new ColourPickerViewModel(new Vector3(0.328125f), written.Add);

            for (var i = 0; i < 4; i++)
                picker.HandleColourChangedCommand.Execute(null);

            Assert.That(written, Is.Empty);
            Assert.That(picker.SelectedColour, Is.EqualTo(new Vector3(0.328125f)));
        }

        [Test]
        public void APickedColour_IsWrittenOnce_AndWhiteIsOne()
        {
            var written = new List<Vector3>();
            var picker = new ColourPickerViewModel(new Vector3(0.328125f), written.Add);

            picker.PickedColor = System.Windows.Media.Color.FromRgb(255, 128, 0);
            picker.HandleColourChangedCommand.Execute(null);
            picker.HandleColourChangedCommand.Execute(null);

            Assert.That(written, Is.EqualTo(new[] { new Vector3(1, 128 / 255f, 0) }));
        }
    }
}
