using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Rendering.Materials.Capabilities;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using Microsoft.Xna.Framework;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Emissive
{
    public partial class TintViewModel : ObservableObject
    {
        private readonly TintCapability _tintCapability;
        private readonly ValueUndo? _undo;

        [ObservableProperty] bool _applyCapability;

        [ObservableProperty] Vector4ViewModel _tintMask;
        [ObservableProperty] ColourPickerViewModel _tintColour;
        [ObservableProperty] float _tintVariation;

        [ObservableProperty] bool _useFactionColours;
        [ObservableProperty] ColourPickerViewModel _factionColour0;
        [ObservableProperty] ColourPickerViewModel _factionColour1;
        [ObservableProperty] ColourPickerViewModel _factionColour2;
        [ObservableProperty] Vector4ViewModel _factionColour3Mask;

        public TintViewModel(TintCapability tintCapability, ValueUndo? undo = null)
        {
            _tintCapability = tintCapability;
            _undo = undo;

            _applyCapability = _tintCapability.ApplyCapability;

            _tintMask = new Vector4ViewModel(_tintCapability.DiffuseTintMask, (c) => _tintCapability.DiffuseTintMask = c);
            _tintColour = new ColourPickerViewModel(_tintCapability.DiffuseTintColour, Colour("tint colour", () => _tintCapability.DiffuseTintColour, c => _tintCapability.DiffuseTintColour = c, () => TintColour));
            _tintVariation = _tintCapability.DiffuseTintVariation;

            _useFactionColours = _tintCapability.UseFactionColours;
            _factionColour0 = new ColourPickerViewModel(_tintCapability.FactionColours[0], Colour("faction colour 1", () => _tintCapability.FactionColours[0], c => _tintCapability.FactionColours[0] = c, () => FactionColour0));
            _factionColour1 = new ColourPickerViewModel(_tintCapability.FactionColours[1], Colour("faction colour 2", () => _tintCapability.FactionColours[1], c => _tintCapability.FactionColours[1] = c, () => FactionColour1));
            _factionColour2 = new ColourPickerViewModel(_tintCapability.FactionColours[2], Colour("faction colour 3", () => _tintCapability.FactionColours[2], c => _tintCapability.FactionColours[2] = c, () => FactionColour2));
            _factionColour3Mask = new Vector4ViewModel(_tintCapability.Faction3Mask, (c) => _tintCapability.Faction3Mask = c);
        }

        partial void OnTintVariationChanged(float value) => _tintCapability.DiffuseTintVariation = value;
        // On the Kitbash undo stack, a whole drag as one step; undo sets the field, which sets the material.
        partial void OnTintVariationChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(TintVariation)), oldValue, newValue, v => TintVariation = v, "tint variation");

        Action<Vector3> Colour(string name, Func<Vector3> get, Action<Vector3> set, Func<ColourPickerViewModel> picker) => colour =>
        {
            var old = get();
            set(colour);
            _undo?.Changed((this, name), old, colour, c => { set(c); picker().Show(c); }, name);
        };
        partial void OnUseFactionColoursChanged(bool value) => _tintCapability.UseFactionColours = value;
        partial void OnApplyCapabilityChanged(bool value) => _tintCapability.ApplyCapability = value;
    }
}
