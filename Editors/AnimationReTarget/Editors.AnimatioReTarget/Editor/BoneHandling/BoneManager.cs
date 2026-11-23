using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Ui.Editors.BoneMapping;

namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    public partial class BoneManager : ObservableObject
    {
        record SkeletonInfo(string Name, AnimationFile Data);

        private readonly IStandardDialogs _standardDialogs;
        private readonly IAbstractFormFactory<BoneMappingWindow> _boneMappingWindowFactory;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;

        private ISkeletonBoneHighlighter? _skeletonBoneHighlighter;
        private SkeletonInfo? _sourceSkeleton;
        private SkeletonInfo? _targetSkeleton;

        [ObservableProperty] SkeletonBoneNode_new? _selectedBone;
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _bones = [];

        /// <summary>
        /// Raised when any bone's setting changes, an offset's X, Y or Z included, so the editor can regenerate at once. The
        /// mapping is not a setting: <see cref="MappingApplied"/> covers it.
        /// </summary>
        public event Action? SettingsChanged;

        static readonly HashSet<string> SettingNames =
        [
            nameof(SkeletonBoneNode_new.IsLocalOffset), nameof(SkeletonBoneNode_new.BoneLengthMult),
            nameof(SkeletonBoneNode_new.RotationOffset), nameof(SkeletonBoneNode_new.TranslationOffset),
            nameof(SkeletonBoneNode_new.ForceSnapToWorld), nameof(SkeletonBoneNode_new.FreezeTranslation),
            nameof(SkeletonBoneNode_new.FreezeRotation), nameof(SkeletonBoneNode_new.FreezeRotationZ),
            nameof(SkeletonBoneNode_new.ApplyTranslation), nameof(SkeletonBoneNode_new.ApplyRotation),
            nameof(SkeletonBoneNode_new.SelectedRelativeBone),
        ];

        static IEnumerable<SkeletonBoneNode_new> AllOf(IEnumerable<SkeletonBoneNode_new>? nodes) =>
            nodes?.SelectMany(x => AllOf(x.Children).Prepend(x)) ?? [];

        partial void OnBonesChanged(ObservableCollection<SkeletonBoneNode_new>? oldValue, ObservableCollection<SkeletonBoneNode_new> newValue)
        {
            foreach (var node in AllOf(oldValue))
            {
                node.PropertyChanged -= OnBonePropertyChanged;
                node.RotationOffset.OnValueChanged -= OnOffsetChanged;
                node.TranslationOffset.OnValueChanged -= OnOffsetChanged;
            }
            foreach (var node in AllOf(newValue))
            {
                node.PropertyChanged += OnBonePropertyChanged;
                node.RotationOffset.OnValueChanged += OnOffsetChanged;
                node.TranslationOffset.OnValueChanged += OnOffsetChanged;
            }
        }

        void OnBonePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not SkeletonBoneNode_new node || e.PropertyName is not { } name || !SettingNames.Contains(name))
                return;
            // "Clear" puts new offset vectors in; their edits have to be heard too.
            if (name == nameof(SkeletonBoneNode_new.RotationOffset))
                node.RotationOffset.OnValueChanged += OnOffsetChanged;
            if (name == nameof(SkeletonBoneNode_new.TranslationOffset))
                node.TranslationOffset.OnValueChanged += OnOffsetChanged;
            SettingsChanged?.Invoke();
        }

        void OnOffsetChanged(global::Shared.Ui.BaseDialogs.MathViews.Vector3ViewModel _) => SettingsChanged?.Invoke();

        public BoneManager(
            IStandardDialogs standardDialogs, 
            IAbstractFormFactory<BoneMappingWindow> boneMappingWindowFactory,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper)
        {
            _standardDialogs = standardDialogs;
            _boneMappingWindowFactory = boneMappingWindowFactory;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
        }

        partial void OnSelectedBoneChanged(SkeletonBoneNode_new? value)
        {
            if (_skeletonBoneHighlighter == null)
                return;
                
            if(value == null)
            {
                _skeletonBoneHighlighter.SelectSourceSkeletonBone(-1);
                _skeletonBoneHighlighter.SelectTargetSkeletonBone(-1);
            }
            else
            {
                _skeletonBoneHighlighter.SelectSourceSkeletonBone(value.BoneIndex);
                if(value.HasMapping)
                    _skeletonBoneHighlighter.SelectTargetSkeletonBone(value.MappedIndex);
                else
                    _skeletonBoneHighlighter.SelectTargetSkeletonBone(-1);
            }
        }

        public void SetSceneNodes(SceneObject source, SceneObject target, SceneObject generated)
        {
            // The tree is the target skeleton, drawn by the generated model; the mapping window calls its bones "source" too.
            _skeletonBoneHighlighter = new SkeletonBoneHighlighter(generated, source);
        }

        public void UpdateTargetSkeleton(string? skeletonName) 
        {
            if (skeletonName == _targetSkeleton?.Name)
                return;

            if (skeletonName == null)
            {
                _targetSkeleton = null;
            }
            else
            {
                var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
                _targetSkeleton = new SkeletonInfo(skeletonName, skeleton);
            }

            Invalidate();
        }

        public void UpdateSourceSkeleton(string? skeletonName) 
        {
            if (skeletonName == _sourceSkeleton?.Name)
                return;

            if (skeletonName == null)
            {
                _sourceSkeleton = null;
            }
            else
            {
                var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
                _sourceSkeleton = new SkeletonInfo(skeletonName, skeleton);
            }

            Invalidate();
        }

        void Invalidate()
        {
            // Every node is a TARGET bone: the remapper and the mapping window both index it that way.
            Bones = _targetSkeleton != null ? SkeletonBoneNodeHelper.Build(_targetSkeleton.Data) : [];
            FlatBoneList.Clear();
            FlatBoneList.Add(null);
            foreach (var bone in _targetSkeleton?.Data.Bones ?? [])
                FlatBoneList.Add(SkeletonBoneNodeHelper.GetNodeFromId(bone.Id, Bones));
            SelectedBone = Bones.FirstOrDefault();
        }

        /// <summary>Raised after a mapping is applied to the tree, so the editor can show the result without another click.</summary>
        public event Action? MappingApplied;

        public void ApplyDefaultMapping()
        {
            var config = CreateMappingConfig();
            BoneMappingHelper.AutomapDirectBoneLinksBasedOnNames(config.MeshBones.First(), config.ParentModelBones);
            Apply(config);
        }

        void Apply(RemappedAnimatedBoneConfiguration config)
        {
            SkeletonBoneNodeHelper.ApplyMapping(Bones, config);
            MappingApplied?.Invoke();
        }


        [RelayCommand] void ShowBoneMappingWindow()
        {
            if (_targetSkeleton == null || _sourceSkeleton == null)
            {
                _standardDialogs.ShowDialogBox("Source or target skeleton not selected", "Error");
                return;
            }

            var config = CreateMappingConfig();

            // Make this possible to use without being a modal!
            var handle = _boneMappingWindowFactory.Create();
            handle.ViewModel.Initialize(config);
            var result = handle.ShowDialog();

            if ((result.HasValue && result.Value == true) == false)
                return;

            Apply(config);
        }

        /// <summary>
        /// A new config each time, holding what the tree has applied. The window edits it in place, so a kept one brought the edits
        /// made before Cancel back on the next open, and the next OK applied them.
        /// </summary>
        RemappedAnimatedBoneConfiguration CreateMappingConfig()
        {
            var config = new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = _targetSkeleton!.Name,
                MeshBones = AnimatedBoneHelper.CreateFromSkeleton(_targetSkeleton.Data),

                ParnetModelSkeletonName = _sourceSkeleton!.Name,
                ParentModelBones = AnimatedBoneHelper.CreateFromSkeleton(_sourceSkeleton.Data),

                SkeletonBoneHighlighter = _skeletonBoneHighlighter
            };

            foreach (var bone in FlatBoneList)
            {
                if (bone?.HasMapping != true)
                    continue;
                var meshBone = SkeletonBoneNodeHelper.GetNodeFromId(bone.BoneIndex, config.MeshBones);
                if (meshBone == null)
                    continue;
                meshBone.MappedBoneIndex.Value = bone.MappedIndex;
                meshBone.MappedBoneName.Value = _sourceSkeleton.Data.Bones.FirstOrDefault(x => x.Id == bone.MappedIndex)?.Name;
            }
            return config;
        }

        /// <summary>Every bone of the tree as a preset keeps it: by name, with the source bone that drives it by name.</summary>
        public List<RetargetPresetBone> PresetBones() => [.. FlatBoneList.OfType<SkeletonBoneNode_new>().Select(b => new RetargetPresetBone(
            b.BoneName,
            b.HasMapping ? _sourceSkeleton?.Data.Bones.FirstOrDefault(x => x.Id == b.MappedIndex)?.Name : null,
            b.BoneLengthMult,
            [b.RotationOffset.X.Value, b.RotationOffset.Y.Value, b.RotationOffset.Z.Value],
            [b.TranslationOffset.X.Value, b.TranslationOffset.Y.Value, b.TranslationOffset.Z.Value],
            b.IsLocalOffset, b.ForceSnapToWorld, b.FreezeTranslation, b.FreezeRotation, b.FreezeRotationZ, b.ApplyTranslation, b.ApplyRotation,
            b.SelectedRelativeBone?.BoneName))];

        /// <summary>
        /// Puts a preset's bones on the tree by name, and returns the names it could not place: a target bone, a source bone or a
        /// relative bone that is not on these skeletons. Those are skipped, and bones the preset does not name are left alone.
        /// </summary>
        public List<string> ApplyPresetBones(IEnumerable<RetargetPresetBone> bones)
        {
            var missing = new List<string>();
            foreach (var p in bones)
            {
                var node = SkeletonBoneNodeHelper.GetNodeFromName(p.Name, Bones);
                if (node == null)
                {
                    missing.Add(p.Name);
                    continue;
                }

                var source = p.MappedFrom == null ? null : _sourceSkeleton?.Data.Bones.FirstOrDefault(x => x.Name == p.MappedFrom);
                if (p.MappedFrom != null && source == null)
                    missing.Add($"{p.MappedFrom} (source, for {p.Name})");
                node.MappedIndex = source?.Id ?? -1;
                node.HasMapping = source != null;
                node.MappedFrom = source?.Name ?? "";

                node.BoneLengthMult = p.BoneLengthMult;
                node.RotationOffset = new(p.RotationOffset[0], p.RotationOffset[1], p.RotationOffset[2]);
                node.TranslationOffset = new(p.TranslationOffset[0], p.TranslationOffset[1], p.TranslationOffset[2]);
                node.IsLocalOffset = p.IsLocalOffset;
                node.ForceSnapToWorld = p.ForceSnapToWorld;
                node.FreezeTranslation = p.FreezeTranslation;
                node.FreezeRotation = p.FreezeRotation;
                node.FreezeRotationZ = p.FreezeRotationZ;
                node.ApplyTranslation = p.ApplyTranslation;
                node.ApplyRotation = p.ApplyRotation;

                var relative = p.RelativeTo == null ? null : SkeletonBoneNodeHelper.GetNodeFromName(p.RelativeTo, Bones);
                if (p.RelativeTo != null && relative == null)
                    missing.Add($"{p.RelativeTo} (relative bone, for {p.Name})");
                node.SelectedRelativeBone = relative;
            }
            MappingApplied?.Invoke();
            return missing;
        }

        public ObservableCollection<SkeletonBoneNode_new?> FlatBoneList { get; } = [];

        /// <summary>Every bone of the tree, in target skeleton order, without the picker's "none".</summary>
        public List<SkeletonBoneNode_new> FlatBones => [.. FlatBoneList.OfType<SkeletonBoneNode_new>()];

        [RelayCommand] void ClearRelativeBone()
        {
            if (SelectedBone != null)
                SelectedBone.SelectedRelativeBone = null;
        }

        [RelayCommand] void ResetSelectedBone() => SelectedBone?.ResetSettings();


    }
}
