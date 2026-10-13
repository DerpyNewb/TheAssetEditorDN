using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using Editors.UnitSimulator.Pairing;
using Editors.UnitSimulator.Parts;
using Editors.UnitSimulator.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;

namespace Editors.UnitSimulator
{
    /// <summary>A rider on its mount, played the way the game pairs them: the rider's table names its mount's (MountBin), the mount's
    /// slot X plays the rider's RIDER_X, and both play from one origin on one clock. docs/superpowers/specs/2026-10-13-unit-simulator-design.md.</summary>
    public partial class UnitSimulatorViewModel : EditorHostBase, ISaveableEditor
    {
        readonly IPackFileService _pfs;
        readonly IEventHub _eventHub;
        readonly Dictionary<string, ClipLength?> _lengths = [];
        IAnimationBinGenericFormat? _wantedMountTable;

        public override Type EditorViewModelType => typeof(Views.EditorView);

        public SceneObjectViewModel Mount { get; }
        public SceneObjectViewModel Rider { get; }
        public PartMeta MountMeta { get; }
        public PartMeta RiderMeta { get; }

        [ObservableProperty] string _linkStatus = "";
        [ObservableProperty] ObservableCollection<PairRow> _pairs = [];
        [ObservableProperty] ObservableCollection<string> _riderOnlySlots = [];
        [ObservableProperty] ObservableCollection<string> _mountRiderTags = [];
        [ObservableProperty] PairRow? _selectedPair;

        public UnitSimulatorViewModel(
            IEditorHostParameters hostParameters,
            IServiceProvider services,
            IPackFileService pfs,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            MetaDataFileParser metaDataFileParser)
            : base(hostParameters)
        {
            DisplayName = "Unit Simulator";
            _pfs = pfs;
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
            // The part's BinAnimationViewModel subscribed first, so its table list is already rebuilt when this runs.
            Mount.Data.SkeletonChanged += _ => SelectMountTable();

            eventHub.Register<SceneObjectUpdateEvent>(this, e => PartOf(e.Owner)?.LoadAndRebuild());
            eventHub.Register<MetaDataAttributeChangedEvent>(this, _ => RebuildMetas());
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, _ => RebuildMetas());
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);

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

        protected void OnRiderTableChanged(IAnimationBinGenericFormat? riderTable)
        {
            _wantedMountTable = null;
            LinkStatus = riderTable == null ? "" : LinkMount(riderTable);
            RefreshPairs();
        }

        // The rider's table names its mount's. The mount is given that table's skeleton when it has another or none, so a rider can be
        // looked at before any mount model is picked; the model can be picked after. Empty when linked, else why not.
        string LinkMount(IAnimationBinGenericFormat riderTable)
        {
            if (string.IsNullOrEmpty(riderTable.MountBin))
                return $"{riderTable.Name} names no mount: its MountBin is empty.";
            if (AnimTableEdits.FindByName(_pfs, riderTable.MountBin) is not { } mountTable)
                return $"No loaded animpack has the mount table {riderTable.MountBin}.";

            var needsSkeleton = Path.GetFileNameWithoutExtension(Mount.Data.SkeletonName.Value) != mountTable.SkeletonName;
            var skeleton = needsSkeleton ? _pfs.FindFile($@"animations\skeletons\{mountTable.SkeletonName}.anim") : null;
            if (needsSkeleton && skeleton == null)
                return $"The mount table {mountTable.Name} uses the skeleton {mountTable.SkeletonName}, which no loaded pack has.";

            // Set first: the new skeleton's SkeletonChanged picks the table once the part's list is read for it.
            _wantedMountTable = mountTable;
            if (skeleton != null)
                SceneObjectEditor.SetSkeleton(Mount.Data, skeleton);
            SelectMountTable();
            return "";
        }

        // A part's table list is read again whenever its skeleton changes (a model picked after the link, say), and that clears it. The
        // rider's MountBin still names the table, so it is picked again.
        protected void SelectMountTable()
        {
            if (_wantedMountTable == null)
                return;
            var tables = Mount.FragAndSlotSelection.FragmentList;
            var match = tables.PossibleValues.FirstOrDefault(x => x.FullPath == _wantedMountTable.FullPath);
            if (match != null && tables.SelectedItem != match)
                tables.SelectedItem = match;
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

        public bool HasUnsavedChanges
        {
            get => MountMeta.HasUnsavedChanges || RiderMeta.HasUnsavedChanges;
            set
            {
                MountMeta.HasUnsavedChanges = value;
                RiderMeta.HasUnsavedChanges = value;
            }
        }

        public bool Save() => MountMeta.Save() & RiderMeta.Save();

        public override void Close()
        {
            _eventHub.UnRegister(this);
            MountMeta.Dispose();
            RiderMeta.Dispose();
            base.Close();
        }
    }
}
