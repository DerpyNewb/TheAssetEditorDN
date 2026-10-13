using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.UnitSimulator.Pairing
{
    public enum PairStatus { Paired, RiderSlotMissing, AnimMissing, LengthsDiffer, CopyOnly }

    public record ClipLength(int Frames, float Seconds);

    /// <summary>One slot of the mount's table and the rider slot the game plays with it.</summary>
    public record PairRow(string MountSlot, string RiderSlot, PairStatus Status, bool RiderHasSlot, ClipLength? Mount, ClipLength? Rider)
    {
        public string StatusText => Status switch
        {
            PairStatus.Paired => "paired",
            PairStatus.RiderSlotMissing => "rider slot missing",
            PairStatus.AnimMissing => "anim missing",
            PairStatus.LengthsDiffer => "lengths differ",
            _ => "copy-only",
        };

        public string Detail => Status switch
        {
            PairStatus.Paired => $"{Mount!.Frames} frames",
            PairStatus.LengthsDiffer => $"mount {Mount!.Frames} frames {Mount.Seconds:0.00} s, rider {Rider!.Frames} frames {Rider.Seconds:0.00} s",
            _ => "",
        };
    }

    /// <summary>How the game pairs a rider with its mount: while the mount plays slot X, the rider plays RIDER_X.</summary>
    public static class RiderPairing
    {
        // The Mount Animation Creator's list (MountAnimationGeneratorService.IsCopyOnlyAnimation): slots a rider keeps as they are
        // instead of having them fitted to the mount.
        static readonly string[] s_copyOnlyPrefixes = ["HAND_POSE_", "DOCK_", "PERSISTENT_METADATA_", "PORTHOLE_", "RIDER_CAST_SPELL_", "RIDER_MOVING_ATTACK_", "RIDER_CELEBRATE_", "RIDER_FIRE_"];
        static readonly HashSet<string> s_copyOnlyNames = ["STAND", "MISSING_ANIM", "RIDER_SHOOT_READY", "RIDER_COMBAT_READY", "RIDER_RELOAD",
            "RIDER_STAND_TO_RIDER_COMBAT_READY", "RIDER_COMBAT_READY_TO_RIDER_STAND", "RIDER_SHOOT_READY_TO_RIDER_STAND",
            "RIDER_STAND_TO_RIDER_SHOOT_READY", "RIDER_SHOOT_READY_TO_RIDER_RELOAD", "RIDER_RELOAD_TO_RIDER_SHOOT_READY"];

        public static bool IsCopyOnly(string slot) => s_copyOnlyNames.Contains(slot) || s_copyOnlyPrefixes.Any(slot.StartsWith);

        public static string RiderSlotFor(string mountSlot) => "RIDER_" + mountSlot;

        /// <summary>One row per slot of the mount's table, in its order. <paramref name="lengthOf"/> gives an .anim's length, or null
        /// when no loaded pack has it.</summary>
        public static List<PairRow> Pair(IAnimationBinGenericFormat mount, IAnimationBinGenericFormat rider, Func<string, ClipLength?> lengthOf)
        {
            var riderSlots = Slots(rider).ToDictionary(x => x.Name, x => x.Anim);
            var rows = new List<PairRow>();
            foreach (var (mountSlot, mountAnim) in Slots(mount))
            {
                var riderName = RiderSlotFor(mountSlot);
                var riderHasSlot = riderSlots.TryGetValue(riderName, out var riderAnim);
                var mountLength = LengthOf(mountAnim, lengthOf);
                var riderLength = riderHasSlot ? LengthOf(riderAnim, lengthOf) : null;

                var status =
                    IsCopyOnly(mountSlot) || IsCopyOnly(riderName) ? PairStatus.CopyOnly :
                    !riderHasSlot ? PairStatus.RiderSlotMissing :
                    mountLength == null || riderLength == null ? PairStatus.AnimMissing :
                    mountLength.Frames != riderLength.Frames ? PairStatus.LengthsDiffer :
                    PairStatus.Paired;
                rows.Add(new PairRow(mountSlot, riderName, status, riderHasSlot, mountLength, riderLength));
            }
            return rows;
        }

        // A table's slots, each with its first animation or none. CA declares slots with nothing in them (the knight's RIDER_ATTACK_5,
        // 73 tables' WALK_2), and Entries, one item per animation, leaves them out; a WH3 table's own rows keep them.
        // ponytail: a slot's first variant only; one whose variants differ in length shows the first's. Look at all of them if that bites.
        static List<(string Name, string? Anim)> Slots(IAnimationBinGenericFormat table)
        {
            var slots = table is AnimationBinWh3 wh3
                ? wh3.AnimationTableEntries.Select(x => (AnimationSlotTypeHelperWh3.GetFromId((int)x.AnimationId).Value, x.AnimationRefs.FirstOrDefault()?.AnimationFile))
                : table.Entries.Select(x => (x.SlotName, (string?)x.AnimationFile));
            return slots.GroupBy(x => x.Item1).Select(x => x.First()).ToList();
        }

        static ClipLength? LengthOf(string? path, Func<string, ClipLength?> lengthOf) => string.IsNullOrWhiteSpace(path) ? null : lengthOf(path);

        /// <summary>The rider's RIDER_ slots that pair with no slot of the mount's: picked on the rider while the mount keeps its own.</summary>
        public static List<string> RiderOnly(IAnimationBinGenericFormat mount, IAnimationBinGenericFormat rider)
        {
            var mountSlots = Slots(mount).Select(x => x.Name).ToHashSet();
            return Slots(rider).Select(x => x.Name)
                .Where(x => x.StartsWith("RIDER_", StringComparison.Ordinal) && !mountSlots.Contains(x["RIDER_".Length..]))
                .ToList();
        }
    }
}
