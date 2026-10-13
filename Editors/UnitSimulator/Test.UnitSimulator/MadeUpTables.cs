using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;

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

        // The tables in one animpack in a new editable pack, as a mod ships them. Add it before giving a part its skeleton: a part
        // lists its tables when its skeleton changes.
        public static IPackFileContainer AddToEditablePack(AssetEditorTestRunner runner, string animPackName, params AnimationBinWh3[] tables)
        {
            var animPack = new AnimationPackFileDatabase(animPackName);
            foreach (var table in tables)
                animPack.AddFile(table);
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes(animPackName, AnimationPackSerializer.ConvertToBytes(animPack)))]);
            return pack;
        }
    }
}
