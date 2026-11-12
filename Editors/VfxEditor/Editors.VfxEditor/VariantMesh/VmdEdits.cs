using Editors.VfxEditor.Document;

namespace Editors.VfxEditor.VariantMesh
{
    /// <summary>The grid's edits as splices for one VfxDocument.Apply, so each is one undo step and touches only its own text. An
    /// empty list means nothing changes. Removing a slot or an entry is VfxDocument.Remove.</summary>
    public static class VmdEdits
    {
        /// <summary>The attributes that hold a path: a mesh's model and imposter, a reference's definition.</summary>
        public static readonly string[] PathAttributes = ["model", "imposter_model", "definition"];

        /// <summary>Sets the attribute, adding it when absent; an empty value removes it with the space before it.</summary>
        public static List<Splice> SetAttribute(VfxDocument document, SpanElement element, string name, string value)
        {
            var attribute = element.Attribute(name);
            if (attribute == null)
                return value.Length == 0 ? [] : [document.SetAttribute(element, name, value)];
            if (attribute.Value == value)
                return [];
            if (value.Length != 0)
                return [document.SetAttribute(element, name, value)];
            var text = document.Text;
            var p = attribute.ValueStart - 2;   // before the opening quote
            while (char.IsWhiteSpace(text[p]))
                p--;
            p--;                                // the '='
            while (char.IsWhiteSpace(text[p]))
                p--;
            var from = p - name.Length + 1;
            while (from > element.NameEnd && char.IsWhiteSpace(text[from - 1]))
                from--;
            return [new Splice(from, attribute.ValueEnd + 1, "")];
        }

        public static IReadOnlyList<string> Tags(VfxDocument document, SpanElement mesh) =>
            mesh.ChildrenNamed("META_DATA").Select(document.TextOf).ToList();

        /// <summary>Changed tags are rewritten in place, extra ones removed, new ones put after the last tag with the same spacing
        /// (CA keeps a mesh's tags on one line, tab-separated), else before the mesh's first slot, else into the mesh.</summary>
        public static List<Splice> SetTags(VfxDocument document, SpanElement mesh, IReadOnlyList<string> tags)
        {
            var old = mesh.ChildrenNamed("META_DATA").ToList();
            var splices = new List<Splice>();
            for (var i = 0; i < Math.Min(old.Count, tags.Count); i++)
            {
                if (document.TextOf(old[i]) != tags[i])
                    splices.Add(new Splice(old[i].Start, old[i].End, Tag(tags[i])));
            }
            for (var i = tags.Count; i < old.Count; i++)
                splices.Add(document.Remove(old[i]));
            if (tags.Count <= old.Count)
                return splices;

            var added = tags.Skip(old.Count).Select(Tag).ToList();
            if (old.Count > 0)
            {
                var space = SpaceBefore(document, old[^1]);
                splices.Add(new Splice(old[^1].End, old[^1].End, string.Concat(added.Select(t => space + t))));
            }
            else if (mesh.Children.Count > 0)
            {
                var first = mesh.Children[0];
                var space = document.LineBreakBefore(first);
                splices.Add(new Splice(first.Start, first.Start, string.Concat(added.Select(t => t + space))));
            }
            else
            {
                splices.Add(document.InsertInto(mesh, string.Join(document.ChildBreak(mesh), added)));
            }
            return splices;
        }

        static string Tag(string value) => $"<META_DATA>{XmlText.EscapeText(value)}</META_DATA>";

        /// <summary>The whitespace between an element and whatever precedes it.</summary>
        static string SpaceBefore(VfxDocument document, SpanElement element)
        {
            var start = element.Start;
            while (start > 0 && char.IsWhiteSpace(document.Text[start - 1]))
                start--;
            return document.Text[start..element.Start];
        }

