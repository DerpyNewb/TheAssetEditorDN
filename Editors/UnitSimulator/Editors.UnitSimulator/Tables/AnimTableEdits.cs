using System.IO;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.UnitSimulator.Tables
{
    /// <summary>Edits to a battle table, and the save that writes one back into its animpack with every other entry untouched.</summary>
    public static class AnimTableEdits
    {
        /// <summary>Every loaded battle table by name, one copy each (OneCopyEach). Reads every animpack: call it once and keep the result.</summary>
        public static Dictionary<string, IAnimationBinGenericFormat> AllByName(IPackFileService pfs) =>
            OneCopyEach(pfs, PackFileServiceUtility.GetAllAnimPacks(pfs).SelectMany(x => AnimationPackSerializer.Load(x, pfs).GetGenericAnimationSets()))
                .GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.First());

        /// <summary>The tables with one copy of each path. A table is listed once per pack holding its animpack, and CA ships some in two
        /// animpacks; the copy kept is the editable pack's, which a save writes to, else the last loaded, which is the one FindFile gives.</summary>
        public static List<IAnimationBinGenericFormat> OneCopyEach(IPackFileService pfs, IEnumerable<IAnimationBinGenericFormat> tables)
        {
            var editable = pfs.GetEditablePack();
            var editableAnimPacks = new Dictionary<string, List<IAnimationBinGenericFormat>?>();
            List<IAnimationBinGenericFormat>? InEditable(string? animPack)
            {
                if (editable == null || animPack == null)
                    return null;
                if (!editableAnimPacks.TryGetValue(animPack, out var sets))
                    editableAnimPacks[animPack] = sets = pfs.FindFile(animPack, editable) is { } file ? AnimationPackSerializer.Load(file, pfs).GetGenericAnimationSets() : null;
                return sets;
            }

            return tables.GroupBy(x => x.FullPath)
                .Select(copies => copies.Count() == 1 ? copies.First()
                    : copies.Select(x => InEditable(x.PackFileReference?.FileName)?.FirstOrDefault(y => y.FullPath == copies.Key)).FirstOrDefault(x => x != null) ?? copies.Last())
                .ToList();
        }

        public static AnimationBinEntry Row(AnimationBinWh3 table, int slotId) =>
            table.AnimationTableEntries.FirstOrDefault(x => x.AnimationId == (uint)slotId) ?? throw new ArgumentException($"{table.FileName} has no slot {slotId}");

        /// <summary>A slot's three files. The slot is the variant the picker shows, found in its row by the files it names now.</summary>
        public static void SetFiles(AnimationBinWh3 table, AnimationBinEntryGenericFormat slot, string anim, string meta, string sound)
        {
            var reference = Row(table, slot.SlotIndex).AnimationRefs.FirstOrDefault(x => x.AnimationFile == slot.AnimationFile && x.AnimationMetaFile == slot.MetaFile && x.AnimationSoundMetaFile == slot.SoundFile)
                ?? throw new ArgumentException($"{table.FileName}'s {slot.SlotName} has no variant naming {slot.AnimationFile}");
            reference.AnimationFile = anim;
            reference.AnimationMetaFile = meta;
            reference.AnimationSoundMetaFile = sound;
        }

        public static void SetBlendAndWeight(AnimationBinWh3 table, int slotId, float blendIn, float weight)
        {
            var row = Row(table, slotId);
            row.BlendIn = blendIn;
            row.SelectionWeight = weight;
        }

        /// <summary>Adds a slot as a copy of a row: its files, blend, weight and weapon bones. Kept in id order, as CA's tables are.</summary>
        public static void AddSlot(AnimationBinWh3 table, string slotName, AnimationBinEntry copyOf)
        {
            var slot = AnimationSlotTypeHelperWh3.GetfromValue(slotName) ?? throw new ArgumentException($"{slotName} is not a WH3 slot");
            if (table.AnimationTableEntries.Any(x => x.AnimationId == (uint)slot.Id))
                throw new ArgumentException($"{table.FileName} already has {slotName}");

            var row = new AnimationBinEntry
            {
                AnimationId = (uint)slot.Id,
                BlendIn = copyOf.BlendIn,
                SelectionWeight = copyOf.SelectionWeight,
                WeaponBools = copyOf.WeaponBools,
                Unk = copyOf.Unk,
                AnimationRefs = copyOf.AnimationRefs.Select(x => new AnimationBinEntry.AnimationRef { AnimationFile = x.AnimationFile, AnimationMetaFile = x.AnimationMetaFile, AnimationSoundMetaFile = x.AnimationSoundMetaFile }).ToList(),
            };
            var at = table.AnimationTableEntries.FindIndex(x => x.AnimationId > row.AnimationId);
            table.AnimationTableEntries.Insert(at < 0 ? table.AnimationTableEntries.Count : at, row);
        }

        /// <summary>An animpack's entries, name and bytes, in file order: u32 count, then per entry a CA string, a u32 size and the data.</summary>
        public static IEnumerable<(string Name, byte[] Data)> ReadEntries(byte[] animPack)
        {
            var chunk = new ByteChunk(animPack);
            var count = chunk.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var entry = new AnimationEntryMetaData(chunk);
                yield return (entry.Name, chunk.GetBytesFromBuffer(entry.StartOffset, entry.Size));
                chunk.Index = entry.StartOffset + entry.Size;
            }
        }

        /// <summary>The animpack with one entry's data replaced and every other entry's bytes copied as they were. Not
        /// AnimationPackSerializer.ConvertToBytes: that re-serialises every entry, and one its loader could not decode it holds as the
        /// whole animpack's buffer (AnimationPackSerializer.LoadFile).</summary>
        public static byte[] Splice(byte[] animPack, string entryName, byte[] entryData)
        {
            var chunk = new ByteChunk(animPack);
            var count = chunk.ReadInt32();
            using var output = new MemoryStream();
            output.Write(animPack, 0, 4);
            var found = false;
            for (var i = 0; i < count; i++)
            {
                var start = chunk.Index;
                var entry = new AnimationEntryMetaData(chunk);
                var end = entry.StartOffset + entry.Size;
                if (entry.Name == entryName)
                {
                    output.Write(new AnimationEntryMetaData { Name = entry.Name, Size = entryData.Length }.ToByteArray());
                    output.Write(entryData);
                    found = true;
                }
                else
                {
                    output.Write(animPack, start, end - start);
                }
                chunk.Index = end;
            }
            if (chunk.Index != animPack.Length)
                throw new InvalidDataException($"{animPack.Length - chunk.Index} bytes after the animpack's last entry");
            if (!found)
                throw new ArgumentException($"the animpack has no {entryName}");
            return output.ToArray();
        }

        /// <summary>The check before an animpack replaces the one in the pack: the same entries in the same order, every other one
        /// byte for byte as it was, and the table read back from what was written giving the same bytes. Throws on a difference, so
        /// a bad write never replaces a good animpack.</summary>
        public static void Verify(byte[] original, byte[] spliced, string entryName, byte[] entryData)
        {
            var before = ReadEntries(original).ToList();
            var after = ReadEntries(spliced).ToList();
            if (!before.Select(x => x.Name).SequenceEqual(after.Select(x => x.Name)))
                throw new InvalidDataException("the written animpack's entries are not the ones it had");
            for (var i = 0; i < after.Count; i++)
            {
                var expected = after[i].Name == entryName ? entryData : before[i].Data;
                if (!after[i].Data.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException($"the written animpack's {after[i].Name} is not what was meant to be there");
            }
            if (!new AnimationBinWh3(entryName, entryData).ToByteArray().AsSpan().SequenceEqual(entryData))
                throw new InvalidDataException($"{entryName} does not read back as written");
        }

        /// <summary>Writes the table into the animpack it came from, at that animpack's path in the editable pack. Null when saved, else
        /// why not: the table is in a CA pack, or no editable pack is selected. The animpack spliced is the editable pack's copy when it
        /// has one, so a second table saved keeps the first; a pack loaded later holding the animpack would otherwise be read instead.</summary>
        public static string? Save(IPackFileService pfs, IFileSaveService saver, AnimationBinWh3 table)
        {
            var animPackPath = table.Parent?.FileName;
            var editable = pfs.GetEditablePack();
            var animPack = animPackPath == null ? null : (editable == null ? null : pfs.FindFile(animPackPath, editable)) ?? pfs.FindFile(animPackPath);
            if (animPackPath == null || animPack == null)
                return $"{table.Name}: the animpack it came from is not loaded.";
            if (pfs.GetPackFileContainer(animPack)?.IsCaPackFile == true)
                return $"{table.Name} is in a CA pack, which cannot be saved. Copy {animPackPath} into your pack and edit that copy.";
            if (editable == null)
                return "No editable pack is selected.";
            try
            {
                var original = animPack.DataSource.ReadData();
                var data = table.ToByteArray();
                var spliced = Splice(original, table.FileName, data);
                Verify(original, spliced, table.FileName, data);
                saver.Save(animPackPath, spliced, false);
                return null;
            }
            catch (Exception e)
            {
                return $"{table.Name} was not saved: {e.Message}";
            }
        }
    }
}
