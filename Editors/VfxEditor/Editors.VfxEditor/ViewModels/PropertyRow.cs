using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.VfxEditor.Document;

namespace Editors.VfxEditor.ViewModels
{
    public enum RowKind { Bool, Choice, Number, Text, Curve }

    /// <summary>One editable field: an attribute of a property element, or its text (a curve) when Attribute is null; Property
    /// is empty on a plain element's attributes. A number that does not parse shows as invalid and is not written.</summary>
    public sealed partial class PropertyRow : ObservableObject
    {
        readonly Action<PropertyRow, string> _commit;
        string _value;

        public PropertyRow(string property, string? attribute, string label, RowKind kind, string value, IReadOnlyList<string>? choices,
            string note, Action<PropertyRow, string> commit, Action<string> report)
        {
            Property = property;
            Attribute = attribute;
            Label = label;
            Kind = kind;
            Choices = choices;
            Note = note;
            _value = value;
            _commit = commit;
            if (kind == RowKind.Curve && CurveText.Parse(value) is { } curve)
                Curve = new CurveEditor(curve, text => commit(this, text), report);
        }

        public string Property { get; }
        public string? Attribute { get; }
        public string Label { get; }
        public RowKind Kind { get; }
        public IReadOnlyList<string>? Choices { get; }
        public string Note { get; private set; }
        public CurveEditor? Curve { get; }
        public string Key => Property + "/" + Attribute;
        public bool IsBool => Kind == RowKind.Bool;
        public bool IsChoice => Kind == RowKind.Choice;
        public bool IsTextual => Kind is RowKind.Number or RowKind.Text;
        public bool CanRemove => Property.Length > 0;

        [ObservableProperty] bool _isInvalid;

        public string Value
        {
            get => _value;
            set
            {
                if (value == _value)
                    return;
                if (Kind == RowKind.Number)
                {
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number))
                    {
                        IsInvalid = true;
                        return;
                    }
                    value = value.Trim();
                }
                IsInvalid = false;
                _commit(this, value);
            }
        }

        public bool BoolValue
        {
            get => string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase);
            set => Value = value ? "true" : "false";
        }

        /// <summary>The document changed: show what it now says, in place, so a box being typed in keeps its focus.</summary>
        internal void Refresh(PropertyRow fresh)
        {
            _value = fresh._value;
            Note = fresh.Note;
            IsInvalid = false;
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(Note));
            Curve?.Reload(_value);
        }
    }
}
