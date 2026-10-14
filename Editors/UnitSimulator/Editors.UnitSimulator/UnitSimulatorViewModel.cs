using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using Editors.UnitSimulator.Pairing;
using Editors.UnitSimulator.Parts;
using Editors.UnitSimulator.Personalities;
using GameWorld.Core.Components;
using Editors.UnitSimulator.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.UnitSimulator
{
    /// <summary>A rider on its mount, played the way the game pairs them: the mount's slot X plays the rider's RIDER_X, both from one
    /// origin on one clock. Both tables are picked by hand: WH3 does not read a table's MountBin
    /// (docs/superpowers/specs/2026-10-13-unit-simulator-personalities-design.md). docs/superpowers/specs/2026-10-13-unit-simulator-design.md.</summary>
    public partial class UnitSimulatorViewModel : EditorHostBase, ISaveableEditor
    {
        readonly IPackFileService _pfs;
        readonly IFileSaveService _fileSaver;
        readonly IEventHub _eventHub;
        readonly Dictionary<string, ClipLength?> _lengths = [];
        // Edited, unsaved tables by path. The instance here is the one saved, whatever a part's list holds.
        readonly Dictionary<string, AnimationBinWh3> _editedTables = [];
        readonly PersonalityPlacement? _placement;

        public override Type EditorViewModelType => typeof(Views.EditorView);

        public SceneObjectViewModel Mount { get; }
        public SceneObjectViewModel Rider { get; }
        /// <summary>The battle_personalities riders on the mount's attach bones.</summary>
        public PersonalityList Personalities { get; }
        public PartMeta MountMeta { get; }
        public PartMeta RiderMeta { get; }

        [ObservableProperty] ObservableCollection<PairRow> _pairs = [];
        [ObservableProperty] ObservableCollection<string> _riderOnlySlots = [];
        [ObservableProperty] ObservableCollection<string> _mountRiderTags = [];
        [ObservableProperty] PairRow? _selectedPair;

        public UnitSimulatorViewModel(
            IEditorHostParameters hostParameters,
            IServiceProvider services,
            IPackFileService pfs,
            IFileSaveService fileSaver,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            MetaDataFileParser metaDataFileParser,
            SceneManager sceneManager,
            AnimationsContainerComponent animations,
            IStandardDialogs dialogs)
            : base(hostParameters)
        {
            DisplayName = "Unit Simulator";
            _pfs = pfs;
            _fileSaver = fileSaver;
            _eventHub = eventHub;

            Mount = _sceneObjectViewModelBuilder.CreateAsset("Mount", true, "Mount", Color.Black, null);
            Rider = _sceneObjectViewModelBuilder.CreateAsset("Rider", true, "Rider", Color.Black, null);
            SceneObjects.Add(Mount);
            SceneObjects.Add(Rider);

            MountMeta = new PartMeta(Mount, ActivatorUtilities.CreateInstance<MetaDataBuilder>(services), uiCommandFactory, metaDataFileParser, eventHub);
            RiderMeta = new PartMeta(Rider, ActivatorUtilities.CreateInstance<MetaDataBuilder>(services), uiCommandFactory, metaDataFileParser, eventHub);
            MountMeta.UnsavedChanged += () => OnPropertyChanged(nameof(HasUnsavedChanges));
            RiderMeta.UnsavedChanged += () => OnPropertyChanged(nameof(HasUnsavedChanges));

            Rider.FragAndSlotSelection.FragmentList.SelectedItemChanged += OnRiderTableChanged;
            Mount.FragAndSlotSelection.FragmentList.SelectedItemChanged += _ => RefreshPairs();
            Mount.FragAndSlotSelection.FragmentSlotList.SelectedItemChanged += OnMountSlotChanged;
            Rider.FragAndSlotSelection.FragmentSlotList.SelectedItemChanged += _ => LoadSlotFields();
            Mount.FragAndSlotSelection.FragmentSlotList.SelectedItemChanged += _ => LoadSlotFields();
            // The part's BinAnimationViewModel subscribed first, so its table list is already rebuilt when these run; an edited table
            // goes back into the list.
            Rider.Data.SkeletonChanged += _ => KeepEditedTables(Rider);
            Mount.Data.SkeletonChanged += _ => KeepEditedTables(Mount);

            eventHub.Register<SceneObjectUpdateEvent>(this, e => PartOf(e.Owner)?.LoadAndRebuild());
            eventHub.Register<MetaDataAttributeChangedEvent>(this, _ => RebuildMetas());
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, _ => RebuildMetas());
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);

            // ponytail: a removed part stays in the animation player's list; AnimationPlayerViewModel has no unregister.
            Personalities = new PersonalityList(pfs, SceneObjectEditor, dialogs, Mount,
                (id, header) =>
                {
                    var part = _sceneObjectViewModelBuilder.CreateAsset(id, true, header, Color.Black, null);
                    SceneObjects.Add(part);
                    return part;
                },
                part =>
                {
                    SceneObjects.Remove(part);
                    GameWorld?.RemoveComponent(part.Data);
                    sceneManager.RootNode.RemoveObject(part.Data.ParentNode);
                    animations.Remove(part.Data.Player);
                    part.Dispose();
                });
            _placement = GameWorld?.AddComponent(new PersonalityPlacement(Personalities, hostParameters.RenderEngine));

            // The metas' props always play, so the models under them have to; off, a model stands in its bind pose.
            Player.IsEnabled.Value = true;
        }

        PartMeta? PartOf(SceneObject owner) => owner == Mount.Data ? MountMeta : owner == Rider.Data ? RiderMeta : null;

        void RebuildMetas()
        {
            MountMeta.Rebuild();
            RiderMeta.Rebuild();
        }

        void OnFileSaved(ScopedFileSavedEvent e)
        {
            MountMeta.OnSaved(e, _pfs, SceneObjectEditor);
            RiderMeta.OnSaved(e, _pfs, SceneObjectEditor);
        }

        void OnRiderTableChanged(IAnimationBinGenericFormat? _)
        {
            RefreshPairs();
            LoadSlotFields();
        }

        void OnMountSlotChanged(AnimationBinEntryGenericFormat? mountSlot)
        {
            if (mountSlot == null)
                return;
            var riderSlots = Rider.FragAndSlotSelection.FragmentSlotList;
            var riderSlot = riderSlots.PossibleValues.FirstOrDefault(x => x.SlotName == RiderPairing.RiderSlotFor(mountSlot.SlotName));
            if (riderSlot != null)
                riderSlots.SelectedItem = riderSlot;

            // Both from their first frame, as the game starts a pair together.
            Mount.Data.Player.SetTime(0);
            Rider.Data.Player.SetTime(0);
            MountRiderTags = new(RiderTags(MountMeta.Slot.ParsedFile));
        }

        partial void OnSelectedPairChanged(PairRow? value)
        {
            if (value == null)
                return;
            var slots = Mount.FragAndSlotSelection.FragmentSlotList;
            slots.SelectedItem = slots.PossibleValues.FirstOrDefault(x => x.SlotName == value.MountSlot);
        }

        protected void RefreshPairs()
        {
            var mount = Mount.FragAndSlotSelection.FragmentList.SelectedItem;
            var rider = Rider.FragAndSlotSelection.FragmentList.SelectedItem;
            var both = mount != null && rider != null;
            Pairs = new(both ? RiderPairing.Pair(mount!, rider!, LengthOf) : []);
            RiderOnlySlots = new(both ? RiderPairing.RiderOnly(mount!, rider!) : []);
        }

        // ponytail: lengths are kept for the tab's life; an .anim replaced while it is open shows its old length until the tab is reopened.
        ClipLength? LengthOf(string path)
        {
            if (_lengths.TryGetValue(path, out var known))
                return known;
            ClipLength? length = null;
            if (_pfs.FindFile(path) is { } file)
            {
                var anim = AnimationFile.Create(file);
                length = new ClipLength(anim.AnimationParts.FirstOrDefault()?.DynamicFrames.Count ?? 0, anim.Header.AnimationTotalPlayTimeInSec);
            }
            return _lengths[path] = length;
        }

        // The mount slot's RIDER_* tags. Only RIDER_IDLE_SPEED_SCALE has a decoded field, and AnimationPlayer has no rate to apply it
        // with, so none is acted out (spec section 4).
        static IEnumerable<string> RiderTags(ParsedMetadataFile? meta) =>
            (meta?.Attributes ?? []).Where(x => x.Name.StartsWith("RIDER_", StringComparison.Ordinal)).Select(x => x is RiderIdleSpeedScale scale
                ? $"{x.Name} {scale.AnimationSpeedScale}: not applied, the preview cannot change one part's speed"
                : $"{x.Name}: listed, its fields are not decoded");

        public string[] PartNames { get; } = ["Rider", "Mount"];
        [ObservableProperty] string _editPart = "Rider";
        [ObservableProperty] string _slotAnim = "";
        [ObservableProperty] string _slotMeta = "";
        [ObservableProperty] string _slotSound = "";
        [ObservableProperty] float _slotBlend;
        [ObservableProperty] float _slotWeight;
        [ObservableProperty] string _tableStatus = "";

        SceneObjectViewModel EditedPart => EditPart == "Mount" ? Mount : Rider;

        partial void OnEditPartChanged(string value) => LoadSlotFields();

        void LoadSlotFields()
        {
            var slot = EditedPart.FragAndSlotSelection.FragmentSlotList.SelectedItem;
            SlotAnim = slot?.AnimationFile ?? "";
            SlotMeta = slot?.MetaFile ?? "";
            SlotSound = slot?.SoundFile ?? "";
            SlotBlend = slot?.BlendInTime ?? 0;
            SlotWeight = slot?.SelectionWeight ?? 0;
        }

        [RelayCommand]
        void ApplySlotEdit()
        {
            var part = EditedPart;
            if (part.FragAndSlotSelection.FragmentList.SelectedItem is not AnimationBinWh3 table || part.FragAndSlotSelection.FragmentSlotList.SelectedItem is not { } slot)
            {
                TableStatus = $"Pick a table and a slot on the {EditPart} first.";
                return;
            }
            AnimTableEdits.SetFiles(table, slot, SlotAnim, SlotMeta, SlotSound);
            AnimTableEdits.SetBlendAndWeight(table, slot.SlotIndex, SlotBlend, SlotWeight);
            Edited(part, table);
        }

        [RelayCommand]
        void AddMissingRiderSlot()
        {
            if (SelectedPair is not { Status: PairStatus.RiderSlotMissing } row)
            {
                TableStatus = "Select a Pairing row whose rider slot is missing.";
                return;
            }
            if (Rider.FragAndSlotSelection.FragmentList.SelectedItem is not AnimationBinWh3 table || Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem is not { } current)
            {
                TableStatus = "Pick the rider slot to copy on the Rider panel first.";
                return;
            }
            AnimTableEdits.AddSlot(table, row.RiderSlot, AnimTableEdits.Row(table, current.SlotIndex));
            Edited(Rider, table);
        }

        [RelayCommand]
        void SaveTables() => SaveEditedTables();

        bool SaveEditedTables()
        {
            var problems = new List<string>();
            foreach (var table in _editedTables.Values.ToList())
            {
                if (AnimTableEdits.Save(_pfs, _fileSaver, table) is { } problem)
                    problems.Add(problem);
                else
                    _editedTables.Remove(table.FileName);
            }
            TableStatus = problems.Count == 0 ? "Tables saved." : string.Join(Environment.NewLine, problems);
            OnPropertyChanged(nameof(HasUnsavedChanges));
            return problems.Count == 0;
        }

        void Edited(SceneObjectViewModel part, AnimationBinWh3 table)
        {
            _editedTables[table.FileName] = table;
            Reload(part);
            RefreshPairs();
            TableStatus = $"{table.Name} edited, not saved.";
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        // The slot list is built from the table's rows when the table is picked, so it is picked again to show the edit, and so is the slot.
        static void Reload(SceneObjectViewModel part)
        {
            var tables = part.FragAndSlotSelection.FragmentList;
            var slots = part.FragAndSlotSelection.FragmentSlotList;
            var table = tables.SelectedItem;
            var slotName = slots.SelectedItem?.SlotName;
            tables.SelectedItem = null;
            tables.SelectedItem = table;
            slots.SelectedItem = slots.PossibleValues.FirstOrDefault(x => x.SlotName == slotName);
        }

        // A part's table list is read from the packs again when its skeleton changes, a copy per pack holding the animpack. One copy of
        // each is kept, the saved one if any, and an edited, unsaved table takes its place: the next edit has to go to the copy the last
        // save wrote, or that save is overwritten by the next.
        void KeepEditedTables(SceneObjectViewModel part)
        {
            var tables = part.FragAndSlotSelection.FragmentList;
            tables.UpdatePossibleValues(AnimTableEdits.OneCopyEach(_pfs, tables.PossibleValues)
                .Select(x => _editedTables.TryGetValue(x.FullPath, out var edited) ? edited : x).ToList());
        }

        public bool HasUnsavedChanges
        {
            get => MountMeta.HasUnsavedChanges || RiderMeta.HasUnsavedChanges || _editedTables.Count != 0;
            set
            {
                MountMeta.HasUnsavedChanges = value;
                RiderMeta.HasUnsavedChanges = value;
                if (!value)
                    _editedTables.Clear();
            }
        }

        public bool Save()
        {
            var metas = MountMeta.Save() & RiderMeta.Save();
            return SaveEditedTables() && metas;
        }

        public override void Close()
        {
            _eventHub.UnRegister(this);
            MountMeta.Dispose();
            RiderMeta.Dispose();
            if (_placement != null)
                GameWorld?.RemoveComponent(_placement);
            base.Close();
        }
    }
}
