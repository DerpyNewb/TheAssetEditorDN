using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter
{
    /// <summary>One mapping between a table row and the Animation element the XML view shows. The converter and the bin grid's
    /// copy and paste both use it, so a row copied from one pastes into the other.</summary>
    public static class BinXml
    {
        // A slot id the game's list does not name is shown as "Not found [id=N]" and saves back as N, so a table holding a slot
        // newer than the list (a later patch's) still opens, edits and saves with that row intact.
        public static int? SlotId(BaseAnimationSlotHelper slots, string slot)
        {
            if (slots.GetfromValue(slot) is { } known)
                return known.Id;
            var unnamed = Regex.Match(slot, @"^Not found \[id=(\d+)\]$");
            return unnamed.Success ? int.Parse(unnamed.Groups[1].Value) : null;
        }

        public static string SlotName(BaseAnimationSlotHelper slots, uint id) => slots.TryGetFromId((int)id)?.Value ?? $"Not found [id={(int)id}]";

        public static Animation FromEntry(AnimationBinEntry entry, BaseAnimationSlotHelper slots) => new()
        {
            Slot = SlotName(slots, entry.AnimationId),
            BlendId = entry.BlendIn,
            BlendOut = entry.SelectionWeight,
            Unk = entry.Unk,
            WeaponBone = ValueConverterHelper.ConvertIntToBoolArray(entry.WeaponBools),
            Ref = entry.AnimationRefs.Select(x => new Instance { File = x.AnimationFile, Meta = x.AnimationMetaFile, Sound = x.AnimationSoundMetaFile }).ToList(),
        };

        public static AnimationBinEntry ToEntry(Animation xml, BaseAnimationSlotHelper slots) => new()
        {
            AnimationId = (uint)(SlotId(slots, xml.Slot) ?? throw new ArgumentException($"{xml.Slot} is not an animation slot")),
            BlendIn = xml.BlendId,
            SelectionWeight = xml.BlendOut,
            WeaponBools = ValueConverterHelper.CreateWeaponFlagInt(xml.WeaponBone),
            Unk = xml.Unk,
            AnimationRefs = xml.Ref.Select(x => new AnimationBinEntry.AnimationRef { AnimationFile = x.File, AnimationMetaFile = x.Meta, AnimationSoundMetaFile = x.Sound }).ToList(),
        };

        /// <summary>The rows as Animation elements alone, written the way the XML view writes them, one after another.</summary>
        public static string ToText(IEnumerable<AnimationBinEntry> entries)
        {
            var slots = AnimationSlotTypeHelperWh3.GetInstance();
            var serializer = new XmlSerializer(typeof(Animation));
            var noNamespace = new XmlSerializerNamespaces();
            noNamespace.Add("", "");
            var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
            return string.Join(Environment.NewLine, entries.Select(entry =>
            {
                using var text = new StringWriter();
                using (var writer = XmlWriter.Create(text, settings))
                    serializer.Serialize(writer, FromEntry(entry, slots), noNamespace);
                return text.ToString();
            }));
        }

        /// <summary>Every Animation element in the text, as rows: a copy from the grid, or any part of the XML view's text, whole
        /// Bin included. An element that does not map to a WH3 row (an unknown slot, a missing attribute) is skipped. Null when
        /// nothing maps.</summary>
        public static List<AnimationBinEntry>? FromText(string text)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse("<r>" + text + "</r>");
            }
            catch (XmlException)
            {
                return null;
            }

            var slots = AnimationSlotTypeHelperWh3.GetInstance();
            var serializer = new XmlSerializer(typeof(Animation));
            var rows = new List<AnimationBinEntry>();
            foreach (var element in document.Descendants("Animation"))
            {
                try
                {
                    rows.Add(ToEntry((Animation)serializer.Deserialize(element.CreateReader())!, slots));
                }
                catch (Exception)
                {
                    // Not a row this table can hold; the others still paste.
                }
            }
            return rows.Count == 0 ? null : rows;
        }
    }
}
