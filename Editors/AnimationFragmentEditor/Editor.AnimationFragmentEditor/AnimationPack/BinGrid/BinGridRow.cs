using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    /// <summary>One row of the grid: one variant of a slot, or a slot with none (Variant -1). It holds no values of its own and reads
    /// the table each time, so an edit anywhere shows here on the next PropertyChanged.</summary>
    public partial class BinGridRow : ObservableObject
    {
        readonly BinGridViewModel _owner;

        public int Entry { get; }
        public int Variant { get; }

        public BinGridRow(BinGridViewModel owner, int entry, int variant)
        {
            _owner = owner;
            Entry = entry;
            Variant = variant;
        }

        AnimationBinEntry Row => _owner.Table.AnimationTableEntries[Entry];
        AnimationBinEntry.AnimationRef? Ref => Variant >= 0 ? Row.AnimationRefs[Variant] : null;

        /// <summary>The slot's cells (slot, weapons, blend, weight, Unk) are edited on its first row; later variants show them greyed.</summary>
        public bool IsFirst => Variant <= 0;

        public string Slot
        {
            get => BinXml.SlotName(AnimationSlotTypeHelperWh3.GetInstance(), Row.AnimationId);
            set => _owner.SetSlot(this, value);
        }

        public string Animation { get => Ref?.AnimationFile ?? ""; set => _owner.EditVariant(this, x => x.AnimationFile = value ?? ""); }
        public string Meta { get => Ref?.AnimationMetaFile ?? ""; set => _owner.EditVariant(this, x => x.AnimationMetaFile = value ?? ""); }
        public string Sound { get => Ref?.AnimationSoundMetaFile ?? ""; set => _owner.EditVariant(this, x => x.AnimationSoundMetaFile = value ?? ""); }

        // Bit N of WeaponBools shows be_prop_N. Each box sets its own bit and keeps the others, past the sixth included.
        public bool Weapon0 { get => Bit(0); set => SetBit(0, value); }
        public bool Weapon1 { get => Bit(1); set => SetBit(1, value); }
        public bool Weapon2 { get => Bit(2); set => SetBit(2, value); }
        public bool Weapon3 { get => Bit(3); set => SetBit(3, value); }
        public bool Weapon4 { get => Bit(4); set => SetBit(4, value); }
        public bool Weapon5 { get => Bit(5); set => SetBit(5, value); }

        bool Bit(int bit) => (Row.WeaponBools & (1 << bit)) != 0;
        void SetBit(int bit, bool on) => _owner.EditSlots(this, x => x.WeaponBools = on ? x.WeaponBools | (1 << bit) : x.WeaponBools & ~(1 << bit));

        public bool Unk { get => Row.Unk; set => _owner.EditSlots(this, x => x.Unk = value); }

        // A number cell holds what was typed while it does not parse, marked, and writes nothing until it does.
        string? _blendText;
        string? _weightText;
        public bool BlendInvalid => _blendText != null;
        public bool WeightInvalid => _weightText != null;

        public string BlendIn
        {
            get => _blendText ?? Row.BlendIn.ToString(CultureInfo.InvariantCulture);
            set => SetNumber(value, ref _blendText, (x, v) => x.BlendIn = v, nameof(BlendInvalid));
        }

        public string Weight
        {
            get => _weightText ?? Row.SelectionWeight.ToString(CultureInfo.InvariantCulture);
            set => SetNumber(value, ref _weightText, (x, v) => x.SelectionWeight = v, nameof(WeightInvalid));
        }

        void SetNumber(string text, ref string? held, Action<AnimationBinEntry, float> write, string invalidName)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                held = null;
                _owner.EditSlots(this, x => write(x, value));
            }
            else
            {
                held = text;
            }
            OnPropertyChanged(invalidName);
            Refresh();
        }

        // What the checks found on this row's slot, shown as a mark on the row with the messages as its tooltip.
        IReadOnlyList<BinFinding> _findings = [];
        public string? Findings => _findings.Count == 0 ? null : string.Join("\n", _findings.Select(x => x.Message).Distinct());
        public bool HasError => _findings.Any(x => x.IsError);
        public bool HasWarning => _findings.Count != 0 && !HasError;

        internal void SetFindings(IReadOnlyList<BinFinding> findings)
        {
            _findings = findings;
            OnPropertyChanged(nameof(Findings));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasWarning));
        }

        /// <summary>Every cell read again from the table.</summary>
        public void Refresh() => OnPropertyChanged(string.Empty);
    }
}
