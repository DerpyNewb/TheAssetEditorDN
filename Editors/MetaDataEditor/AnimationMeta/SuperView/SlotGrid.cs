using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Shared.Core.Common.ReferenceModel;
using Shared.Core.Misc;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>What a slot plays: its three files, and the blend, weight and weapon bones of its row in a battle table.</summary>
    public sealed record SlotValues(string Animation, string Meta, string Sound, float BlendIn, float Weight, int Weapons)
    {
        public static SlotValues Of(AnimationBinEntryGenericFormat slot) =>
            new(slot.AnimationFile ?? "", slot.MetaFile ?? "", slot.SoundFile ?? "", slot.BlendInTime, slot.SelectionWeight, slot.WeaponBools);
    }

    /// <summary>One row of the slot grid: a slot's variant as its set names it (<see cref="Slot"/>) and as edited (<see cref="Values"/>).
    /// A cell's edit goes through the grid, which shares the slot's blend, weight and weapons with its other variants.</summary>
    public sealed partial class SlotGridRow(AnimationBinEntryGenericFormat slot, Action<SlotGridRow, SlotValues> edit) : ObservableObject
    {
        readonly SlotValues _saved = SlotValues.Of(slot);
        SlotValues _values = SlotValues.Of(slot);

        public AnimationBinEntryGenericFormat Slot => slot;
        public string Name => slot.DisplayName;
        [ObservableProperty] string _metaSource = "";

        public SlotValues Values
        {
            get => _values;
            set
            {
                if (_values == value)
                    return;
                _values = value;
                OnPropertyChanged(string.Empty);
            }
        }

        public bool IsEdited => _values != _saved;
        public SlotValues Saved => _saved;

        public string Animation { get => _values.Animation; set => edit(this, _values with { Animation = value ?? "" }); }
        public string Meta { get => _values.Meta; set => edit(this, _values with { Meta = value ?? "" }); }
        public string Sound { get => _values.Sound; set => edit(this, _values with { Sound = value ?? "" }); }
        public float BlendIn { get => _values.BlendIn; set => edit(this, _values with { BlendIn = value }); }
        public float Weight { get => _values.Weight; set => edit(this, _values with { Weight = value }); }

        // Bit N of the weapons shows be_prop_N, as in the full bin grid.
        public bool Weapon0 { get => Bit(0); set => SetBit(0, value); }
        public bool Weapon1 { get => Bit(1); set => SetBit(1, value); }
        public bool Weapon2 { get => Bit(2); set => SetBit(2, value); }
        public bool Weapon3 { get => Bit(3); set => SetBit(3, value); }
        public bool Weapon4 { get => Bit(4); set => SetBit(4, value); }
        public bool Weapon5 { get => Bit(5); set => SetBit(5, value); }

        bool Bit(int bit) => (_values.Weapons & (1 << bit)) != 0;
        void SetBit(int bit, bool on) => edit(this, _values with { Weapons = on ? _values.Weapons | (1 << bit) : _values.Weapons & ~(1 << bit) });
    }

    /// <summary>SuperView's slot grid (spec 2026-11-15): every slot of the picked set as a row. Picking a row picks its slot, as the
    /// Scene panel's slot list does, and the two follow each other. An edit plays at once and stays unsaved, with undo, until
    /// SuperView saves the set; the header, adding and removing slots stay in the full bin grid.</summary>
    public sealed partial class SlotGridViewModel : ObservableObject
    {
        readonly BinAnimationViewModel _selection;
        readonly Func<SlotGridRow, string?> _cannotPlay;
        readonly Func<string, string> _metaSource;
        readonly Func<bool> _save;
        readonly Action _openFullGrid;
        readonly SnapshotUndo<List<SlotValues>> _history;
        bool _following;

        /// <param name="cannotPlay">Why a row's files cannot play (a file in no loaded pack, an animation for another skeleton), or null.</param>
        /// <param name="metaSource">Where a meta path comes from, for its cell.</param>
        /// <param name="save">SuperView's save, which writes the set.</param>
        /// <param name="beforeLeaving">Called with the set when another is about to be picked while this one has unsaved edits.</param>
        /// <param name="openFullGrid">Opens the set in its own editor, for what the grid leaves out.</param>
        public SlotGridViewModel(BinAnimationViewModel selection, Func<SlotGridRow, string?> cannotPlay, Func<string, string> metaSource, Func<bool> save,
            Action<IAnimationBinGenericFormat> beforeLeaving, Action openFullGrid)
        {
            _openFullGrid = openFullGrid;
            _selection = selection;
            _cannotPlay = cannotPlay;
            _metaSource = metaSource;
            _save = save;
            _history = new(() => Rows.Select(x => x.Values).ToList(), (a, b) => a.SequenceEqual(b), (arriving, _) => Restore(arriving));
            selection.FragmentList.BeforeSelectedItemChanged += next =>
            {
                if (Set != null && next != Set && HasUnsavedChanges)
                    beforeLeaving(Set);
                return true;
            };
            selection.FragmentList.SelectedItemChanged += _ => Load();
            selection.FragmentSlotList.SelectedItemChanged += Follow;
            Load();
        }

        public ObservableCollection<SlotGridRow> Rows { get; } = [];
        public IAnimationBinGenericFormat? Set { get; private set; }
        [ObservableProperty] SlotGridRow? _selectedRow;
        [ObservableProperty] string _status = "";

        /// <summary>Blend, weight and weapons are a battle table's; a campaign set's rows hold files only.</summary>
        public bool IsBattleTable => Set is AnimationBinWh3;
        public bool HasUnsavedChanges => Rows.Any(x => x.IsEdited);
        public bool CanUndo => _history.CanUndo;
        public bool CanRedo => _history.CanRedo;

        /// <summary>Raised on every change to a row's values, an edit, an undo or a load.</summary>
        public event Action? Changed;

        /// <summary>The picked set's slots, as saved, with nothing to undo.</summary>
        public void Load()
        {
            Set = _selection.FragmentList.SelectedItem;
            Rows.Clear();
            foreach (var slot in _selection.FragmentSlotList.PossibleValues ?? [])
                Rows.Add(new SlotGridRow(slot, Edit) { MetaSource = _metaSource(slot.MetaFile ?? "") });
            _history.Reset();
            Status = "";
            Follow(_selection.FragmentSlotList.SelectedItem);
            OnPropertyChanged(nameof(IsBattleTable));
            Notify();
        }

        /// <summary>Drops every unsaved edit, as when the set is written or the tab closes without saving.</summary>
        public void Discard()
        {
            foreach (var row in Rows)
                row.Values = row.Saved;
            _history.Reset();
            Notify();
        }

        public SlotGridRow? RowOf(AnimationBinEntryGenericFormat? slot) => Rows.FirstOrDefault(x => x.Slot == slot);

        void Follow(AnimationBinEntryGenericFormat? slot)
        {
            _following = true;
            SelectedRow = RowOf(slot);
            _following = false;
            // The slot list played what the set names; an edited row plays its edit instead.
            if (SelectedRow is { IsEdited: true } row)
                Play(row);
        }

        partial void OnSelectedRowChanged(SlotGridRow? value)
        {
            if (_following || value == null)
                return;
            if (_selection.FragmentSlotList.SelectedItem != value.Slot)
                _selection.FragmentSlotList.SelectedItem = value.Slot;    // follows back through Follow, which plays an edited row
        }

        void Edit(SlotGridRow row, SlotValues values)
        {
            if (!IsBattleTable && (values.BlendIn, values.Weight, values.Weapons) != (row.Values.BlendIn, row.Values.Weight, row.Values.Weapons))
            {
                Status = "A campaign set's rows hold files only: no blend, weight or weapons.";
                values = values with { BlendIn = row.Values.BlendIn, Weight = row.Values.Weight, Weapons = row.Values.Weapons };
            }
            if (values == row.Values)
                return;
            row.Values = values;
            // Blend, weight and weapons are the slot's, so every variant of it shows the same.
            if (IsBattleTable)
            {
                foreach (var other in Rows.Where(x => x != row && x.Slot.SlotIndex == row.Slot.SlotIndex))
                    other.Values = other.Values with { BlendIn = values.BlendIn, Weight = values.Weight, Weapons = values.Weapons };
            }
            row.MetaSource = _metaSource(values.Meta);
            _history.Checkpoint();
            Notify();
            if (row == SelectedRow)
                Play(row);
        }

        /// <summary>Plays a row's values in its slot, saving nothing. A row that cannot play says why and the scene keeps what it played.</summary>
        void Play(SlotGridRow row)
        {
            var problem = _cannotPlay(row);
            Status = problem ?? "";
            if (problem != null)
                return;
            var (v, s) = (row.Values, row.Slot);
            _selection.PlaySlot(new AnimationBinEntryGenericFormat
            {
                Index = s.Index, SlotIndex = s.SlotIndex, SlotName = s.SlotName, BlendInTime = v.BlendIn, SelectionWeight = v.Weight, WeaponBools = v.Weapons,
                AnimationFile = Key(v.Animation), MetaFile = Key(v.Meta), SoundFile = Key(v.Sound),
            });
        }

        // An undo shows the row it changed, picked and playing.
        void Restore(List<SlotValues> arriving)
        {
            var changed = Rows.Select((row, i) => (row, i)).FirstOrDefault(x => x.i < arriving.Count && x.row.Values != arriving[x.i]).row;
            for (var i = 0; i < Rows.Count && i < arriving.Count; i++)
            {
                Rows[i].Values = arriving[i];
                Rows[i].MetaSource = _metaSource(arriving[i].Meta);
            }
            Notify();
            if (changed == null)
                return;
            if (changed == SelectedRow)
                PlayAsSaved(changed);
            else
                SelectedRow = changed;
        }

        // An unedited row plays what its set names, as picking its slot does.
        void PlayAsSaved(SlotGridRow row)
        {
            if (row.IsEdited)
                Play(row);
            else
            {
                Status = "";
                _selection.PlaySlot(row.Slot);
            }
        }

        [RelayCommand]
        void Undo()
        {
            if (!_history.Undo())
                Status = "No slot edit to undo.";
        }

        [RelayCommand]
        void Redo()
        {
            if (!_history.Redo())
                Status = "No slot edit to redo.";
        }

        [RelayCommand]
        void Save() => _save();

        [RelayCommand]
        void OpenFullGrid() => _openFullGrid();

        void Notify()
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            Changed?.Invoke();
        }

        internal static string Key(string path) => path.Trim().Replace('\\', '/').ToLowerInvariant();
    }
}
