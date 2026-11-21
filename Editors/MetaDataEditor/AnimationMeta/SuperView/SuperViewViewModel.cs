using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.MetaEditor;
using Editors.AnimationMeta.MetaEditor.Check;
using Editors.AnimationMeta.Presentation;
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
        private readonly ApplicationSettingsService _settings;

        // Where each tab's file and the slot panel's typed meta come from: "From vanilla: <path>", "From your pack: <path>", or
        // another mod's pack name. A vanilla or other mod's meta saved at its own path overrides it for every bin that names it.
        [ObservableProperty] string _persistentMetaSource = "";
        [ObservableProperty] string _metaSource = "";
        [ObservableProperty] string _soundMetaSource = "";
        [ObservableProperty] string _soundMetaChangedOnDisk = "";
        [ObservableProperty] string _slotMetaSource = "";
        [ObservableProperty] bool _slotHasNoMeta;

        [ObservableProperty] string _persistentMetaFilePath = "";
        [ObservableProperty] string _metaFilePath = "";
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
            MetaTagGizmoComponent metaTagGizmo,
            ApplicationSettingsService settings,
            IAudioRepository audioRepository)
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
            Initialize();
            metaTagGizmo.Follow(() => SceneEditor(SelectedTabControllerIndex).SelectedTag,
                MetaTagGizmoComponent.FrameOf(metaDataFactory, _asset.Data));
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

        // The Sound tab's file was written, from outside SuperView or by a save of the tab's own. What the tab already shows is nothing
        // new, so the tab keeps its undo history. Otherwise a tab with no edits shows the file as written, and one with edits keeps them
        // and says so; the tab's own save then clears that.
        void OnSoundMetaWritten()
        {
            var file = _packFileService.FindFile(_soundMetaPath);
            if (file != null && SoundMetaEditor.ParsedFile is { } shown
                && file.DataSource.ReadData().AsSpan().SequenceEqual(_metaDataFileParser.GenerateBytes(SoundMetaEditor.MetaDataFileVersion, shown)))
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

        // The Sound tab's file in the scene, as a slot's play sets it, republished with the other two.
        void ShowSoundMeta(PackFile? file)
        {
            _asset.Data.SoundMetaData = file;
            _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, _asset.Data.PersistMetaData);
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

            _asset = assetViewModel;
            InitTimeline();
            SlotGrid = new SlotGridViewModel(_asset.FragAndSlotSelection, CannotPlay, x => SourceOf(Key(x)), Save, OfferToSaveSlotGrid, OpenFullGrid);
            SlotGrid.Changed += ShowSelectedRow;
            SlotGrid.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SlotGridViewModel.SelectedRow))
                    ShowSelectedRow();
            };

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
            var selected = SceneEditor(SelectedTabControllerIndex).SelectedAttribute;

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
            RebuildTimeline();
        }

        private void OnSceneObjectUpdated(SceneObjectUpdateEvent e)
        {
            if (_pointingRow)
                return;
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);

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

        public override void Close()
        {
            try
            {
                CloseSound();
            }
            finally
            {
                _eventHub?.UnRegister(this);
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
                : set != null && anim.Length != 0 && !_skeletonAnimationLookUpHelper.GetAnimationsForSkeleton(set.SkeletonName).Any(x => Key(x.AnimationFile) == anim)
                    ? $"{anim} is not an animation for {set.SkeletonName}, the set's skeleton."
                : null;
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
    }
}
