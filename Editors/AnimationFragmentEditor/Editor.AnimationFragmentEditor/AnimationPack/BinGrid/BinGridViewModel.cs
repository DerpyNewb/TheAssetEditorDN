using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    /// <summary>A WH3 battle table as a grid, one row per variant. Edits go through the animpack editor's EditTable, so each is one
    /// undo step and the XML view shows the same bytes.</summary>
    public partial class BinGridViewModel : ObservableObject
    {
        static readonly BaseAnimationSlotHelper Slots = AnimationSlotTypeHelperWh3.GetInstance();

        readonly Action<AnimationBinWh3, Action<AnimationBinWh3>> _edit;
        readonly BinTableChecks _checks;
        readonly IStandardDialogs _dialogs;
        readonly IUiCommandFactory _commands;
        readonly IPackFileService _pfs;
        readonly ISkeletonAnimationLookUpHelper _skeletons;
        readonly IReadOnlyList<string> _otherBins;
        readonly Func<IReadOnlyCollection<string>, string?> _pickSlot;

        public AnimationBinWh3 Table { get; }

        [ObservableProperty] ObservableCollection<BinGridRow> _rows = [];
        [ObservableProperty] IList<BinGridRow> _selectedRows = new List<BinGridRow>();
        [ObservableProperty] string _status = "";

        public BinGridViewModel(AnimationBinWh3 table, Action<AnimationBinWh3, Action<AnimationBinWh3>> edit, BinTableChecks checks, IStandardDialogs dialogs,
            IUiCommandFactory commands, IPackFileService pfs, ISkeletonAnimationLookUpHelper skeletons, IReadOnlyList<string> otherBins, Func<IReadOnlyCollection<string>, string?> pickSlot)
        {
            Table = table;
            _edit = edit;
            _checks = checks;
            _dialogs = dialogs;
            _commands = commands;
            _pfs = pfs;
            _skeletons = skeletons;
            _otherBins = otherBins;
            _pickSlot = pickSlot;
            Reload();
        }

        // The header strip

        public string Name { get => Table.Name; set => EditHeader(x => x.Name = value ?? ""); }
        public string Skeleton { get => Table.SkeletonName; set => EditHeader(x => x.SkeletonName = value ?? ""); }
        public string MountBin { get => Table.MountBin; set => EditHeader(x => x.MountBin = value ?? ""); }
        public string LocomotionGraph { get => Table.LocomotionGraph; set => EditHeader(x => x.LocomotionGraph = value ?? ""); }

        /// <summary>The header's unnamed string, empty on all 2,830 CA tables (2026-11-12). One comment calls it the rider table's name.</summary>
        public string UnknownText { get => Table.Unknown; set => EditHeader(x => x.Unknown = value ?? ""); }

        // UnknownValue1 only ever has two bits set across CA's 2,830 tables (2026-11-12): bit 0 on 13 fliers (bats, crows, vultures,
        // sky junk, the pegasus master) and bit 8 on 375 riders, every one with a mount. What either does is unconfirmed.
        public bool SimpleFlight { get => (Table.UnknownValue1 & 1) != 0; set => EditHeader(x => x.UnknownValue1 = Flag(x.UnknownValue1, 1, value)); }
        public bool RiderFlag { get => (Table.UnknownValue1 & 256) != 0; set => EditHeader(x => x.UnknownValue1 = Flag(x.UnknownValue1, 256, value)); }
        static short Flag(short bits, int flag, bool on) => (short)(on ? bits | flag : bits & ~flag);

        public string VersionText => $"{Table.TableVersion} / {Table.TableSubVersion}";

        public IReadOnlyList<string> SlotNames { get; } = Slots.Values.Select(x => x.Value).ToList();
        public IReadOnlyList<string> MountBins => _otherBins;

        IReadOnlyList<string>? _skeletonNames;
        public IReadOnlyList<string> Skeletons => _skeletonNames ??= _skeletons.GetAllSkeletonFileNames().Select(x => Path.GetFileNameWithoutExtension(x)).Distinct().Order().ToList();

        // Edits

        void EditHeader(Action<AnimationBinWh3> change)
        {
            _edit(Table, change);
            Refresh();
        }

        /// <summary>A change to a row's slot. With several rows selected and this one among them, every selected slot takes it, as
        /// one undo step.</summary>
        public void EditSlots(BinGridRow row, Action<AnimationBinEntry> change)
        {
            var rows = SelectedRows.Count > 1 && SelectedRows.Contains(row) ? SelectedRows : [row];
            var entries = rows.Select(x => x.Entry).Distinct().ToList();
            _edit(Table, table => entries.ForEach(x => change(table.AnimationTableEntries[x])));
            Refresh();
        }

        /// <summary>A change to a row's variant. A slot with none gets one first.</summary>
        public void EditVariant(BinGridRow row, Action<AnimationBinEntry.AnimationRef> change)
        {
            if (row.Variant >= 0)
            {
                _edit(Table, table => change(table.AnimationTableEntries[row.Entry].AnimationRefs[row.Variant]));
                Refresh();
                return;
            }
            _edit(Table, table =>
            {
                var added = EmptyVariant();
                change(added);
                table.AnimationTableEntries[row.Entry].AnimationRefs.Add(added);
            });
            Reload();
        }

        public void SetSlot(BinGridRow row, string name)
        {
            var id = BinXml.SlotId(Slots, name ?? "");
            if (id == null)
                Status = $"{name} is not a WH3 animation slot.";
            else if (Table.AnimationTableEntries.Where((_, i) => i != row.Entry).Any(x => x.AnimationId == (uint)id))
                Status = $"This table already has {name}.";
            else
                _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationId = (uint)id);
            Refresh();
        }

        static AnimationBinEntry.AnimationRef EmptyVariant() => new() { AnimationFile = "", AnimationMetaFile = "", AnimationSoundMetaFile = "" };

        [RelayCommand]
        void AddVariant(BinGridRow? row)
        {
            if (row == null)
                return;
            _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationRefs.Add(EmptyVariant()));
            Reload();
        }

        [RelayCommand]
        void RemoveVariant(BinGridRow? row)
        {
            if (row is not { Variant: >= 0 })
                return;
            _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationRefs.RemoveAt(row.Variant));
            Reload();
        }

        [RelayCommand]
        void MatchFileName() => Name = Path.GetFileNameWithoutExtension(Table.FileName);

        // Rows

        /// <summary>Builds the rows anew from the table: after a change to which variants exist, or an undo. The selection is kept by
        /// slot and variant where those still exist.</summary>
        public void Reload()
        {
            var selected = SelectedRows.Select(x => (x.Entry, x.Variant)).ToHashSet();
            var rows = new ObservableCollection<BinGridRow>();
            for (var entry = 0; entry < Table.AnimationTableEntries.Count; entry++)
            {
                var variants = Table.AnimationTableEntries[entry].AnimationRefs.Count;
                if (variants == 0)
                    rows.Add(new BinGridRow(this, entry, -1));
                for (var variant = 0; variant < variants; variant++)
                    rows.Add(new BinGridRow(this, entry, variant));
            }
            Rows = rows;
            SelectedRows = rows.Where(x => selected.Contains((x.Entry, x.Variant))).ToList();
            OnPropertyChanged(string.Empty);
        }

        /// <summary>Every cell and header field read again from the table, keeping the same rows.</summary>
        public void Refresh()
        {
            OnPropertyChanged(string.Empty);
            foreach (var row in Rows)
                row.Refresh();
        }
    }
}
