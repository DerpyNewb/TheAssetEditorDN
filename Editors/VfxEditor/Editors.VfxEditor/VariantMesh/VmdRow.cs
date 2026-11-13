using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.VfxEditor.Document;

namespace Editors.VfxEditor.VariantMesh
{
    /// <summary>One grid row. It holds no values of its own and reads the document each time, so an edit anywhere shows here on the
    /// next PropertyChanged.</summary>
    public sealed partial class VmdRow : ObservableObject
    {
        readonly VmdEditorViewModel _owner;

        internal VmdRow(VmdEditorViewModel owner, VmdRowRef reference)
        {
            _owner = owner;
            Ref = reference;
        }

        internal VmdRowRef Ref { get; set; }
        VfxDocument Document => _owner.Document!;
        internal SpanElement SlotElement => Document.At(Ref.Slot);
        internal SpanElement? Entry => VmdLayout.EntryOf(Document, Ref);
        SpanElement? Mesh => Entry is { Name: "VARIANT_MESH" } mesh ? mesh : null;

        /// <summary>The slot's own cells (name, attach point, probability) are edited on its first row and greyed on the rest.</summary>
        public bool IsFirst => Ref.IsFirst;
        public bool HasEntry => Entry != null;
        public bool HasTags => Mesh != null;

        /// <summary>The slot's names from the outermost, "pilot / body"; SlotName is this slot's own.</summary>
        public string Slot => Ref.Label;
        public string SlotName { get => SlotElement.Attr("name") ?? ""; set => Set(SlotElement, "name", value); }
        public string AttachPoint { get => SlotElement.Attr("attach_point") ?? ""; set => Set(SlotElement, "attach_point", value); }

        public string Kind => Entry?.Name switch { "VARIANT_MESH" => "model", "VARIANT_MESH_REFERENCE" => "reference", _ => "" };

        public string Path
        {
            get => Entry is { } entry ? entry.Attr(PathAttribute(entry)) ?? "" : "";
            set
            {
                if (Entry is { } entry)
                    Set(entry, PathAttribute(entry), value);
            }
        }

        static string PathAttribute(SpanElement entry) => entry.Name == "VARIANT_MESH" ? "model" : "definition";

        public string Imposter
        {
            get => Mesh?.Attr("imposter_model") ?? "";
            set
            {
                if (Mesh is { } mesh)
                    Set(mesh, "imposter_model", value);
            }
        }

        public string Tags
        {
            get => Mesh is { } mesh ? string.Join(", ", VmdEdits.Tags(Document, mesh)) : "";
            set
            {
                if (Mesh is not { } mesh)
                    return;
                var tags = (value ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length != 0).ToList();
                _owner.Edit(d => VmdEdits.SetTags(d, mesh, tags));
            }
        }

        // A probability holds what was typed while it does not parse, marked, and writes nothing until it does. Empty removes it.
        string? _probabilityText;
        public bool ProbabilityInvalid => _probabilityText != null;

        public string Probability
        {
            get => _probabilityText ?? SlotElement.Attr("probability") ?? "";
            set
            {
                var text = (value ?? "").Trim();
                if (text.Length == 0 || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    _probabilityText = null;
                    Set(SlotElement, "probability", text);
                }
                else
                {
                    _probabilityText = text;
                }
                Refresh();
            }
        }

        void Set(SpanElement element, string attribute, string? value) => _owner.Edit(d => VmdEdits.SetAttribute(d, element, attribute, (value ?? "").Trim()));

        // What the checks found on this row, shown as a mark on the row with the messages as its tooltip.
        IReadOnlyList<VmdFinding> _findings = [];
        public string? Findings => _findings.Count == 0 ? null : string.Join("\n", _findings.Select(x => x.Message).Distinct());
        public bool HasError => _findings.Any(x => x.IsError);
        public bool HasWarning => _findings.Count != 0 && !HasError;

        internal void SetFindings(IReadOnlyList<VmdFinding> findings)
        {
            _findings = findings;
            OnPropertyChanged(nameof(Findings));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasWarning));
        }

        /// <summary>Every cell read again from the document.</summary>
        public void Refresh() => OnPropertyChanged(string.Empty);
    }
}
