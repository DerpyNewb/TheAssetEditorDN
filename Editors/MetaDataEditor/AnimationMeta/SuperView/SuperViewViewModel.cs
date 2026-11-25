using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.MetaEditor;
using Editors.AnimationMeta.MetaEditor.Check;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Problems;
using Editors.AnimationMeta.SuperView.Timeline;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Audio.Shared.Storage;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Common.Timeline;

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
        private readonly ApplicationSettingsService _settings;

        // Where each tab's file and the slot panel's typed meta come from: "From vanilla: <path>", "From your pack: <path>", or
        // another mod's pack name. A vanilla or other mod's meta saved at its own path overrides it for every bin that names it.
        [ObservableProperty] string _persistentMetaSource = "";
        [ObservableProperty] string _metaSource = "";
        [ObservableProperty] string _soundMetaSource = "";
        [ObservableProperty] string _soundMetaChangedOnDisk = "";
        [ObservableProperty] string _slotMetaSource = "";
        [ObservableProperty] bool _slotHasNoMeta;
        private readonly LocalizationManager _localizationManager;
        private readonly CombatMetaDataEditSession _editSession;
        private readonly CombatMetaDataGizmoComponent _gizmo;
        private readonly MetaDataMarkerPickerComponent _markerPicker;

        // Which of a splash attack's points the gizmo is on: the one picked in the viewport, the start when picked in the list.
        CombatMetaDataPoint _splashPoint = CombatMetaDataPoint.SplashStart;
        bool _selectingMarker;

        [ObservableProperty] MetaDataEditorViewModel _persistentMetaEditor;
        [ObservableProperty] MetaDataEditorViewModel _metaEditor;
        /// <summary>The played slot's .snd.meta, the third tab. The persistent row's is loaded for sound but has no tab.</summary>
        [ObservableProperty] MetaDataEditorViewModel _soundMetaEditor;
        [ObservableProperty] int _selectedTabControllerIndex = 0;
        [ObservableProperty] bool _showVfx = true;

        // The battle entity's vfx_filter_id, offered from the ids the metas' EFFECT tags carry: a tag whose id is neither 0 nor
        // this does not play on that entity. Picked, it holds across animations; until then it is the lowest id the metas carry.
        [ObservableProperty] List<int> _vfxFilterIds = [];
        [ObservableProperty] int? _vfxFilterId;
        bool _showingVfxFilter;

        // What the game supplies while the unit plays, for the preview only; none of it is written into the META. As a unit far
        // from the camera, EFFECT tags marked Distance Culled are not drawn. The aura and weapon vfx are a spell's wind-up
        // (special_ability_displays wind_up_aura_vfx / wind_up_weapon_vfx: on the caster's body, and in the hand or staff), which
        // the game plays at the placeholder EFFECT tags of type ability_aura (1) and ability_weapon (2) in cast animations; empty,
        // those tags play the vfx they name.
        [ObservableProperty] bool _hideDistanceCulledEffects;
        [ObservableProperty] string _auraVfx = "";
        [ObservableProperty] string _weaponVfx = "";

        // The 3D view's tool: select only (no gizmo), move, or rotate the selected tag.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSelectTool), nameof(IsMoveTool), nameof(IsRotateTool))]
        MetaDataEditTool _editTool = MetaDataEditTool.Select;
        public bool IsSelectTool { get => EditTool == MetaDataEditTool.Select; set { if (value) EditTool = MetaDataEditTool.Select; } }
        public bool IsMoveTool { get => EditTool == MetaDataEditTool.Move; set { if (value) EditTool = MetaDataEditTool.Move; } }
        public bool IsRotateTool { get => EditTool == MetaDataEditTool.Rotate; set { if (value) EditTool = MetaDataEditTool.Rotate; } }
        public bool CanEditSelectedIn3D => _editSession.CurrentSource != null;
        public bool CanRotateSelectedIn3D => _editSession.CanRotate;
        // The toolbar's undo and redo are the open tab's own, the history its typed edits share with the view's moves and turns.
        // The edit session keeps a history of its own as well, which nothing here reads.
        public bool CanUndoEditIn3D => CurrentEditor?.UndoCommand.CanExecute(null) == true;
        public bool CanRedoEditIn3D => CurrentEditor?.RedoCommand.CanExecute(null) == true;

        // What is wrong in the three META files, for the list under them; picking one opens its tag.
        public ObservableCollection<MetaDataProblem> Problems { get; } = [];
        [ObservableProperty] MetaDataProblem? _selectedProblem;
        public bool HasProblems => Problems.Count > 0;
        public string ProblemsHeader => string.Format(_localizationManager.Get("SuperView.Problems.Header"), Problems.Count);
        bool _showingProblems;

        // The META files the selected slot names. One that is named but not in any loaded pack can be created empty; one
        // that is not named has nothing to show.
        public string AnimationMetaFilePath => _asset?.FragAndSlotSelection.MetaDataName ?? "";
        public string PersistentMetaFilePath => _asset?.FragAndSlotSelection.MetaDataPersistName ?? "";
        public bool CanCreateAnimationMetaFile => MetaEditor?.CurrentFile == null && !string.IsNullOrWhiteSpace(AnimationMetaFilePath);
        public bool CanCreatePersistentMetaFile => PersistentMetaEditor?.CurrentFile == null && !string.IsNullOrWhiteSpace(PersistentMetaFilePath);
        public bool IsAnimationMetaFileNotNamed => MetaEditor?.CurrentFile == null && string.IsNullOrWhiteSpace(AnimationMetaFilePath);
        public bool IsPersistentMetaFileNotNamed => PersistentMetaEditor?.CurrentFile == null && string.IsNullOrWhiteSpace(PersistentMetaFilePath);

        public override Type EditorViewModelType => typeof(EditorView);
        public override global::Shared.Core.ToolCreation.EditorEnums LayoutKey => global::Shared.Core.ToolCreation.EditorEnums.SuperView_Editor;
        public bool HasUnsavedChanges
        { 
            get 
            {
                return PersistentMetaEditor.HasUnsavedChanges || MetaEditor.HasUnsavedChanges || SoundMetaEditor.HasUnsavedChanges || SlotGrid?.HasUnsavedChanges == true;
            }
            set
            {
                PersistentMetaEditor.HasUnsavedChanges = value;
                MetaEditor.HasUnsavedChanges = value;
                SoundMetaEditor.HasUnsavedChanges = value;
                if (!value)
                    SlotGrid?.Discard();
            }
        }

        // The tab is red while any editor has unsaved changes, so it hears when any one's change.
        partial void OnPersistentMetaEditorChanged(MetaDataEditorViewModel? oldValue, MetaDataEditorViewModel newValue) => Follow(oldValue, newValue);
        partial void OnMetaEditorChanged(MetaDataEditorViewModel? oldValue, MetaDataEditorViewModel newValue) => Follow(oldValue, newValue);
        partial void OnSoundMetaEditorChanged(MetaDataEditorViewModel? oldValue, MetaDataEditorViewModel newValue) => Follow(oldValue, newValue);

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
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            ApplicationSettingsService settings,
            IAudioRepository audioRepository,
            LocalizationManager localizationManager,
            CombatMetaDataEditSession editSession,
            CombatMetaDataGizmoComponent gizmo,
            MetaDataMarkerPickerComponent markerPicker)
            : base(editorHostParameters)
        {
            DisplayName = "Super view";
            _settings = settings;
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
            _localizationManager = localizationManager;
            _editSession = editSession;
            _gizmo = gizmo;
            _markerPicker = markerPicker;

            // Added to this editor's world only: registered as game components, they would join every editor's.
            GameWorld!.AddComponent(_gizmo);
            GameWorld.AddComponent(_markerPicker);
            _markerPicker.Connect(GetMarkers, SelectMarker, FocusMarker, ClearMarkerSelection);
            _editSession.ValueChanged += OnEditValueChanged;
            _editSession.HistoryChanged += NotifyEditState;

            Initialize();
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);
            eventHub.Register<MetaDataAttributeChangedEvent>(this, OnMetaDataAttributeChanged);
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, OnSelectedMetaDataAttributeChanged);
            globalEventHub.Register<PackFileContainerFilesAddedEvent>(this, x => OnPackFilesChanged(x.Container, x.AddedFiles));
            globalEventHub.Register<PackFileContainerFilesUpdatedEvent>(this, x => OnPackFilesChanged(x.Container, x.ChangedFiles));
            InitSound(audioRepository);
        }

        // A vfx file, a texture, or a model, material or animation a rigid particle may draw changed in a pack (the VFX editor
        // saved, a file was replaced): forget what was loaded from it and rebuild, so the scene shows it without the tab being
        // reopened.
        static readonly string[] s_reloaded = [".dds", ".rigid_model_v2", ".wsmodel", ".material", ".anim"];

        void OnPackFilesChanged(IPackFileContainer container, List<PackFile> files)
        {
            var all = files.Select(x => _packFileService.GetFullPath(x, container)).ToList();
            if (_soundMetaPath.Length != 0 && all.Any(x => CustomMeta.Normalise(x) == _soundMetaPath))
                OnSoundMetaWritten();
            var paths = all
                .Where(x => x.StartsWith("vfx", StringComparison.OrdinalIgnoreCase) || s_reloaded.Any(e => x.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (paths.Count == 0)
                return;
            _metaDataFactory.Forget(paths);
            RecreateMetaDataInformation();
        }

        // The Sound tab's file, as its path was when the tab loaded it: a pack closed since then no longer knows the file's path.
        string _soundMetaPath = "";

        // The Sound tab's file was written, from outside SuperView or by a save of the tab's own. The file the tab holds, written with
        // what the tab already shows, is nothing new, so the tab keeps its undo history. Otherwise a tab with no edits shows the file as
        // written, and one with edits keeps them and says so; the tab's own save then clears that.
        void OnSoundMetaWritten()
        {
            var file = _packFileService.FindFile(_soundMetaPath);
            if (file != null && file == _asset.Data.SoundMetaData && Shown() is { } shown && file.DataSource.ReadData().AsSpan().SequenceEqual(shown))
                return;
            if (SoundMetaEditor.HasUnsavedChanges)
            {
                SoundMetaChangedOnDisk = "The .snd.meta changed on disk. Your unsaved edits are kept.";
                return;
            }
            // Written in place, it is the PackFile the tab holds, which LoadFile would not read again.
            SoundMetaEditor.CurrentFile = null!;
            ShowSoundMeta(file);
        }

        // What the Sound tab shows, as bytes; null when it shows nothing or has a tag that cannot be written, as the editor's own
        // undo snapshot treats it.
        byte[]? Shown()
        {
            if (SoundMetaEditor.ParsedFile is not { } shown)
                return null;
            try
            {
                return _metaDataFileParser.GenerateBytes(SoundMetaEditor.MetaDataFileVersion, shown);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The Sound tab's file in the scene, as a slot's play sets it, republished with the other two.
        void ShowSoundMeta(PackFile? file)
        {
            _asset.Data.SoundMetaData = file;
            _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, _asset.Data.PersistMetaData);
        }

        private void OnSelectedMetaDataAttributeChanged(SelecteMetaDataAttributeChangedEvent @event)
        {
            RecreateMetaDataInformation();
            ShowTimelineSelection();
            if (!_selectingMarker)
                _splashPoint = CombatMetaDataPoint.SplashStart;
            UpdateEditTarget();
        }

        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event)
        {
            RecreateMetaDataInformation();
            RebuildTimeline();
        }

        void OnMetaDataChanged(SceneObject sceneObject) => RecreateMetaDataInformation();

        partial void OnShowVfxChanged(bool value)
        {
            _metaDataFactory.ShowVfx = value;
            RecreateMetaDataInformation();
        }

        partial void OnHideDistanceCulledEffectsChanged(bool value)
        {
            _metaDataFactory.HideDistanceCulled = value;
            RecreateMetaDataInformation();
        }

        partial void OnAuraVfxChanged(string value)
        {
            _metaDataFactory.AuraVfx = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            RecreateMetaDataInformation();
        }

        partial void OnWeaponVfxChanged(string value)
        {
            _metaDataFactory.WeaponVfx = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            RecreateMetaDataInformation();
        }

        [RelayCommand] void BrowseAuraVfx() => AuraVfx = BrowseVfx() ?? AuraVfx;
        [RelayCommand] void BrowseWeaponVfx() => WeaponVfx = BrowseVfx() ?? WeaponVfx;
        [RelayCommand] void ClearAuraVfx() => AuraVfx = "";
        [RelayCommand] void ClearWeaponVfx() => WeaponVfx = "";

        // A vfx file picked from the packs, named as an EFFECT tag names it: its path under vfx/.
        string? BrowseVfx()
        {
            static bool UnderVfx(string path) => path.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("vfx/", StringComparison.OrdinalIgnoreCase);
            var result = _standardDialogs.DisplayBrowseDialog([".xml"], path => UnderVfx(path.TrimStart('\\', '/')));
            if (!result.Result || result.File == null)
                return null;
            var path = _packFileService.GetFullPath(result.File).TrimStart('\\', '/');
            return UnderVfx(path) ? path[4..] : path;
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
            else if (evnt.FileOwner == SoundMetaEditor)
            {
                SoundMetaChangedOnDisk = "";
                ShowSoundMeta(newFile);
            }
            else
                throw new Exception($"Unable to determine file owner when reciving a file save event in SuperView. Owner:{evnt.FileOwner}, File:{evnt.NewPath}");
        }

        void Initialize()
        {
            PersistentMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            MetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            SoundMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            // Check meta checks against the playing clip and the skeleton. A persistent meta plays under every clip of the set,
            // so its tab gets no clip length to check start times against.
            MetaEditor.CheckScene = () => new MetaCheckScene(_asset.Data.Player.AnimationClip?.PlayTimeInSec, _asset.Data.Skeleton?.BoneNames);
            SoundMetaEditor.CheckScene = MetaEditor.CheckScene;
            PersistentMetaEditor.CheckScene = () => new MetaCheckScene(null, _asset.Data.Skeleton?.BoneNames);
            // A save that would override someone else's meta can make a custom copy instead, which this points the slot at.
            PersistentMetaEditor.SaveAsCustom = () => SaveCustomCopy(PersistentMetaEditor);
            MetaEditor.SaveAsCustom = () => SaveCustomCopy(MetaEditor);
            SoundMetaEditor.SaveAsCustom = () => SaveCustomCopy(SoundMetaEditor);

            var assetViewModel = _sceneObjectViewModelBuilder.CreateAsset("SuperViewRoot", true, "Root", Color.Black,null);
            SceneObjects.Add(assetViewModel);

            assetViewModel.Data.MetaDataChanged += OnMetaDataChanged;
            assetViewModel.Data.AnimationChanged += OnAnimationChanged;

            // New, delete, move, paste and undo rebuild an editor's Tags
            foreach (var editor in AllEditors)
            {
                editor.Tags.CollectionChanged += OnTagsChanged;
                editor.UndoCommand.CanExecuteChanged += OnEditorUndoChanged;
                editor.RedoCommand.CanExecuteChanged += OnEditorUndoChanged;
            }
            Player.TimelineItemClicked += OnTimelineItemClicked;

            _asset = assetViewModel;
            InitTimeline();
            SlotGrid = new SlotGridViewModel(_asset.FragAndSlotSelection, CannotPlay, x => SourceOf(Key(x)), Save, OfferToSaveSlotGrid, OpenFullGrid);
            SlotGrid.Changed += ShowSelectedRow;
            SlotGrid.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SlotGridViewModel.SelectedRow))
                    ShowSelectedRow();
            };
            _asset.FragAndSlotSelection.PropertyChanged += OnSlotSelectionChanged;

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
            // The slot's .snd.meta plays with the clip, so the builder takes its tags after the clip meta's, as one file. The tags
            // are the editors' own, so a selected one is still known by reference.
            var (clip, sound) = (MetaEditor.ParsedFile, SoundMetaEditor.ParsedFile);
            var meta = clip == null && sound == null ? null
                : new ParsedMetadataFile { Version = (clip ?? sound)!.Version, Attributes = [.. clip?.Attributes ?? [], .. sound?.Attributes ?? []] };
            var selected = CurrentEditor.SelectedAttribute;

            _asset.Data.MetaDataItems = _metaDataFactory.Create(persist, meta, selected, _asset.Data.MainNode, _asset.Data, _asset.Data.Player, _asset.FragAndSlotSelection.FragmentList.SelectedItem);
            foreach (var t in old.Except(_asset.Data.MetaDataItems))
                t.CleanUp();
            _asset.Data.Player.Refresh();

            var ids = MetaDataBuilder.VfxFilterIds(persist, meta);
            var filter = _metaDataFactory.VfxFilterId ?? ids.FirstOrDefault();
            _showingVfxFilter = true;
            VfxFilterIds = filter == 0 || ids.Contains(filter) ? ids : [.. ids.Append(filter).Order()];
            VfxFilterId = filter;
            _showingVfxFilter = false;
            RebuildProblems();
        }

        // Filled again only when a line changes, so the one picked stays picked across the rebuild that opening its tag starts.
        void RebuildProblems()
        {
            if (PersistentMetaEditor == null || MetaEditor == null || SoundMetaEditor == null)
                return;
            var problems = MetaDataProblem.Create(PersistentMetaEditor.Tags, MetaEditor.Tags, _metaDataFactory.Problems, _localizationManager, SoundMetaEditor.Tags);
            if (problems.SequenceEqual(Problems))
                return;

            _showingProblems = true;
            var picked = SelectedProblem;
            Problems.Clear();
            foreach (var problem in problems)
                Problems.Add(problem);
            SelectedProblem = Problems.FirstOrDefault(x => x == picked);
            _showingProblems = false;
            OnPropertyChanged(nameof(HasProblems));
            OnPropertyChanged(nameof(ProblemsHeader));
        }

        partial void OnSelectedProblemChanged(MetaDataProblem? value)
        {
            if (_showingProblems || value == null)
                return;
            var start = ParsedMetadataTimeRange.TryCreate(value.Source, out var range) && float.IsFinite(range.StartTime) && !range.IsWholeAnimationRange
                ? range.StartTime
                : (float?)null;
            OpenTag(value.Owner, value.Source, start);
            FocusMarker(value.Source);
        }

        private void OnSceneObjectUpdated(SceneObjectUpdateEvent e)
        {
            if (_pointingRow)
                return;
            var (persistent, animation, sound) = (PersistentMetaEditor.ParsedFile, MetaEditor.ParsedFile, SoundMetaEditor.ParsedFile);
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);
            // A file read again has new tags: the 3D edits' undo steps point at the old ones.
            if (PersistentMetaEditor.ParsedFile != persistent)
                _editSession.ResetDocument(PersistentMetaEditor);
            if (MetaEditor.ParsedFile != animation)
                _editSession.ResetDocument(MetaEditor);

            // A .snd.meta that does not parse is left out and named, so a bad one breaks nothing else. Another file in the Sound
            // tab clears what it said about the last one changing on disk.
            if (e.Owner.SoundMetaData != SoundMetaEditor.CurrentFile)
                SoundMetaChangedOnDisk = "";
            _unreadableSound = [];
            _persistSoundMeta = null;
            try
            {
                SoundMetaEditor.LoadFile(e.Owner.SoundMetaData);
            }
            catch (Exception ex)
            {
                Unreadable("Slot", e.Owner.SoundMetaData!, ex);
                SoundMetaEditor.LoadFile(null);
            }
            if (SoundMetaEditor.ParsedFile != sound)
                _editSession.ResetDocument(SoundMetaEditor);
            if (e.Owner.PersistSoundMetaData is { } persist)
            {
                try
                {
                    _persistSoundMeta = _metaDataFileParser.ParseFile(persist.DataSource.ReadData());
                }
                catch (Exception ex)
                {
                    Unreadable("Persistent", persist, ex);
                }
            }

            PersistentMetaSource = Describe(PersistentMetaEditor.CurrentFile);
            MetaSource = Describe(MetaEditor.CurrentFile);
            SoundMetaSource = Describe(SoundMetaEditor.CurrentFile);
            // From the scene's file, so one the tab could not read is still watched, and read again when it is fixed on disk.
            _soundMetaPath = e.Owner.SoundMetaData == null ? "" : CustomMeta.Normalise(_packFileService.GetFullPath(e.Owner.SoundMetaData));
            OnPropertyChanged(nameof(SoundFileProblems));
            // Only the label: the field is the selected row's, and a row picked in the grid plays before the panel shows it, so
            // writing the field back would give that row the previous one's meta.
            SlotMetaSource = SourceOf(Key(SlotMeta.Trim()));

            RecreateMetaDataInformation();
            RebuildTimeline();
            UpdateEditTarget();
            NotifyMetaFileState();
        }

        void OnSlotSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BinAnimationViewModel.MetaDataName) or nameof(BinAnimationViewModel.MetaDataPersistName))
                NotifyMetaFileState();
        }

        void NotifyMetaFileState()
        {
            OnPropertyChanged(nameof(AnimationMetaFilePath));
            OnPropertyChanged(nameof(PersistentMetaFilePath));
            OnPropertyChanged(nameof(CanCreateAnimationMetaFile));
            OnPropertyChanged(nameof(CanCreatePersistentMetaFile));
            OnPropertyChanged(nameof(IsAnimationMetaFileNotNamed));
            OnPropertyChanged(nameof(IsPersistentMetaFileNotNamed));
        }

        public void CreateAnimationMetaFile()
        {
            if (CanCreateAnimationMetaFile && CreateEmptyMetaFile(AnimationMetaFilePath) is { } file)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, file, _asset.Data.PersistMetaData);
        }

        public void CreatePersistentMetaFile()
        {
            if (CanCreatePersistentMetaFile && CreateEmptyMetaFile(PersistentMetaFilePath) is { } file)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, file);
        }

        // An empty version 2 META, the version WH3's files use, at the path the slot names, in the editable pack.
        PackFile? CreateEmptyMetaFile(string path)
        {
            var bytes = _metaDataFileParser.GenerateBytes(2, new ParsedMetadataFile { Version = 2 });
            return _fileSaveService.Save(path, bytes, false);
        }

        void OnAnimationChanged(GameWorld.Core.Animation.AnimationClip? clip) => RebuildTimeline();
        void OnTagsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Tags added, deleted, moved, pasted or undone: the document's 3D edit target may point at a tag no longer in it.
            if (AllEditors.FirstOrDefault(x => ReferenceEquals(sender, x.Tags)) is { } editor)
                _editSession.ResetDocument(editor);
            RebuildTimeline();
        }

        // One marker per timed tag in the three META files, on the animation player's scrub bar, a lane for each file.
        void RebuildTimeline()
        {
            if (_asset == null || SoundMetaEditor == null)
                return;

            var sources = new[] { MetaDataDocumentOwner.Persistent, MetaDataDocumentOwner.Animation, MetaDataDocumentOwner.Sound }
                .SelectMany(owner => EditorOf(TabOf(owner)).Tags.Select(x => new MetaDataInspectionSource(x._input, owner, x.HasError() == null)));
            var index = MetaDataInspectionIndex.Create(sources);

            var clipDuration = _asset.Data.Player.GetAnimationLengthUs() / 1_000_000.0;
            var wholeAnimation = _localizationManager.Get("SuperView.Timeline.WholeAnimation");

            Player.SelectedTimelineItem = null;
            Player.TimelineItems.Clear();
            foreach (var item in index.Items.Where(x => x.MarkerKind != null))
                Player.TimelineItems.Add(new MetaDataTimelineItem(item, clipDuration, OwnerName(item.Owner), wholeAnimation));
            ShowTimelineSelection();
            RebuildProblems();
        }

        string OwnerName(MetaDataDocumentOwner owner) => _localizationManager.Get(owner switch
        {
            MetaDataDocumentOwner.Persistent => "MetadataEditor.Tabs.Persistent",
            MetaDataDocumentOwner.Sound => "MetadataEditor.Tabs.Sound",
            _ => "MetadataEditor.Tabs.Animation",
        });

        // The two enums list the files in the tabs' order.
        static MetaTab TabOf(MetaDataDocumentOwner owner) => (MetaTab)(int)owner;

        // Highlights the marker of the tag selected on the open tab
        void ShowTimelineSelection()
        {
            if (Player == null || SoundMetaEditor == null)
                return;
            var owner = (MetaDataDocumentOwner)SelectedTabControllerIndex;
            var selected = CurrentEditor.SelectedAttribute;
            Player.SelectedTimelineItem = selected == null
                ? null
                : Player.TimelineItems.OfType<MetaDataTimelineItem>().FirstOrDefault(x => x.Item.Owner == owner && ReferenceEquals(x.Item.Source, selected));
        }

        // A clicked marker opens its tag: its tab, the tag selected, and the animation at the tag's start
        void OnTimelineItemClicked(ITimelineItem clicked)
        {
            if (clicked is not MetaDataTimelineItem item)
                return;
            OpenTag(item.Item.Owner, item.Item.Source, item.Start);
        }

        // A tag's tab, the tag selected, and the animation at the given time. A tag outside the clip is gone to at the nearer end.
        void OpenTag(MetaDataDocumentOwner owner, ParsedMetadataAttribute source, double? time)
        {
            if (time != null)
                Player.SetAnimationTime(Math.Clamp(time.Value, 0, _asset.Data.Player.GetAnimationLengthUs() / 1_000_000.0));

            // The tab first, so the rebuild the selection sets off highlights the tag in the tab that shows it. That rebuild is the only
            // one when the pick changes, so the tab switch skips its own then. Delete acts on every row marked selected, which the list
            // keeps in step with its own clicks but not with this one.
            var editor = EditorOf(TabOf(owner));
            var row = editor.Tags.FirstOrDefault(x => ReferenceEquals(x._input, source));
            _tickSwitchingTab = row != null && editor.SelectedTag != row;
            try
            {
                SelectedTabControllerIndex = (int)TabOf(owner);
            }
            finally
            {
                _tickSwitchingTab = false;
            }
            if (row == null)
                return;
            foreach (var tag in editor.Tags)
                tag.IsSelected = tag == row;
            editor.SelectedTag = row;
        }

        /// <summary>A .snd.meta the played slot or the persistent row names that no loaded pack has, and one that does not parse.</summary>
        public IReadOnlyList<string> SoundFileProblems
        {
            get
            {
                var selection = _asset.FragAndSlotSelection;
                return [.. Missing("Slot", selection.SoundMetaName), .. Missing("Persistent", selection.SoundMetaPersistName), .. _unreadableSound];

                IEnumerable<string> Missing(string which, string? path) => string.IsNullOrWhiteSpace(path) || _packFileService.FindFile(path) != null
                    ? [] : [$"{which} .snd.meta {path} is in no loaded pack."];
            }
        }

        List<string> _unreadableSound = [];

        void Unreadable(string which, PackFile file, Exception ex) =>
            _unreadableSound.Add($"{which} .snd.meta {CustomMeta.Normalise(_packFileService.GetFullPath(file))} could not be read: {ex.Message}");

        string Describe(PackFile? file)
        {
            if (file == null)
                return "";
            var path = CustomMeta.Normalise(_packFileService.GetFullPath(file));
            var source = CustomMeta.Source(_packFileService, path);
            return source.Length == 0 ? path : $"From {source}: {path}";
        }

        partial void OnSlotMetaChanged(string value)
        {
            SlotMetaSource = SourceOf(Key(value.Trim()));
            EditSelectedRow(row => row.Meta = value);
        }

        string SourceOf(string path)
        {
            var source = path.Length == 0 ? "" : CustomMeta.Source(_packFileService, path);
            return path.Length == 0 ? "" : source.Length == 0 ? "Not in any loaded pack" : $"From {source}";
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

        ISpatialMetaDataPreview? FindPreview(ParsedMetadataAttribute? source) =>
            source == null ? null : _asset?.Data.MetaDataItems.OfType<ISpatialMetaDataPreview>().FirstOrDefault(x => ReferenceEquals(x.Source, source));

        // The gizmo goes on the tag selected on the open tab, when the tag is drawn in the scene and has a point to move.
        void UpdateEditTarget()
        {
            if (_editSession == null || PersistentMetaEditor == null || MetaEditor == null || SoundMetaEditor == null)
                return;

            _gizmo.EndDrag();
            var editor = CurrentEditor;
            var source = editor.SelectedAttribute;
            var point = source is SplashAttack_v3 or SplashAttack_v10 ? _splashPoint : CombatMetaDataPoint.Position;
            // Looked up on every call: a rebuild replaces the instance that draws the tag.
            if (FindPreview(source) == null || !_editSession.SetTarget(editor, source!, point, () => FindPreview(source)?.ReferenceWorldTransform ?? Matrix.Identity))
                _editSession.ClearTarget(editor);

            if (EditTool == MetaDataEditTool.Rotate && !_editSession.CanRotate)
                EditTool = MetaDataEditTool.Move;
            UpdateGizmo();
            NotifyEditState();
        }

        partial void OnEditToolChanged(MetaDataEditTool value) => UpdateGizmo();

        void UpdateGizmo()
        {
            _gizmo.TransformMode = EditTool == MetaDataEditTool.Rotate ? CombatMetaDataTransformMode.Rotate : CombatMetaDataTransformMode.Translate;
            _gizmo.IsEnabled = EditTool != MetaDataEditTool.Select && _editSession.CurrentSource != null;
        }

        void NotifyEditState()
        {
            OnPropertyChanged(nameof(CanEditSelectedIn3D));
            OnPropertyChanged(nameof(CanRotateSelectedIn3D));
            OnPropertyChanged(nameof(CanUndoEditIn3D));
            OnPropertyChanged(nameof(CanRedoEditIn3D));
            UndoEditIn3DCommand.NotifyCanExecuteChanged();
            RedoEditIn3DCommand.NotifyCanExecuteChanged();
            FocusSelectedTagCommand.NotifyCanExecuteChanged();
        }

        void OnEditValueChanged(CombatMetaDataEditChange change)
        {
            var editor = change.Owner as MetaDataEditorViewModel;
            if (change.Kind == CombatMetaDataEditChangeKind.Preview)
            {
                // Effects, props and most markers follow their tag on the next frame and are rebuilt when the drag ends
                // (an effect's particles, a prop's model). IMPACT_POS, TARGET_POS, FIRE_POS and SPLASH_ATTACK are drawn
                // where their values put them, and cheap to redraw.
                if (FindPreview(change.Source) is not { FollowsTagLive: true })
                    RebuildMarkersDuringDrag();
                editor?.RefreshAttributeValues(change.Source, countAsEdit: false);
                return;
            }

            editor?.RefreshAttributeValues(change.Source, countAsEdit: change.Kind != CombatMetaDataEditChangeKind.Cancelled);
            RecreateMetaDataInformation();
            NotifyEditState();
        }

        // A splash attack whose start is dragged onto its end does not draw; the builder lists that as a problem on the tag.
        void RebuildMarkersDuringDrag() => RecreateMetaDataInformation();

        IReadOnlyList<IMetaDataMarkerPreview> GetMarkers() =>
            _asset?.Data.MetaDataItems.OfType<IMetaDataMarkerPreview>().ToList() ?? [];

        // A marker clicked in the viewport: its tag's tab, the tag selected, and the gizmo on the point clicked.
        void SelectMarker(ParsedMetadataAttribute source, MetaDataMarkerPoint point)
        {
            // The clip's and the Sound tab's tags are drawn as one file, so the tag is looked for in each tab's own.
            var tab = new[] { MetaTab.Persistent, MetaTab.Clip, MetaTab.Sound }.FirstOrDefault(x => EditorOf(x).Tags.Any(y => ReferenceEquals(y._input, source)), (MetaTab)(-1));
            if (tab < 0)
                return;
            var editor = EditorOf(tab);
            var tag = editor.Tags.First(x => ReferenceEquals(x._input, source));

            _splashPoint = point == MetaDataMarkerPoint.SplashEnd ? CombatMetaDataPoint.SplashEnd : CombatMetaDataPoint.SplashStart;
            _selectingMarker = true;
            try
            {
                SelectedTabControllerIndex = (int)tab;
                if (editor.SelectedTag == tag)
                    UpdateEditTarget(); // the other end of the same splash attack
                else
                    editor.SelectedTag = tag;
            }
            finally
            {
                _selectingMarker = false;
            }
        }

        void FocusMarker(ParsedMetadataAttribute source)
        {
            if (FindPreview(source) is { } preview)
                FocusService.LookAt(preview.FocusPosition);
        }

        void ClearMarkerSelection() => CurrentEditor.SelectedTag = null;

        [RelayCommand(CanExecute = nameof(CanUndoEditIn3D))] void UndoEditIn3D() => CurrentEditor.UndoCommand.Execute(null);
        [RelayCommand(CanExecute = nameof(CanRedoEditIn3D))] void RedoEditIn3D() => CurrentEditor.RedoCommand.Execute(null);

        void OnEditorUndoChanged(object? sender, EventArgs e) => NotifyEditState();

        IEnumerable<MetaDataEditorViewModel> AllEditors => [PersistentMetaEditor, MetaEditor, SoundMetaEditor];

        bool CanFocusSelectedTag() => FindPreview(CurrentEditor?.SelectedAttribute) != null;
        [RelayCommand(CanExecute = nameof(CanFocusSelectedTag))] void FocusSelectedTag() => FocusMarker(CurrentEditor.SelectedAttribute!);

        public override void Close()
        {
            try
            {
                CloseSound();
            }
            finally
            {
                _markerPicker.Disconnect();
                _gizmo.EndDrag();
                _editSession.ValueChanged -= OnEditValueChanged;
                _editSession.HistoryChanged -= NotifyEditState;
                GameWorld?.RemoveComponent(_markerPicker);
                GameWorld?.RemoveComponent(_gizmo);
                _eventHub?.UnRegister(this);
                Player.TimelineItemClicked -= OnTimelineItemClicked;
                foreach (var editor in AllEditors)
                {
                    editor.Tags.CollectionChanged -= OnTagsChanged;
                    editor.UndoCommand.CanExecuteChanged -= OnEditorUndoChanged;
                    editor.RedoCommand.CanExecuteChanged -= OnEditorUndoChanged;
                }
                _asset.Data.AnimationChanged -= OnAnimationChanged;
                _asset.FragAndSlotSelection.PropertyChanged -= OnSlotSelectionChanged;
                _globalEventHub.UnRegister(this);
                base.Close();
            }
        }

        // Only an edited tab is saved: writing an untouched vanilla meta would override it for nothing. The slot grid's edits are
        // written into their set.
        public bool Save()
        {
            var res0 = !PersistentMetaEditor.HasUnsavedChanges || PersistentMetaEditor.Save();
            var res1 = !MetaEditor.HasUnsavedChanges || MetaEditor.Save();
            var res2 = !SoundMetaEditor.HasUnsavedChanges || SoundMetaEditor.Save();
            var res3 = !SlotGrid.HasUnsavedChanges || Said(SaveSlotGrid());
            return res0 && res1 && res2 && res3;
        }

        bool Said(string? problem)
        {
            if (problem != null)
                _standardDialogs.ShowDialogBox(problem);
            return problem == null;
        }

        /// <summary>Every slot of the picked set, under the player; the slot panel shows its selected row.</summary>
        public SlotGridViewModel SlotGrid { get; private set; } = null!;
        public override IReadOnlyList<EditorPane> EditorPanes => [new("SlotGrid", "SuperView.SlotGrid", typeof(SlotGridView), SlotGrid, UnderPlayer: true)];

        // The slot panel: the slot grid's selected row's three files, typed or browsed. A change is an edit of that row, which plays at
        // once and is written with the set by Save.
        [ObservableProperty] string _slotAnim = "";
        [ObservableProperty] string _slotMeta = "";
        [ObservableProperty] string _slotSound = "";
        bool _showingRow;
        bool _pointingRow;

        void ShowSelectedRow()
        {
            var row = SlotGrid.SelectedRow;
            _showingRow = true;
            (SlotAnim, SlotMeta, SlotSound) = (row?.Animation ?? "", row?.Meta ?? "", row?.Sound ?? "");
            _showingRow = false;
            SlotHasNoMeta = row != null && string.IsNullOrEmpty(row.Meta);
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        partial void OnSlotAnimChanged(string value) => EditSelectedRow(row => row.Animation = value);
        partial void OnSlotSoundChanged(string value) => EditSelectedRow(row => row.Sound = value);

        void EditSelectedRow(Action<SlotGridRow> edit)
        {
            if (_showingRow || SlotGrid == null)
                return;
            if (SlotGrid.SelectedRow is { } row)
                edit(row);
            else
                SlotGrid.Status = "Pick an animation set and a slot first.";
        }

        // Why a row's files cannot play: a file in no loaded pack, or an animation for another skeleton. Not the sound: a row whose .snd.meta
        // no loaded pack has still plays, with an empty Sound tab, because CA's slots name many such files.
        string? CannotPlay(SlotGridRow row)
        {
            var (anim, meta) = (Key(row.Animation.Trim()), Key(row.Meta.Trim()));
            var missing = new[] { anim, meta }.FirstOrDefault(x => x.Length != 0 && _packFileService.FindFile(x) == null);
            var set = SlotGrid.Set;
            return missing != null ? $"{missing} is in no loaded pack."
                : set != null && anim.Length != 0 && !_skeletonAnimationLookUpHelper.CopyAnimationsForSkeleton(set.SkeletonName).Any(x => Key(x.AnimationFile) == anim)
                    ? $"{anim} is not an animation for {set.SkeletonName}, the set's skeleton."
                : null;
        }

        // An animation offered only if it is for the set's skeleton, as one for another skeleton cannot play on it.
        [RelayCommand]
        void BrowseSlotAnim()
        {
            var skeleton = _asset.FragAndSlotSelection.FragmentList.SelectedItem?.SkeletonName;
            var anims = skeleton == null ? null : _skeletonAnimationLookUpHelper.CopyAnimationsForSkeleton(skeleton).Select(x => Key(x.AnimationFile)).ToHashSet();
            Browse(".anim", anims == null ? null : x => anims.Contains(Key(x)), x => SlotAnim = x);
        }

        [RelayCommand] void BrowseSlotMeta() => Browse(".anm.meta", null, x => SlotMeta = x);
        [RelayCommand] void BrowseSlotSound() => Browse(".snd.meta", null, x => SlotSound = x);

        // The Sound tab's own browse. While a slot plays, its Sound is set, as the slot panel's browse does. While a clip from "Any
        // animation" plays, the file opens in the tab and holds until another clip or slot plays: the slot still picked in the grid is
        // not what plays, and setting its Sound would play it in place of the clip.
        [RelayCommand]
        void BrowseSoundTab()
        {
            if (SlotGrid.SelectedRow != null && _asset.FragAndSlotSelection.PlayingSlot)
            {
                BrowseSlotSound();
                return;
            }
            Browse(".snd.meta", null, x => ShowSoundMeta(_packFileService.FindFile(x)));
        }

        static string Key(string path) => path.Replace('\\', '/').ToLowerInvariant();

        void Browse(string extension, Func<string, bool>? filter, Action<string> picked)
        {
            var pick = _standardDialogs.DisplayBrowseDialog([extension], filter);
            if (pick.Result && pick.File != null)
                picked(Key(_packFileService.GetFullPath(pick.File)));
        }

        // Save writes a meta back over the file it was read from, which every set naming that file shares. This writes the open
        // tab's meta to a new file instead and points the slot that tab shows at it: its Meta, or its Sound for the Sound tab.
        [RelayCommand]
        void SaveMetaAs() => SaveCustomCopy(EditorOf((MetaTab)SelectedTabControllerIndex));

        // The save dialog offers the original's name with the custom prefix, beside it. True when saved.
        bool SaveCustomCopy(MetaDataEditorViewModel editor)
        {
            var sound = editor == SoundMetaEditor;
            var selection = _asset.FragAndSlotSelection;
            var slot = editor == PersistentMetaEditor
                ? selection.FragmentSlotList.PossibleValues.FirstOrDefault(x => x.SlotName.StartsWith("PERSISTENT_METADATA_") && x.MetaFile == selection.MetaDataPersistName)
                : selection.FragmentSlotList.SelectedItem;
            var problem = sound && slot == null && editor.ParsedFile != null ? "No slot is picked to point at a copy of the Sound tab's .snd.meta. Pick an animation set and a slot first."
                : slot == null || editor.ParsedFile == null ? "This tab has no meta file open to save."
                : editor.Tags.Select(x => x.HasError()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (problem == null)
            {
                var original = editor.CurrentFile != null ? _packFileService.GetFullPath(editor.CurrentFile) : sound ? slot!.SoundFile : slot!.MetaFile;
                var path = CustomMeta.Ask(_standardDialogs, _packFileService, _settings, original, CustomMeta.Offered(original, CustomMeta.Prefix(_settings)),
                    sound ? ".snd.meta" : CustomMeta.Extension);
                if (path == null)
                    return false;
                // A path another pack has is refused, as the copy would override that pack's file for every set that names it. One the
                // editable pack has is the user's own, and is written over.
                problem = _packFileService.FindFile(path) != null && _packFileService.FindFile(path, _packFileService.GetEditablePack()) == null
                    ? $"{path} is already in another pack. To use it, type it into the slot's {(sound ? "Sound" : "Meta")} field."
                    : SaveRowMeta(SlotGrid.RowOf(slot)!, sound ? (row, x) => row.Sound = x : (row, x) => row.Meta = x, path,
                        _metaDataFileParser.GenerateBytes(editor.MetaDataFileVersion, editor.ParsedFile!));
                // A copy saved over the tab's own file (the offered path, once the tab shows a custom copy) loads nothing new, and
                // what the tab shows is what was written.
                if (problem == null)
                {
                    editor.HasUnsavedChanges = false;
                    if (sound)
                        SoundMetaChangedOnDisk = "";
                }
            }
            if (problem != null)
                _standardDialogs.ShowDialogBox(problem);
            return problem == null;
        }

        // A slot that names no meta gets an empty one, at the animation's path with .anm.meta, or that prefixed when a pack already
        // has a file there. A path any loaded pack has is refused, so an existing meta is never blanked.
        [RelayCommand]
        void NewMeta()
        {
            var row = SlotGrid.SelectedRow;
            var anim = row == null ? "" : Key(row.Animation.Trim());
            var problem = row == null ? "Pick an animation set and a slot first."
                : row.Meta.Trim().Length != 0 ? "This slot already has a meta."
                : anim.Length == 0 ? "This slot has no animation to name a meta after."
                : null;
            if (problem == null)
            {
                var plain = (anim.EndsWith(".anim", StringComparison.Ordinal) ? anim[..^".anim".Length] : anim) + CustomMeta.Extension;
                var offered = _packFileService.FindFile(plain) == null ? plain : CustomMeta.Offered(plain, CustomMeta.Prefix(_settings));
                var path = CustomMeta.Ask(_standardDialogs, _packFileService, _settings, plain, offered);
                if (path == null)
                    return;
                problem = _packFileService.FindFile(path) != null ? $"{path} is already in a loaded pack. To use it, type it into the slot's Meta field."
                    : SaveRowMeta(row!, (r, x) => r.Meta = x, path, _metaDataFileParser.GenerateBytes(2, new ParsedMetadataFile { Version = 2 }));
            }
            if (problem != null)
                _standardDialogs.ShowDialogBox(problem);
        }

        // Points a row at a new meta, as an edit, and saves the set with every other edit of the grid, then writes the meta. The edit
        // is taken back when the save is refused.
        string? SaveRowMeta(SlotGridRow row, Action<SlotGridRow, string> point, string metaPath, byte[] metaBytes)
        {
            var before = row.Values;
            // The picked row plays its edit at once. A Sound edit plays even though the new file is not written yet, which would empty
            // the Sound tab, so the tabs are left as they are until the save loads the new file or a refusal plays the row as it was.
            // A refused save then loses no unsaved edit.
            _pointingRow = true;
            try
            {
                point(row, metaPath);
            }
            finally
            {
                _pointingRow = false;
            }
            var problem = SaveSlotGrid(Key(metaPath.Trim()), metaBytes);
            if (problem != null && row.Values != before)
                SlotGrid.UndoCommand.Execute(null);
            return problem;
        }

        // Writes the slot grid's edits into their set in the editable pack, then metaBytes, when given, at metaPath, and picks the saved
        // set in the shown one's place. Null when saved, else why not, and nothing is written then.
        string? SaveSlotGrid(string? metaPath = null, byte[]? metaBytes = null)
        {
            var shown = SlotGrid.Set;
            var (problem, saved) = WriteSlots(shown, SlotGrid.Rows.Where(x => x.IsEdited).ToList());
            if (problem != null)
                return problem;
            // Written, so no longer edits. The picks below include WPF's own: the picker writes null back while the saved copy takes
            // the shown set's place in its items, and an edit still held was offered for saving again, into a table it no longer
            // matched ("...has no variant naming...").
            SlotGrid.Discard();
            if (metaBytes != null)
                _fileSaveService.Save(metaPath!, metaBytes, false);

            // The slot list is built from a set when it is picked, so the saved copy is picked in the shown one's place, then the slot
            // at the same place in the list: an edit changes a row's files, never the rows. The edited tab loads its new file; the
            // other keeps any unsaved edit, as its file is the same one.
            var selection = _asset.FragAndSlotSelection;
            var slots = selection.FragmentSlotList;
            var at = slots.SelectedItem == null ? -1 : slots.PossibleValues.IndexOf(slots.SelectedItem);
            var name = slots.SelectedItem?.SlotName;
            selection.FragmentList.UpdatePossibleValues(selection.FragmentList.PossibleValues.Select(x => x == shown ? saved! : x).ToList());
            selection.FragmentList.SelectedItem = saved;
            slots.SelectedItem = at >= 0 && at < slots.PossibleValues.Count && slots.PossibleValues[at].SlotName == name ? slots.PossibleValues[at]
                : slots.PossibleValues.FirstOrDefault(x => x.SlotName == name);
            return null;
        }

        // What the grid leaves out, the set's header and adding or removing slots, is in the set's own editor: the animpack holding a
        // battle table, the editable pack's copy when there is one, or a campaign set's file.
        void OpenFullGrid()
        {
            var set = SlotGrid.Set;
            var path = set is AnimationBinWh3 table ? AnimTableEdits.Latest(_packFileService, table).Parent?.FileName : set?.FullPath;
            var editable = _packFileService.GetEditablePack();
            var file = path == null ? null : (editable == null ? null : _packFileService.FindFile(path, editable)) ?? _packFileService.FindFile(path);
            if (file == null)
                SlotGrid.Status = set == null ? "Pick an animation set first." : $"{set.Name}'s file is in no loaded pack.";
            else
                _uiCommandFactory.Create<OpenEditorCommand>().Execute(file);
        }

        // Another set is being picked while the grid has unsaved edits to this one: save them into it, or drop them. Closing the box
        // saves, so nothing is lost by accident. The saved copy takes the old one's place in the list, the pick going ahead.
        void OfferToSaveSlotGrid(IAnimationBinGenericFormat set)
        {
            if (_standardDialogs.ShowChoiceBox($"The slot grid has unsaved edits to {set.Name}.", "Slot grid", ["Save them", "Drop them"]) == 1)
                return;
            var (problem, saved) = WriteSlots(set, SlotGrid.Rows.Where(x => x.IsEdited).ToList());
            if (problem != null)
            {
                _standardDialogs.ShowDialogBox($"The slot grid's edits were not saved: {problem}");
                return;
            }
            SlotGrid.Discard();    // written; the swap below makes WPF pick null too, as in SaveSlotGrid
            var list = _asset.FragAndSlotSelection.FragmentList;
            if (list.PossibleValues.IndexOf(set) is var i and >= 0)
                list.PossibleValues[i] = saved!;
            if (list.Values.IndexOf(set) is var j and >= 0)
                list.Values[j] = saved!;
        }

        // Writes edited rows into a set: a battle table is spliced into its animpack, a campaign set is a file of its own and is
        // written whole. Either is first read again from the editable pack, so a set picked before an earlier save does not undo it.
        // A row is found in the set by the files it names as saved. Paths are stored lower-cased with forward slashes, as CA's rows
        // have them. Returns the set as saved, or why not, and nothing is written then.
        (string? Problem, IAnimationBinGenericFormat? Saved) WriteSlots(IAnimationBinGenericFormat? set, List<SlotGridRow> edits)
        {
            var editable = _packFileService.GetEditablePack();
            if (editable == null)
                return ("No editable pack is selected.", null);
            try
            {
                if (set is AnimationBinWh3 table)
                {
                    table = AnimTableEdits.Latest(_packFileService, table);
                    var before = table.ToByteArray();
                    void Undo() => table.AnimationTableEntries = new AnimationBinWh3(table.FileName, before).AnimationTableEntries;
                    try
                    {
                        foreach (var row in edits)
                        {
                            var v = row.Values;
                            AnimTableEdits.SetFiles(table, row.Slot, Key(v.Animation.Trim()), Key(v.Meta.Trim()), Key(v.Sound.Trim()));
                            AnimTableEdits.SetBlendAndWeight(table, row.Slot.SlotIndex, v.BlendIn, v.Weight);
                            AnimTableEdits.Row(table, row.Slot.SlotIndex).WeaponBools = v.Weapons;
                        }
                    }
                    catch
                    {
                        Undo();
                        throw;
                    }
                    if (AnimTableEdits.Save(_packFileService, _fileSaveService, table) is { } refused)
                    {
                        Undo();
                        return (refused, null);
                    }
                    return (null, AnimTableEdits.Latest(_packFileService, table));
                }
                if (set is CampaignAnimationSet campaign)
                {
                    CampaignAnimationSet Read() => new(campaign.FullPath, CampaignAnimationBinLoader.Load(
                        (_packFileService.FindFile(campaign.FullPath, editable) ?? _packFileService.FindFile(campaign.FullPath))!.DataSource.ReadDataAsChunk()));
                    var copy = Read();
                    foreach (var row in edits)
                        copy.SetFiles(row.Slot, Key(row.Animation.Trim()), Key(row.Meta.Trim()), Key(row.Sound.Trim()));
                    _fileSaveService.Save(campaign.FullPath, CampaignAnimationBinLoader.Write(copy.Bin, Path.GetFileNameWithoutExtension(campaign.FullPath)), false);
                    // Read back as written: the writer lower-cases every string, so a set with capitals is not what it wrote, and a
                    // slot picked from it would name files the file no longer does.
                    return (null, Read());
                }
                return ("Pick an animation set first.", null);
            }
            catch (ArgumentException e)
            {
                return (e.Message, null);
            }
        }

        static bool SaveIfChanged(MetaDataEditorViewModel editor) =>
            editor.CurrentFile == null || editor.ParsedFile == null || !editor.HasUnsavedChanges || editor.Save();
    }

    public enum MetaDataEditTool
    {
        Select,
        Move,
        Rotate,
    }
}