        /// <summary>A slot named slot_N, the first N free at its level: after `after`, else last in the file's own mesh.</summary>
        public static (Splice Splice, string Name) AddSlot(VfxDocument document, SpanElement? after)
        {
            var parent = after?.Parent ?? VmdLayout.Top(document);
            var names = parent.ChildrenNamed("SLOT").Select(s => s.Attr("name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var n = 1;
            while (names.Contains($"slot_{n}"))
                n++;
            var name = $"slot_{n}";
            var xml = $"<SLOT name=\"{name}\" />";
            if (after != null)
                return (document.InsertAfter(after, xml), name);
            if (parent.Children.Count > 0)
                return (document.InsertInto(parent, xml), name);
            // The file's own mesh, empty: its first slot on its own line, as CA lays a file out.
            return (parent.SelfClosing
                ? new Splice(parent.StartTagClose, parent.End, $">\r\n\t{xml}\r\n</{parent.Name}>")
                : new Splice(parent.ContentStart, parent.ContentEnd, $"\r\n\t{xml}\r\n"), name);
        }

        /// <summary>A reference for a .variantmeshdefinition, a model for anything else, as the slot's last entry.</summary>
        public static Splice AddEntry(VfxDocument document, SpanElement slot, string path) =>
            document.InsertInto(slot, EntryXml(path));

        public static string EntryXml(string path) => path.EndsWith(".variantmeshdefinition", StringComparison.OrdinalIgnoreCase)
            ? $"<VARIANT_MESH_REFERENCE definition=\"{XmlText.EscapeAttribute(path, '"')}\" />"
            : $"<VARIANT_MESH model=\"{XmlText.EscapeAttribute(path, '"')}\" />";

        /// <summary>Each slot's text once, in file order.</summary>
        public static string Copy(VfxDocument document, IEnumerable<SpanElement> slots) =>
            string.Join("\r\n", slots.DistinctBy(s => s.Start).OrderBy(s => s.Start).Select(document.RawOf));

        /// <summary>Slots go after `slot` (or last in the file), meshes and references into `slot`. Throws ArgumentException with the
        /// reason when the text is not XML, holds none of the three, or holds meshes and no slot is given.</summary>
        public static List<Splice> Paste(VfxDocument document, SpanElement? slot, string xml)
        {
            SpanElement parsed;
            try
            {
                parsed = XmlSpanParser.Parse(xml);
            }
            catch (VfxParseException e)
            {
                throw new ArgumentException($"The clipboard text is not XML: {e.Message}");
            }
            var slots = parsed.ChildrenNamed("SLOT").Select(e => xml[e.Start..e.End]).ToList();
            var entries = parsed.Children.Where(VmdLayout.IsEntry).Select(e => xml[e.Start..e.End]).ToList();
            if (slots.Count == 0 && entries.Count == 0)
                throw new ArgumentException("The clipboard holds no SLOT, VARIANT_MESH or VARIANT_MESH_REFERENCE to paste.");
            if (entries.Count > 0 && slot == null)
                throw new ArgumentException("Select a row whose slot takes the pasted meshes.");

            var splices = new List<Splice>();
            if (entries.Count > 0)
                splices.Add(document.InsertInto(slot!, string.Join(document.ChildBreak(slot!), entries)));
            if (slots.Count > 0)
            {
                var top = VmdLayout.Top(document);
                splices.Add(slot != null
                    ? document.InsertAfter(slot, string.Join(document.LineBreakBefore(slot), slots))
                    : document.InsertInto(top, string.Join(document.ChildBreak(top), slots)));
            }
            return splices;
        }

        /// <summary>Plain text, ignoring case, in the entries' path attributes; `cells` counts the attributes changed.</summary>
        public static List<Splice> ReplaceInPaths(VfxDocument document, IEnumerable<SpanElement> entries, string find, string replace, out int cells)
        {
            cells = 0;
            var splices = new List<Splice>();
            if (find.Length == 0)
                return splices;
            foreach (var entry in entries)
            {
                foreach (var name in PathAttributes)
                {
                    var value = entry.Attr(name);
                    if (value == null || !value.Contains(find, StringComparison.OrdinalIgnoreCase))
                        continue;
                    splices.Add(document.SetAttribute(entry, name, value.Replace(find, replace, StringComparison.OrdinalIgnoreCase)));
                    cells++;
                }
            }
            return splices;
        }
    }
}
