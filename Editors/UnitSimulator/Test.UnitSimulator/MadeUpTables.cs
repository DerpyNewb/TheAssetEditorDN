using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Test.UnitSimulator
{
    // Battle tables built in the test, a slot at a time by its 9.0 name, for the cases no vanilla table has.
    static class MadeUpTables
    {
        public static AnimationBinWh3 Table(string name, string skeleton, string mountBin, params (string Slot, string Anim)[] slots)
        {
            var table = new AnimationBinWh3($"animations/database/battle/bin/{name}.bin") { Name = name, SkeletonName = skeleton, MountBin = mountBin };
            foreach (var (slot, anim) in slots)
                table.AnimationTableEntries.Add(new AnimationBinEntry
                {
                    AnimationId = (uint)AnimationSlotTypeHelperWh3.GetfromValue(slot).Id,
                    BlendIn = 0.2f,
                    SelectionWeight = 1,
                    AnimationRefs = [new() { AnimationFile = anim, AnimationMetaFile = "", AnimationSoundMetaFile = "" }],
                });
            return table;
        }
    }
}
