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

        public ObservableCollection<SkeletonBoneNode_new?> FlatBoneList { get; } = [];
        [RelayCommand] void ClearRelativeBone()
        {
            if (SelectedBone != null)
                SelectedBone.SelectedRelativeBone = null;
        }

        [RelayCommand] void ResetSelectedBone() => SelectedBone?.ResetSettings();


    }
}
