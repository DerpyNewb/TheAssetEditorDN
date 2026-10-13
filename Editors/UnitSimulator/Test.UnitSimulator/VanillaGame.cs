using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    // The game's three anim packs from the data folder in WH3_DATA, as Test.VfxEditor's VanillaVfx loads vfx_desc.pack; a test using
    // them is skipped without it. They hold every battle table, skeleton and animation these tests read.
    static class VanillaGame
    {
        public const string KnightTable = "animations/database/battle/bin/hu1_empire_hr1_warhorse_lance_sword_shield.bin";
        public const string WarhorseTable = "animations/database/battle/bin/hr1_warhorse.bin";
        public const string TablesAnimPack = "animations/database/battle/bin/animation_tables__.animpack";

        // asCaPacks marks the packs as CA's, as the app loads them, for the tests of what a CA pack refuses.
        public static AssetEditorTestRunner Load(bool asCaPacks = false)
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(dataFolder != null && Directory.Exists(dataFolder), "set WH3_DATA");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            foreach (var pack in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder!, pack), true);
                container.IsCaPackFile = asCaPacks;
                runner.PackFileService.AddContainer(container);
            }
            return runner;
        }

        // Every battle table in the loaded animpacks, by path. The three vanilla animpacks repeat 1,404 of them, byte for byte.
        public static Dictionary<string, AnimationBinWh3> Tables(IPackFileService pfs)
        {
            var tables = new Dictionary<string, AnimationBinWh3>();
            foreach (var animPack in PackFileServiceUtility.GetAllAnimPacks(pfs))
                foreach (var table in AnimationPackSerializer.Load(animPack, pfs).Files.OfType<AnimationBinWh3>())
                    tables[table.FileName] = table;
            return tables;
        }
    }
}
