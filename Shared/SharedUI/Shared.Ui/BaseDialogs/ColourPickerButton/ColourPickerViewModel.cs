using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Xna.Framework;

namespace Shared.Ui.BaseDialogs.ColourPickerButton
{
    public partial class ColourPickerViewModel : ObservableObject
    {
        private readonly Action<Vector3>? _onColourChangedCallback;

        public Vector3 SelectedColour { get; private set; }
        [ObservableProperty] System.Windows.Media.Color _pickedColor;

        public ColourPickerViewModel(Vector3 colour, Action<Vector3>? OnColourChangedCallback = null)
        {
            SelectedColour = colour;
            _onColourChangedCallback = OnColourChangedCallback;
            _pickedColor = ToPicked(colour);
        }

        // The picker raises ColorChanged when it is first drawn too. A colour it shows as the one stored is no change: writing it
        // back would put the byte-rounded value in place of the exact one, and two copies of one material, one shown and one not,
        // would then differ (Kitbash's combine refuses them).
        [RelayCommand]
        public void OnHandleColourChanged()
        {
            if (PickedColor == ToPicked(SelectedColour))
                return;

            SelectedColour = new Vector3(PickedColor.R / 255f, PickedColor.G / 255f, PickedColor.B / 255f);
            _onColourChangedCallback?.Invoke(SelectedColour);
        }

        /// <summary>Shows a colour set from outside (an undo) without handing it back to the callback.</summary>
        public void Show(Vector3 colour)
        {
            SelectedColour = colour;
            PickedColor = ToPicked(colour);
        }

        static System.Windows.Media.Color ToPicked(Vector3 colour) =>
            System.Windows.Media.Color.FromRgb(ToByte(colour.X), ToByte(colour.Y), ToByte(colour.Z));

        static byte ToByte(float channel) => (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
    }
}
