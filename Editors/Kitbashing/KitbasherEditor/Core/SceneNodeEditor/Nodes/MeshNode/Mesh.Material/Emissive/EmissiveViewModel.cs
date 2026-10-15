using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.UserInterface;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Emissive
{
    public partial class EmissiveViewModel : ObservableObject
    {
        private readonly EmissiveCapability _emissiveCapability;
        private readonly ValueUndo? _undo;

        [ObservableProperty] ShaderTextureViewModel _emissiveTexture;
        [ObservableProperty] ShaderTextureViewModel _emissiveDistortionTexture;
        [ObservableProperty] Vector2ViewModel _emissiveDirection;
        [ObservableProperty] float _emissiveDistortStrength;
        [ObservableProperty] float _emissiveFresnelStrength;
        [ObservableProperty] ColourPickerViewModel _emissiveTint;
        [ObservableProperty] ColourPickerViewModel _gradient0;
        [ObservableProperty] ColourPickerViewModel _gradient1;
        [ObservableProperty] ColourPickerViewModel _gradient2;
        [ObservableProperty] ColourPickerViewModel _gradient3;
        [ObservableProperty] float _gradientTime0;
        [ObservableProperty] float _gradientTime1;
        [ObservableProperty] float _gradientTime2;
        [ObservableProperty] float _gradientTime3;

        [ObservableProperty] float _emissiveSpeed;
        [ObservableProperty] float _emissivePulseSpeed;
        [ObservableProperty] float _emissivePulseStrength;
        [ObservableProperty] float _emissiveStrength;
        [ObservableProperty] Vector2ViewModel _emissiveTiling;

        public EmissiveViewModel(EmissiveCapability emissiveCapability, IUiCommandFactory uiCommandFactory, IPackFileService packFileService, IScopedResourceLibrary resourceLibrary, IStandardDialogs packFileUiProvider, ValueUndo? undo = null)
        {
            _emissiveCapability = emissiveCapability;
            _undo = undo;

            _emissiveTexture = new ShaderTextureViewModel(emissiveCapability.Emissive, packFileService, uiCommandFactory, resourceLibrary, packFileUiProvider);
            _emissiveDistortionTexture = new ShaderTextureViewModel(emissiveCapability.EmissiveDistortion, packFileService, uiCommandFactory, resourceLibrary, packFileUiProvider);

            _emissiveDirection = new Vector2ViewModel(emissiveCapability.EmissiveDirection, OnEmissiveDirectionChanged);
            _emissiveDistortStrength = emissiveCapability.EmissiveDistortStrength;
            _emissiveFresnelStrength = emissiveCapability.EmissiveFresnelStrength;

            _emissiveTint = new ColourPickerViewModel(emissiveCapability.EmissiveTint, Colour("emissive tint", () => _emissiveCapability.EmissiveTint, c => _emissiveCapability.EmissiveTint = c, () => EmissiveTint));

            _gradient0 = new ColourPickerViewModel(emissiveCapability.GradientColours[0], Colour("gradient colour 1", () => _emissiveCapability.GradientColours[0], c => _emissiveCapability.GradientColours[0] = c, () => Gradient0));
            _gradient1 = new ColourPickerViewModel(emissiveCapability.GradientColours[1], Colour("gradient colour 2", () => _emissiveCapability.GradientColours[1], c => _emissiveCapability.GradientColours[1] = c, () => Gradient1));
            _gradient2 = new ColourPickerViewModel(emissiveCapability.GradientColours[2], Colour("gradient colour 3", () => _emissiveCapability.GradientColours[2], c => _emissiveCapability.GradientColours[2] = c, () => Gradient2));
            _gradient3 = new ColourPickerViewModel(emissiveCapability.GradientColours[3], Colour("gradient colour 4", () => _emissiveCapability.GradientColours[3], c => _emissiveCapability.GradientColours[3] = c, () => Gradient3));

            _gradientTime0 = emissiveCapability.GradientTimes[0];
            _gradientTime1 = emissiveCapability.GradientTimes[1];
            _gradientTime2 = emissiveCapability.GradientTimes[2];
            _gradientTime3 = emissiveCapability.GradientTimes[3];

            _emissiveSpeed = emissiveCapability.EmissiveSpeed;
            _emissivePulseSpeed = emissiveCapability.EmissivePulseSpeed;
            _emissivePulseStrength = emissiveCapability.EmissivePulseStrength;
            _emissiveStrength = emissiveCapability.EmissiveStrength;

            _emissiveTiling = new Vector2ViewModel(emissiveCapability.EmissiveTiling, OnEmissiveTilingChanged);
        }

        partial void OnEmissiveDistortStrengthChanged(float value) => _emissiveCapability.EmissiveDistortStrength = value;
        partial void OnEmissiveSpeedChanged(float value) => _emissiveCapability.EmissiveSpeed = value;
        partial void OnEmissivePulseStrengthChanged(float value) => _emissiveCapability.EmissivePulseStrength = value;
        partial void OnEmissivePulseSpeedChanged(float value) => _emissiveCapability.EmissivePulseSpeed = value;
        void OnEmissiveDirectionChanged(Vector2 value) => _emissiveCapability.EmissiveDirection = value;
        partial void OnEmissiveStrengthChanged(float value) => _emissiveCapability.EmissiveStrength = value;
        partial void OnEmissiveFresnelStrengthChanged(float value) => _emissiveCapability.EmissiveFresnelStrength = value;

        void OnEmissiveTilingChanged(Vector2 value) => _emissiveCapability.EmissiveTiling = value;

        partial void OnGradientTime0Changed(float value) => _emissiveCapability.GradientTimes[0] = value;
        partial void OnGradientTime1Changed(float value) => _emissiveCapability.GradientTimes[1] = value;
        partial void OnGradientTime2Changed(float value) => _emissiveCapability.GradientTimes[2] = value;
        partial void OnGradientTime3Changed(float value) => _emissiveCapability.GradientTimes[3] = value;

        // Each slider edit goes on the Kitbash undo stack, a whole drag as one step; undo sets the field, which sets the material.
        partial void OnEmissiveDistortStrengthChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(EmissiveDistortStrength)), oldValue, newValue, v => EmissiveDistortStrength = v, nameof(EmissiveDistortStrength));
        partial void OnEmissiveFresnelStrengthChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(EmissiveFresnelStrength)), oldValue, newValue, v => EmissiveFresnelStrength = v, nameof(EmissiveFresnelStrength));
        partial void OnGradientTime0Changed(float oldValue, float newValue) => _undo?.Changed((this, nameof(GradientTime0)), oldValue, newValue, v => GradientTime0 = v, nameof(GradientTime0));
        partial void OnGradientTime1Changed(float oldValue, float newValue) => _undo?.Changed((this, nameof(GradientTime1)), oldValue, newValue, v => GradientTime1 = v, nameof(GradientTime1));
        partial void OnGradientTime2Changed(float oldValue, float newValue) => _undo?.Changed((this, nameof(GradientTime2)), oldValue, newValue, v => GradientTime2 = v, nameof(GradientTime2));
        partial void OnGradientTime3Changed(float oldValue, float newValue) => _undo?.Changed((this, nameof(GradientTime3)), oldValue, newValue, v => GradientTime3 = v, nameof(GradientTime3));
        partial void OnEmissiveSpeedChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(EmissiveSpeed)), oldValue, newValue, v => EmissiveSpeed = v, nameof(EmissiveSpeed));
        partial void OnEmissivePulseSpeedChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(EmissivePulseSpeed)), oldValue, newValue, v => EmissivePulseSpeed = v, nameof(EmissivePulseSpeed));
        partial void OnEmissivePulseStrengthChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(EmissivePulseStrength)), oldValue, newValue, v => EmissivePulseStrength = v, nameof(EmissivePulseStrength));
        partial void OnEmissiveStrengthChanged(float oldValue, float newValue) => _undo?.Changed((this, nameof(EmissiveStrength)), oldValue, newValue, v => EmissiveStrength = v, nameof(EmissiveStrength));

        Action<Vector3> Colour(string name, Func<Vector3> get, Action<Vector3> set, Func<ColourPickerViewModel> picker) => colour =>
        {
            var old = get();
            set(colour);
            _undo?.Changed((this, name), old, colour, c => { set(c); picker().Show(c); }, name);
        };

    }
}
