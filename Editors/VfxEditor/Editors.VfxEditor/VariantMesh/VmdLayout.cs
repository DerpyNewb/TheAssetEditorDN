using System.Text;
using Editors.VfxEditor.Document;

namespace Editors.VfxEditor.VariantMesh
{
    /// <summary>A grid row: one entry (a VARIANT_MESH or VARIANT_MESH_REFERENCE) of a slot, or a slot with none (Entry -1). Slot is
    /// the SLOT's path in the document, Entry its index among the slot's entries, Label the slot names from the outermost.</summary>
    public sealed record VmdRowRef(int[] Slot, int Entry, int Depth, string Label, bool IsFirst)
    {
        /// <summary>The row's identity across a rebuild that keeps the structure: the slot path and the entry.</summary>
        public string Key => string.Join(",", Slot) + ":" + Entry;
    }

    /// <summary>A .variantmeshdefinition read as a VfxDocument (docs/superpowers/specs/2026-11-13-asseteditor-variant-mesh-grid-design.md),
    /// and its rows in file order, each nested slot right after the entry that holds it.</summary>
    public static class VmdLayout
    {
        const string ReopenHint = "Close this tab and open the file again, picking the text editor.";

        public static (VfxDocument? Document, string? Error) Load(byte[] bytes)
        {
            VfxDocument document;
            try
            {
                document = VfxDocument.Load(bytes);
            }
            catch (VfxParseException e)
            {
                return (null, $"The file did not parse: {e.Message}. {ReopenHint}");
            }
            catch (DecoderFallbackException e)
            {
                return (null, $"The file is not valid text: {e.Message}. {ReopenHint}");
            }
            if (document.Root.Children.FirstOrDefault()?.Name != "VARIANT_MESH")
                return (null, $"The file's first element is not <VARIANT_MESH>, so it is not a variant mesh. {ReopenHint}");
            return (document, null);
        }

        /// <summary>The file's own VARIANT_MESH.</summary>
        public static SpanElement Top(VfxDocument document) => document.Root.Children[0];

        public static bool IsEntry(SpanElement element) => element.Name is "VARIANT_MESH" or "VARIANT_MESH_REFERENCE";

        public static List<SpanElement> Entries(SpanElement slot) => slot.Children.Where(IsEntry).ToList();

        public static SpanElement? EntryOf(VfxDocument document, VmdRowRef row) => row.Entry < 0 ? null : Entries(document.At(row.Slot))[row.Entry];

        public static List<VmdRowRef> Rows(VfxDocument document)
        {
            var rows = new List<VmdRowRef>();
            AddSlots(Top(document), 0, "", rows);
            return rows;
        }

        static void AddSlots(SpanElement mesh, int depth, string prefix, List<VmdRowRef> rows)
        {
            foreach (var slot in mesh.ChildrenNamed("SLOT"))
            {
                var label = prefix + (slot.Attr("name") ?? "");
                var entries = Entries(slot);
                if (entries.Count == 0)
                    rows.Add(new VmdRowRef(slot.Path, -1, depth, label, true));
                for (var i = 0; i < entries.Count; i++)
                {
                    rows.Add(new VmdRowRef(slot.Path, i, depth, label, i == 0));
                    AddSlots(entries[i], depth + 1, label + " / ", rows);
                }
            }
        }
    }
}
