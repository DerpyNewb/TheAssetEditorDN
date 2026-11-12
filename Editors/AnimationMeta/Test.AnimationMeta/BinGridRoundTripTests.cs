using System.IO;
using Editors.AnimationFragmentEditor.AnimationPack.BinGrid;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Opening a table in the bin grid and saving it with no edit must give back the bytes it was opened with: every CA table is
    // read through the grid, every cell of every row and every header field, and none may change it or ask to. Reads anim.pack
    // from WH3_DATA; skipped without it.
    public class BinGridRoundTripTests
    {
        static readonly string[] AnimPacks =
        [
            @"animations\database\battle\bin\animation_tables.animpack",
            @"animations\database\battle\bin\animation_tables__.animpack",
            @"animations\database\battle\bin\animation_tables__3.animpack",
        ];

        [Test]
        public void EveryCaTable_ReadThroughTheGrid_IsUnchanged()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            runner.PackFileService.AddContainer(loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, "anim.pack"), true));
            var skeletons = runner.ServiceProvider.GetRequiredService<ISkeletonAnimationLookUpHelper>();
            var checks = new BinTableChecks(runner.PackFileService, skeletons, runner.ServiceProvider.GetRequiredService<MetaDataFileParser>(), null);

            var tables = 0;
            var changed = new List<string>();
            foreach (var path in AnimPacks)
            {
                var animPack = AnimationPackSerializer.Load(runner.PackFileService.FindFile(path)!, runner.PackFileService);
                foreach (var table in animPack.Files.OfType<AnimationBinWh3>().Where(x => x.TableVersion == 4))
                {
                    tables++;
                    var before = table.ToByteArray();
                    var grid = new BinGridViewModel(table, (_, _) => changed.Add($"{table.FileName} asked to edit"), checks, null!, null!, runner.PackFileService, skeletons, [], _ => null);
                    foreach (var property in typeof(BinGridViewModel).GetProperties().Where(x => x.GetIndexParameters().Length == 0))
                        _ = property.GetValue(grid);
                    foreach (var row in grid.Rows)
                        foreach (var property in typeof(BinGridRow).GetProperties().Where(x => x.GetIndexParameters().Length == 0))
                            _ = property.GetValue(row);
                    if (!table.ToByteArray().AsSpan().SequenceEqual(before))
                        changed.Add($"{table.FileName} changed");
                }
            }

            TestContext.Out.WriteLine($"{tables} tables read through the grid");
            Assert.That(tables, Is.GreaterThan(2500), "anim.pack did not load the tables"); // 2830 in 9.0
            Assert.That(changed, Is.Empty);
        }
    }
}
