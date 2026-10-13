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
        /// <summary>The loaded battle table of this name (a rider table's MountBin names its mount's), or null.</summary>
        // ponytail: reads every animpack on each call, under a second for vanilla's three; cache by pack if picking a rider table drags.
        public static IAnimationBinGenericFormat? FindByName(IPackFileService pfs, string name) =>
            PackFileServiceUtility.GetAllAnimPacks(pfs)
                .SelectMany(x => AnimationPackSerializer.Load(x, pfs).GetGenericAnimationSets())
                .FirstOrDefault(x => x.Name == name);

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

        public static void SetMountBin(AnimationBinWh3 table, string mountBin) => table.MountBin = mountBin;

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

        /// <summary>Writes the table into the animpack it came from, at that animpack's path in the editable pack. Null when saved, else
        /// why not: the table is in a CA pack, or no editable pack is selected.</summary>
        public static string? Save(IPackFileService pfs, IFileSaveService saver, AnimationBinWh3 table)
        {
            var animPackPath = table.Parent?.FileName;
            var animPack = animPackPath == null ? null : pfs.FindFile(animPackPath);
            if (animPackPath == null || animPack == null)
                return $"{table.Name}: the animpack it came from is not loaded.";
            if (pfs.GetPackFileContainer(animPack)?.IsCaPackFile == true)
                return $"{table.Name} is in a CA pack, which cannot be saved. Copy {animPackPath} into your pack and edit that copy.";
            if (pfs.GetEditablePack() == null)
                return "No editable pack is selected.";
            try
            {
                saver.Save(animPackPath, Splice(animPack.DataSource.ReadData(), table.FileName, table.ToByteArray()), false);
                return null;
            }
            catch (Exception e)
            {
                return $"{table.Name} was not saved: {e.Message}";
            }
        }
    }
}
