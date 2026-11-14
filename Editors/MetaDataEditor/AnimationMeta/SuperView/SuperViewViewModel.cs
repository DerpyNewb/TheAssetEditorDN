using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.MetaEditor.Check;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SuperViewViewModel : EditorHostBase, ISaveableEditor, IFileEditor
    {
        SceneObjectViewModel _asset;

        private readonly SceneObjectEditor _sceneObjectBuilder;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataBuilder _metaDataFactory;
        private readonly IPackFileService _packFileService;
        private readonly IEventHub _eventHub;
        private readonly IGlobalEventHub _globalEventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly IFileSaveService _fileSaveService;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;

        [ObservableProperty] string _persistentMetaFilePath = "";
        [ObservableProperty] string _metaFilePath = "";
        [ObservableProperty] MetaDataEditorViewModel _persistentMetaEditor;
        [ObservableProperty] MetaDataEditorViewModel _metaEditor;
        [ObservableProperty] int _selectedTabControllerIndex = 0;
        [ObservableProperty] bool _showVfx = true;

        // The battle entity's vfx_filter_id, offered from the ids the metas' EFFECT tags carry: a tag whose id is neither 0 nor
        // this does not play on that entity. Picked, it holds across animations; until then it is the lowest id the metas carry.
        [ObservableProperty] List<int> _vfxFilterIds = [];
        [ObservableProperty] int? _vfxFilterId;
        bool _showingVfxFilter;
        public override Type EditorViewModelType => typeof(EditorView);
        public override global::Shared.Core.ToolCreation.EditorEnums LayoutKey => global::Shared.Core.ToolCreation.EditorEnums.SuperView_Editor;
        public bool HasUnsavedChanges
        { 
            get 
            {
                return PersistentMetaEditor.HasUnsavedChanges || MetaEditor.HasUnsavedChanges;
            }
            set 
            {
                PersistentMetaEditor.HasUnsavedChanges = value;
                MetaEditor.HasUnsavedChanges = value;
            }
        }

        // The tab is red while either editor has unsaved changes, so it hears when either's change.
        partial void OnPersistentMetaEditorChanged(MetaDataEditorViewModel? oldValue, MetaDataEditorViewModel newValue) => Follow(oldValue, newValue);
        partial void OnMetaEditorChanged(MetaDataEditorViewModel? oldValue, MetaDataEditorViewModel newValue) => Follow(oldValue, newValue);

        void Follow(MetaDataEditorViewModel? oldValue, MetaDataEditorViewModel newValue)
        {
            if (oldValue != null)
                oldValue.PropertyChanged -= OnEditorPropertyChanged;
            newValue.PropertyChanged += OnEditorPropertyChanged;
        }

        void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HasUnsavedChanges))
                OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        // A screenshot is named after the file.
        protected override string ScreenshotName => Path.GetFileNameWithoutExtension(CurrentFile?.Name ?? "superview");


        public SuperViewViewModel(
            IPackFileService packFileService,
            IEventHub eventHub,
            IGlobalEventHub globalEventHub,
            IUiCommandFactory uiCommandFactory,
            SceneObjectEditor sceneObjectBuilder,
            IEditorHostParameters editorHostParameters,
            MetaDataFileParser metaDataFileParser,
            IMetaDataBuilder metaDataFactory,
            IStandardDialogs standardDialogs,
            IFileSaveService fileSaveService,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper)
            : base(editorHostParameters)
        {
            DisplayName = "Super view";
            _standardDialogs = standardDialogs;
            _fileSaveService = fileSaveService;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _packFileService = packFileService;
            _eventHub = eventHub;
            _globalEventHub = globalEventHub;
            _uiCommandFactory = uiCommandFactory;
            _sceneObjectBuilder = sceneObjectBuilder;
            _metaDataFileParser = metaDataFileParser;
            _metaDataFactory = metaDataFactory;
            Initialize();
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);
            eventHub.Register<MetaDataAttributeChangedEvent>(this, OnMetaDataAttributeChanged);
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, OnSelectedMetaDataAttributeChanged);
            globalEventHub.Register<PackFileContainerFilesAddedEvent>(this, x => OnPackFilesChanged(x.Container, x.AddedFiles));
            globalEventHub.Register<PackFileContainerFilesUpdatedEvent>(this, x => OnPackFilesChanged(x.Container, x.ChangedFiles));
        }

        // A vfx file, a texture, or a model, material or animation a rigid particle may draw changed in a pack (the VFX editor
        // saved, a file was replaced): forget what was loaded from it and rebuild, so the scene shows it without the tab being
        // reopened.
        static readonly string[] s_reloaded = [".dds", ".rigid_model_v2", ".wsmodel", ".material", ".anim"];

        void OnPackFilesChanged(IPackFileContainer container, List<PackFile> files)
        {
            var paths = files.Select(x => _packFileService.GetFullPath(x, container))
                .Where(x => x.StartsWith("vfx", StringComparison.OrdinalIgnoreCase) || s_reloaded.Any(e => x.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (paths.Count == 0)
                return;
            _metaDataFactory.Forget(paths);
            RecreateMetaDataInformation();
        }

        private void OnSelectedMetaDataAttributeChanged(SelecteMetaDataAttributeChangedEvent @event) => RecreateMetaDataInformation();
        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event) => RecreateMetaDataInformation();
        void OnMetaDataChanged(SceneObject sceneObject) => RecreateMetaDataInformation();

        partial void OnShowVfxChanged(bool value)
        {
            _metaDataFactory.ShowVfx = value;
            RecreateMetaDataInformation();
        }

        partial void OnVfxFilterIdChanged(int? value)
        {
            if (_showingVfxFilter || value == null || value == _metaDataFactory.VfxFilterId)
                return;
            _metaDataFactory.VfxFilterId = value;
            RecreateMetaDataInformation();
        }

        private void OnFileSaved(ScopedFileSavedEvent evnt)
        {
            var newFile = _packFileService.FindFile(evnt.NewPath);
            if (evnt.FileOwner == PersistentMetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, newFile);
            else if (evnt.FileOwner == MetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, newFile, _asset.Data.PersistMetaData);
            else
                throw new Exception($"Unable to determine file owner when reciving a file save event in SuperView. Owner:{evnt.FileOwner}, File:{evnt.NewPath}");
        }

        void Initialize()
        {
            PersistentMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            MetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            // Check meta checks against the playing clip and the skeleton. A persistent meta plays under every clip of the set,
            // so its tab gets no clip length to check start times against.
            MetaEditor.CheckScene = () => new MetaCheckScene(_asset.Data.Player.AnimationClip?.PlayTimeInSec, _asset.Data.Skeleton?.BoneNames);
            PersistentMetaEditor.CheckScene = () => new MetaCheckScene(null, _asset.Data.Skeleton?.BoneNames);
            
            var assetViewModel = _sceneObjectViewModelBuilder.CreateAsset("SuperViewRoot", true, "Root", Color.Black,null);
            SceneObjects.Add(assetViewModel);

            assetViewModel.Data.MetaDataChanged += OnMetaDataChanged;

            _asset = assetViewModel;
            _asset.FragAndSlotSelection.FragmentSlotList.SelectedItemChanged += LoadSlotFields;

            // The meta's props always play, so the model under them has to as well; off, it stands
            // in its bind pose (Rmv2MeshNode only skins while its player is enabled).
            Player.IsEnabled.Value = true;
            OnSceneObjectUpdated(new SceneObjectUpdateEvent(_asset.Data, false, false, false, true));
        }

        void RecreateMetaDataInformation()
        {
            // The builder keeps what an unchanged tag built, so only what it no longer returns is cleaned up.
            var old = SceneObjects.SelectMany(x => x.Data.MetaDataItems).ToList();
            foreach (var item in SceneObjects)
            {
                item.Data.MetaDataItems.Clear();
                item.Data.Player.AnimationRules.Clear();
            }

            var persist = PersistentMetaEditor.ParsedFile;
            var meta = MetaEditor.ParsedFile;

            _asset.Data.MetaDataItems = _metaDataFactory.Create(persist, meta, MetaEditor.SelectedAttribute, _asset.Data.MainNode, _asset.Data, _asset.Data.Player, _asset.FragAndSlotSelection.FragmentList.SelectedItem);
            foreach (var t in old.Except(_asset.Data.MetaDataItems))
                t.CleanUp();
            _asset.Data.Player.Refresh();

            var ids = MetaDataBuilder.VfxFilterIds(persist, meta);
            var filter = _metaDataFactory.VfxFilterId ?? ids.FirstOrDefault();
            _showingVfxFilter = true;
            VfxFilterIds = filter == 0 || ids.Contains(filter) ? ids : [.. ids.Append(filter).Order()];
            VfxFilterId = filter;
            _showingVfxFilter = false;
        }

        private void OnSceneObjectUpdated(SceneObjectUpdateEvent e)
        {
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);

            RecreateMetaDataInformation();
        }

        // Opened on a .variantmeshdefinition from the pack browser: the file is the mesh, and the
        // animation set and slot are then picked in the scene object panel as usual.
        public PackFile? CurrentFile { get; private set; }

        public void LoadFile(PackFile file)
        {
            CurrentFile = file;
            DisplayName = $"Super view - {file.Name}";
            _sceneObjectBuilder.SetMesh(_asset.Data, file);
        }

        public void Load(AnimationToolInput debugDataToLoad)
        {
            _sceneObjectBuilder.SetMesh(_asset.Data, debugDataToLoad.Mesh);

            // Hack :(
            if (debugDataToLoad.AnimationSlot != null)
            {
                var frag = _asset.FragAndSlotSelection.FragmentList.PossibleValues.FirstOrDefault(x => x.FullPath == debugDataToLoad.FragmentName);
                _asset.FragAndSlotSelection.FragmentList.SelectedItem = frag;

                var slot = _asset.FragAndSlotSelection.FragmentSlotList.PossibleValues.First(x => x.SlotName == debugDataToLoad.AnimationSlot.Value);
                _asset.FragAndSlotSelection.FragmentSlotList.SelectedItem = slot;
            }
        }


        public void RefreshAction() => _asset.Data.TriggerMeshChanged();

        public override void Close()
        {
            _eventHub?.UnRegister(this);
            _globalEventHub.UnRegister(this);
            base.Close();
        }

        public bool Save()
        {
            var res0 = PersistentMetaEditor.Save();
            var res1 = MetaEditor.Save();
            return res0 && res1;
        }

        // The slot panel: the picked slot's three files, typed or browsed, and Apply, which saves them into its set.
        [ObservableProperty] string _slotAnim = "";
        [ObservableProperty] string _slotMeta = "";
        [ObservableProperty] string _slotSound = "";

        void LoadSlotFields(AnimationBinEntryGenericFormat? slot) => (SlotAnim, SlotMeta, SlotSound) = (slot?.AnimationFile ?? "", slot?.MetaFile ?? "", slot?.SoundFile ?? "");

        [RelayCommand]
        void ApplySlotFiles()
        {
            var problem = _asset.FragAndSlotSelection.FragmentSlotList.SelectedItem is { } slot ? AssignFiles(slot, SlotAnim, SlotMeta, SlotSound)
                : "Pick an animation set and a slot first.";
            if (problem != null)
                _standardDialogs.ShowDialogBox(problem);
        }

        // An animation offered only if it is for the set's skeleton, as one for another skeleton cannot play on it.
        [RelayCommand]
        void BrowseSlotAnim()
        {
            var skeleton = _asset.FragAndSlotSelection.FragmentList.SelectedItem?.SkeletonName;
            var anims = skeleton == null ? null : _skeletonAnimationLookUpHelper.GetAnimationsForSkeleton(skeleton).Select(x => Key(x.AnimationFile)).ToHashSet();
            Browse(".anim", anims == null ? null : x => anims.Contains(Key(x)), x => SlotAnim = x);
        }

        [RelayCommand] void BrowseSlotMeta() => Browse(".anm.meta", null, x => SlotMeta = x);
        [RelayCommand] void BrowseSlotSound() => Browse(".snd.meta", null, x => SlotSound = x);

        static string Key(string path) => path.Replace('\\', '/').ToLowerInvariant();

        void Browse(string extension, Func<string, bool>? filter, Action<string> picked)
        {
            var pick = _standardDialogs.DisplayBrowseDialog([extension], filter);
            if (pick.Result && pick.File != null)
                picked(Key(_packFileService.GetFullPath(pick.File)));
        }

        // Save writes a meta back over the file it was read from, which every set naming that file shares. This writes the open
        // tab's meta to a new file instead and points the slot that tab shows at it.
        [RelayCommand]
        void SaveMetaAs()
        {
            var persistent = SelectedTabControllerIndex == 0;
            var editor = persistent ? PersistentMetaEditor : MetaEditor;
            var selection = _asset.FragAndSlotSelection;
            var slot = persistent
                ? selection.FragmentSlotList.PossibleValues.FirstOrDefault(x => x.SlotName.StartsWith("PERSISTENT_METADATA_") && x.MetaFile == selection.MetaDataPersistName)
                : selection.FragmentSlotList.SelectedItem;
            var problem = slot == null || editor.ParsedFile == null ? "This tab has no meta file open to save."
                : editor.Tags.Select(x => x.HasError()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (problem == null)
            {
                var pick = _standardDialogs.DisplaySaveDialog(_packFileService, [".anm.meta"]);
                if (!pick.Result || string.IsNullOrWhiteSpace(pick.SelectedFilePath))
                    return;
                var path = pick.SelectedFilePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? pick.SelectedFilePath : pick.SelectedFilePath + ".anm.meta";
                problem = AssignFiles(slot!, slot!.AnimationFile, path, slot.SoundFile, _metaDataFileParser.GenerateBytes(editor.MetaDataFileVersion, editor.ParsedFile));
            }
            if (problem != null)
                _standardDialogs.ShowDialogBox(problem);
        }

        // Points a slot at other files and saves its set into the editable pack, then writes metaBytes, when given, at the meta's
        // path. A battle table is spliced into its animpack; a campaign set is a file of its own and is written whole. Either is
        // first read again from the editable pack, so a set picked before an earlier save does not undo it. Null when saved, else
        // why not, and nothing is written then. Paths are stored lower-cased with forward slashes, as CA's rows have them.
        string? AssignFiles(AnimationBinEntryGenericFormat slot, string anim, string meta, string sound, byte[]? metaBytes = null)
        {
            (anim, meta, sound) = (Key(anim.Trim()), Key(meta.Trim()), Key(sound.Trim()));
            var selection = _asset.FragAndSlotSelection;
            var shown = selection.FragmentList.SelectedItem;
            var editable = _packFileService.GetEditablePack();
            if (editable == null)
                return "No editable pack is selected.";

            IAnimationBinGenericFormat saved;
            try
            {
                if (shown is AnimationBinWh3 table)
                {
                    table = AnimTableEdits.Latest(_packFileService, table);
                    var before = table.ToByteArray();
                    AnimTableEdits.SetFiles(table, slot, anim, meta, sound);
                    if (AnimTableEdits.Save(_packFileService, _fileSaveService, table) is { } refused)
                    {
                        table.AnimationTableEntries = new AnimationBinWh3(table.FileName, before).AnimationTableEntries;
                        return refused;
                    }
                    saved = AnimTableEdits.Latest(_packFileService, table);
                }
                else if (shown is CampaignAnimationSet campaign)
                {
                    CampaignAnimationSet Read() => new(campaign.FullPath, CampaignAnimationBinLoader.Load(
                        (_packFileService.FindFile(campaign.FullPath, editable) ?? _packFileService.FindFile(campaign.FullPath))!.DataSource.ReadDataAsChunk()));
                    var set = Read();
                    set.SetFiles(slot, anim, meta, sound);
                    _fileSaveService.Save(campaign.FullPath, CampaignAnimationBinLoader.Write(set.Bin, Path.GetFileNameWithoutExtension(campaign.FullPath)), false);
                    // Read back as written: the writer lower-cases every string, so a set with capitals is not what it wrote, and a
                    // slot picked from it would name files the file no longer does.
                    saved = Read();
                }
                else
                    return "Pick an animation set first.";
            }
            catch (ArgumentException e)
            {
                return e.Message;
            }
            if (metaBytes != null)
                _fileSaveService.Save(meta, metaBytes, false);

            // The slot list is built from a set when it is picked, so the saved copy is picked in the shown one's place, then the slot
            // at the same place in the list: an edit changes a row's files, never the rows. The edited tab loads its new file; the
            // other keeps any unsaved edit, as its file is the same one.
            var slots = selection.FragmentSlotList;
            var at = slots.PossibleValues.IndexOf(slots.SelectedItem!);
            var name = slots.SelectedItem?.SlotName;
            selection.FragmentList.UpdatePossibleValues(selection.FragmentList.PossibleValues.Select(x => x == shown ? saved : x).ToList());
            selection.FragmentList.SelectedItem = saved;
            slots.SelectedItem = at >= 0 && at < slots.PossibleValues.Count && slots.PossibleValues[at].SlotName == name ? slots.PossibleValues[at]
                : slots.PossibleValues.FirstOrDefault(x => x.SlotName == name);
            return null;
        }
    }
}
