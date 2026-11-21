using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.Common;

namespace Editors.Shared.Core.Common.ReferenceModel
{
    public partial class BinAnimationViewModel : ObservableObject
    {
        private readonly SceneObject _sceneObject;
        private readonly SceneObjectEditor _sceneObjectEditor;
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IUiCommandFactory _uiCommandFactory;

        [ObservableProperty] FilterCollection<IAnimationBinGenericFormat> _fragmentList;
        [ObservableProperty] FilterCollection<AnimationBinEntryGenericFormat> _fragmentSlotList;
        [ObservableProperty] FilterCollection<AnimationReference> _animationList;
        [ObservableProperty] string? _metaDataName;
        [ObservableProperty] string? _metaDataPersistName;
        [ObservableProperty] string? _soundMetaName;
        [ObservableProperty] string? _soundMetaPersistName;
        [ObservableProperty] string? _animationFileName;

        /// <summary>Whether what plays came from a slot. "Any animation" plays with no slot, even while a slot is still picked.</summary>
        public bool PlayingSlot { get; private set; }

        public BinAnimationViewModel(SceneObjectEditor sceneObjectEditor, IPackFileService pfs, ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper, SceneObject sceneObject, IUiCommandFactory uiCommandFactory)
        {
            _sceneObjectEditor = sceneObjectEditor;
            _pfs = pfs;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _sceneObject = sceneObject;
            _uiCommandFactory = uiCommandFactory;

            FragmentList = new FilterCollection<IAnimationBinGenericFormat>(null, (value) => OnFragmentSelected(value, FragmentSlotList, _sceneObject.SkeletonName.Value))
            {
                SearchFilter = (value, rx) => { return rx.Match(value.FullPath).Success; }
            };
            FragmentSlotList = new FilterCollection<AnimationBinEntryGenericFormat>(null, PlaySlot)
            {
                SearchFilter = (value, rx) => { return rx.Match(value.SlotName).Success; }
            };
            AnimationList = new FilterCollection<AnimationReference>(null, PlayAnimation)
            {
                IsPlainTextFilter = true,
                SearchFilter = (value, rx) => rx.Match(value.AnimationFile).Success
            };

            OnSkeletonChange(_sceneObject.Skeleton);
            _sceneObject.SkeletonChanged += OnSkeletonChange;
        }

        public void PreviewSelectedSlot()
        {
            if (FragmentList.SelectedItem != null)
                _uiCommandFactory.Create<OpenEditorCommand>().ExecuteAsWindow(FragmentList.SelectedItem.PackFileReference?.FileName ?? FragmentList.SelectedItem.FullPath, 800, 900);
        }


        private void OnSkeletonChange(GameSkeleton newValue)
        {
            FragmentList.SelectedItem = null;
            FragmentSlotList.SelectedItem = null;
            AnimationList.SelectedItem = null;

            if (newValue == null)
            {
                FragmentList.UpdatePossibleValues(null);
                FragmentSlotList.UpdatePossibleValues(null);
                AnimationList.UpdatePossibleValues(null);
                return;
            }

            var skeletonName = Path.GetFileNameWithoutExtension(_sceneObject.SkeletonName.Value);
            var allPossibleFragments = LoadFragmentsForSkeleton(skeletonName);
            FragmentList.UpdatePossibleValues(allPossibleFragments);
            AnimationList.UpdatePossibleValues(_skeletonAnimationLookUpHelper.CopyAnimationsForSkeleton(skeletonName).OrderBy(x => x.AnimationFile));
        }

        List<IAnimationBinGenericFormat> LoadFragmentsForSkeleton(string skeletonName) => AnimationSets.ForSkeleton(_pfs, skeletonName);

        void OnFragmentSelected(IAnimationBinGenericFormat value, FilterCollection<AnimationBinEntryGenericFormat> animationSlotsCollection, string skeletonName)
        {
            if (value == null)
            {
                animationSlotsCollection.UpdatePossibleValues(null);
                return;
            }

            var newSkeletonName = value.SkeletonName;
            var existingSkeletonName = Path.GetFileNameWithoutExtension(skeletonName);
            if (newSkeletonName != existingSkeletonName)
            {
                MessageBox.Show("This fragment does not fit the current skeleton");
                return;
            }

            animationSlotsCollection.UpdatePossibleValues(value.Entries);
        }

