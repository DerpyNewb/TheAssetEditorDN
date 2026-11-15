using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shared.Ui.BaseDialogs.MathViews;

namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    public partial class SkeletonBoneNode_new : ObservableObject
    {
        public SkeletonBoneNode_new(string boneName, int boneIndex, int parentBoneIndex)
        {
            BoneName = boneName;
            BoneIndex = boneIndex;
            ParnetBoneIndex = parentBoneIndex;
        }

        [ObservableProperty] int _boneIndex =0;
        [ObservableProperty] string _boneName  = "";
        [ObservableProperty] int _parnetBoneIndex = -1;
        [ObservableProperty] int _mappedIndex = -1;
        [ObservableProperty] bool _hasMapping = false;
        [ObservableProperty] string _mappedFrom = "";   // the source bone's name, for the tree

        [ObservableProperty] bool _isLocalOffset = false;  // Not implemented, testing
        [ObservableProperty] float _boneLengthMult = 1;
        [ObservableProperty] Vector3ViewModel _rotationOffset = new(0, 0, 0);
        [ObservableProperty] Vector3ViewModel _translationOffset = new(0, 0, 0);

        [ObservableProperty] bool _forceSnapToWorld  = false;
        [ObservableProperty] bool _freezeTranslation = false;
        [ObservableProperty] bool _freezeRotation = false;
        [ObservableProperty] bool _freezeRotationZ  = false;
        [ObservableProperty] bool _applyTranslation  = true;
        [ObservableProperty] bool _applyRotation = true;

        [ObservableProperty] SkeletonBoneNode_new? _selectedRelativeBone = null;

        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _children = [];

        /// <summary>Puts every setting back to the defaults above. The mapping is not a setting and stays.</summary>
        public void ResetSettings()
        {
            IsLocalOffset = false;
            BoneLengthMult = 1;
            RotationOffset = new(0, 0, 0);
            TranslationOffset = new(0, 0, 0);
            ForceSnapToWorld = false;
            FreezeTranslation = false;
            FreezeRotation = false;
            FreezeRotationZ = false;
            ApplyTranslation = true;
            ApplyRotation = true;
            SelectedRelativeBone = null;
        }
    }
}
