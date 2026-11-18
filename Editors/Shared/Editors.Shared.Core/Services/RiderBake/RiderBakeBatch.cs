using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.Shared.Core.Services.RiderBake
{
    /// <summary>One mount for a batch: its table, skeleton and seat.</summary>
    public record RiderBakeTable(AnimationBinWh3 Table, RiderBakeMount Mount);

    /// <summary>What a batch made: the new rider table, the .anim files it names, and one line per rider slot saying what was done.</summary>
    public record RiderBakeOutput(AnimationBinWh3 Table, List<(string Path, byte[] Bytes)> Anims, List<string> Report);

    /// <summary>Bakes every rider slot of a rider table onto a mount's table: RIDER_X is fitted to the mount's X. The new table is a copy
    /// of the rider's, every row and variant kept, with the baked rows naming the new .anim files; copy-only slots
    /// (RiderBaker.IsCopyOnly), rider-only slots and slots the mount lacks keep the files they had. Slot ids come from the rider
    /// table's own rows and names from the 9.0 slot list: the Mount Animation Creator looked both up in the Warhammer 2 list.
    /// Writes nothing; the caller saves.</summary>
    public static class RiderBakeBatch
    {
        public static string TablePath(string name) => $"animations/database/battle/bin/{name}.bin";

        /// <param name="clip">Reads an .anim for a skeleton; null when no loaded pack has it.</param>
        /// <param name="source">The mount the rider table was made for; null for a table of foot clips.</param>
        public static RiderBakeOutput Bake(AnimationBinWh3 riderTable, GameSkeleton riderSkeleton, RiderBakeTable? source, RiderBakeTable target,
            Matrix offset, string newName, Func<string, GameSkeleton, AnimationClip?> clip)
        {
            newName = newName.ToLowerInvariant();
            var table = new AnimationBinWh3(TablePath(newName), riderTable.ToByteArray()) { Name = newName, MountBin = target.Table.Name };
            var anims = new List<(string, byte[])>();
            var report = new List<string>();
            var folder = $"animations/battle/{riderSkeleton.SkeletonName.ToLowerInvariant()}/{newName}";

            foreach (var row in table.AnimationTableEntries)
            {
                var slot = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)row.AnimationId)?.Value ?? $"slot {row.AnimationId}";
                if (!slot.StartsWith("RIDER_", StringComparison.Ordinal))
                    continue;
                if (RiderBaker.IsCopyOnly(slot))
                {
                    report.Add($"{slot}: copy-only, kept as it was");
                    continue;
                }
                var mountSlot = slot["RIDER_".Length..];
                var targetFile = FirstAnim(target.Table, mountSlot);
                if (targetFile == null)
                {
                    report.Add($"{slot}: {target.Table.Name} has no {mountSlot}, kept as it was");
                    continue;
                }
                var targetClip = clip(targetFile, target.Mount.Skeleton);
                if (targetClip == null)
                {
                    report.Add($"{slot}: {targetFile} is in no loaded pack, kept as it was");
                    continue;
                }
                AnimationClip? sourceClip = null;
                if (source != null && (FirstAnim(source.Table, mountSlot) is not { } sourceFile || (sourceClip = clip(sourceFile, source.Mount.Skeleton)) == null))
                {
                    report.Add($"{slot}: {source.Table.Name} has no {mountSlot} to take the rider off, kept as it was");
                    continue;
                }

                for (var i = 0; i < row.AnimationRefs.Count; i++)
                {
                    var reference = row.AnimationRefs[i];
                    if (string.IsNullOrWhiteSpace(reference.AnimationFile))
                        continue;
                    var riderClip = clip(reference.AnimationFile, riderSkeleton);
                    if (riderClip == null)
                    {
                        report.Add($"{slot}: {reference.AnimationFile} is in no loaded pack, kept as it was");
                        continue;
                    }
                    var baked = RiderBaker.Bake(riderClip, riderSkeleton, source?.Mount, sourceClip, target.Mount, targetClip, offset);
                    var path = $"{folder}/{slot.ToLowerInvariant()}{(i == 0 ? "" : $"_{i}")}.anim";
                    anims.Add((path, AnimationFile.ConvertToBytes(baked.ConvertToFileFormat(riderSkeleton))));
                    reference.AnimationFile = path;
                    report.Add($"{slot}: baked onto {mountSlot}, {baked.DynamicFrames.Count} frames");
                }
            }
            return new RiderBakeOutput(table, anims, report);
        }

        // The first animation of a slot, by its 9.0 name; null when the table has no such slot or the slot names no file.
        static string? FirstAnim(AnimationBinWh3 table, string slot)
        {
            var id = AnimationSlotTypeHelperWh3.GetfromValue(slot)?.Id;
            var file = table.AnimationTableEntries.FirstOrDefault(x => x.AnimationId == id)?.AnimationRefs.FirstOrDefault()?.AnimationFile;
            return string.IsNullOrWhiteSpace(file) ? null : file;
        }
    }
}