        /// <summary>Plays any animation made for the model's skeleton, with the .anm.meta beside it when there is one, without picking
        /// a set and a slot first (upstream #263). A set that is picked still gives it its persistent meta.</summary>
        public void PlayAnimation(AnimationReference? animation)
        {
            if (animation == null)
                return;
            var meta = Path.ChangeExtension(animation.AnimationFile, ".anm.meta");
            PlaySlot(new AnimationBinEntryGenericFormat { SlotName = "", AnimationFile = animation.AnimationFile, MetaFile = _pfs.FindFile(meta) != null ? meta : "" });
        }

        /// <summary>Plays a slot's animation and metas on the scene object. Picking a slot calls it; SuperView's Preview calls it with
        /// a copy of the picked slot naming other files, which plays them without writing the set.</summary>
        public void PlaySlot(AnimationBinEntryGenericFormat? value)
        {
            PlayingSlot = !string.IsNullOrEmpty(value?.SlotName);
            AnimationReference? animationReference = null;
            PackFile? meta = null;
            PackFile? persistMeta = null;
            string? persistFullpath = null;

            if (string.IsNullOrWhiteSpace(value?.AnimationFile) == false)
            {
                // A table can name an .anim no loaded pack has (a mod's, not loaded); the part then plays nothing.
                var file = _pfs.FindFile(value.AnimationFile);
                if (file != null)
                    animationReference = _skeletonAnimationLookUpHelper.FindAnimationRefFromPackFile(file);
            }

            if (string.IsNullOrWhiteSpace(value?.MetaFile) == false)
                meta = _pfs.FindFile(value.MetaFile);

            AnimationBinEntryGenericFormat? persistRow = null;
            var persist = FragmentSlotList.PossibleValues.FirstOrDefault(x => x.SlotName == "PERSISTENT_METADATA_ALIVE");
            if (persist != null && string.IsNullOrWhiteSpace(persist.MetaFile) == false)
            {
                persistMeta = _pfs.FindFile(persist.MetaFile);
                persistFullpath = persist.MetaFile;
                persistRow = persist;
            }

            if (persistMeta == null)
            {
                var persistFlying = FragmentSlotList.PossibleValues.FirstOrDefault(x => x.SlotName == "PERSISTENT_METADATA_FLYING");
                if (persistFlying != null && string.IsNullOrWhiteSpace(persistFlying.MetaFile) == false)
                { 
                    persistMeta = _pfs.FindFile(persistFlying.MetaFile);
                    persistFullpath = persistFlying.MetaFile;
                    persistRow = persistFlying;
                }
            }

            // The slot's .snd.meta, and the one on the row the persistent meta came from. Set before SetMetaFile, so its one update
            // carries them, and with their names, so a file in no loaded pack can be named.
            SoundMetaName = value?.SoundFile;
            SoundMetaPersistName = persistRow?.SoundFile;
            _sceneObject.SoundMetaData = string.IsNullOrWhiteSpace(SoundMetaName) ? null : _pfs.FindFile(SoundMetaName);
            _sceneObject.PersistSoundMetaData = string.IsNullOrWhiteSpace(SoundMetaPersistName) ? null : _pfs.FindFile(SoundMetaPersistName);

            // The slot shows only the weapon bones it flags (bit N, be_prop_N), as in game: STAND_NO_WEAPON none, a Doomseeker's
            // STAND_IDLE_1 his left flail, the right one replaced by the flail his animated prop swings.
            var weaponBones = value?.WeaponBools ?? 0b111111;
            if (_sceneObject.ModelNode != null)
            {
                foreach (var weapon in SceneNodeHelper.GetChildrenOfType<SlotNode>(_sceneObject.ModelNode))
                {
                    if (weapon.AttachmentBoneName?.StartsWith("be_prop_") == true && int.TryParse(weapon.AttachmentBoneName["be_prop_".Length..], out var bone))
                        weapon.IsVisible = (weaponBones >> bone & 1) == 1;
                }
            }

            _sceneObjectEditor.SetAnimation(_sceneObject, animationReference?.AnimationFile);
            _sceneObjectEditor.SetMetaFile(_sceneObject, meta, persistMeta);

            MetaDataName = value?.MetaFile;
            MetaDataPersistName = persistFullpath;
            if (animationReference != null)
                AnimationFileName = "[" + animationReference?.Container.Name + "] " + animationReference?.AnimationFile;
            else
                AnimationFileName = null;
        }
    }
}
