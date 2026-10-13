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
        const string CampaignSetFolder = @"animations\campaign\database\bin\";

        private readonly ILogger _logger = Logging.Create<BinAnimationViewModel>();
        private readonly SceneObject _sceneObject;
        private readonly SceneObjectEditor _sceneObjectEditor;
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IUiCommandFactory _uiCommandFactory;

        [ObservableProperty] FilterCollection<IAnimationBinGenericFormat> _fragmentList;
        [ObservableProperty] FilterCollection<AnimationBinEntryGenericFormat> _fragmentSlotList;
        [ObservableProperty] string? _metaDataName;
        [ObservableProperty] string? _metaDataPersistName;
        [ObservableProperty] string? _animationFileName;

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
            FragmentSlotList = new FilterCollection<AnimationBinEntryGenericFormat>(null, OnAnimationSlotSelected)
            {
                SearchFilter = (value, rx) => { return rx.Match(value.SlotName).Success; }
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

            if (newValue == null)
            {
                FragmentList.UpdatePossibleValues(null);
                FragmentSlotList.UpdatePossibleValues(null);
                return;
            }

            var skeletonName = Path.GetFileNameWithoutExtension(_sceneObject.SkeletonName.Value);
            var allPossibleFragments = LoadFragmentsForSkeleton(skeletonName);
            FragmentList.UpdatePossibleValues(allPossibleFragments);
        }

        List<IAnimationBinGenericFormat> LoadFragmentsForSkeleton(string skeletonName, bool onlyPacksThatCanBeSaved = false)
        {
            var outputFragments = new List<IAnimationBinGenericFormat>();
            var animPacks = PackFileServiceUtility.GetAllAnimPacks(_pfs);
            foreach (var animPack in animPacks)
            {
                if (onlyPacksThatCanBeSaved == true)
                {
                    if (_pfs.GetPackFileContainer(animPack).IsCaPackFile)
                        continue;
                }

                var animPackFile = AnimationPackSerializer.Load(animPack, _pfs);
                var fragments = animPackFile.GetGenericAnimationSets(skeletonName);
                foreach (var fragment in fragments)
                    outputFragments.Add(fragment);
            }

            // A campaign set is a loose file, not in an animpack, and the only set some skeletons have (Glottkin's). Each path
            // once, the copy the game would load.
            // ponytail: reads every set's string table on each skeleton change, 0.2 s for vanilla's 761; cache by file if it shows.
            var campaignSets = PackFileServiceUtility.FindAllWithExtentionIncludePaths(_pfs, ".bin")
                .Select(x => PathNormalization.NormalizeFileName(x.FileName))
                .Where(x => x.StartsWith(CampaignSetFolder))
                .Distinct();
            foreach (var path in campaignSets)
            {
                var file = _pfs.FindFile(path);
                if (file == null || onlyPacksThatCanBeSaved && _pfs.GetPackFileContainer(file).IsCaPackFile)
                    continue;
                try
                {
                    if (CampaignAnimationBinLoader.ReadSkeletonName(file.DataSource.ReadDataAsChunk()) == skeletonName)
                        outputFragments.Add(new CampaignAnimationSet(path.Replace('\\', '/'), CampaignAnimationBinLoader.Load(file.DataSource.ReadDataAsChunk())));
                }
                catch (Exception e)
                {
                    _logger.Here().Warning($"Campaign animation set {path} could not be read, so it is not listed: {e.Message}");
                }
            }
            return outputFragments;
        }

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

        void OnAnimationSlotSelected(AnimationBinEntryGenericFormat value)
        {
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

            var persist = FragmentSlotList.PossibleValues.FirstOrDefault(x => x.SlotName == "PERSISTENT_METADATA_ALIVE");
            if (persist != null && string.IsNullOrWhiteSpace(persist.MetaFile) == false)
            {
                persistMeta = _pfs.FindFile(persist.MetaFile);
                persistFullpath = persist.MetaFile;
            }

            if (persistMeta == null)
            {
                var persistFlying = FragmentSlotList.PossibleValues.FirstOrDefault(x => x.SlotName == "PERSISTENT_METADATA_FLYING");
                if (persistFlying != null && string.IsNullOrWhiteSpace(persistFlying.MetaFile) == false)
                { 
                    persistMeta = _pfs.FindFile(persistFlying.MetaFile);
                    persistFullpath = persistFlying.MetaFile;
                }
            }

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
