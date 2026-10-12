using System.Diagnostics;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;

namespace Shared.CoreTest.PackFiles.Models.Containers
{
    // The game's cache holds 750,000 files and the app asks it for a file's path all the time, first of every container, so a
    // path lookup must cost what a file lookup does, through an index, and not a read of every row: lowering the name column
    // in the query did that, 100 ms for one of the cache's own files and 300 ms for a mod's.
    internal class CachedPackFileContainerTests_GetFullPathCost
    {
        [Test]
        public void GetFullPath_CostsAboutWhatFindFileDoes()
        {
            const int Count = 200_000;
            var files = Enumerable.Range(0, Count)
                .Select(i => ($"folder{i % 500}\\file{i}.dds", $"file{i}.dds", (long)i * 10, 10L, false, false, CompressionFormat.None, 0u))
                .ToArray();
            using var container = CachedPackFileContainer.CreateFromFileList("Cost", files, useInMemoryDb: true, systemFilePath: @"c:\game\data", sourcePackFilePath: @"c:\game\data\pack1.pack");
            var paths = Enumerable.Range(0, 20).Select(i => files[i * 9973 % Count].Item1).ToList();
            var mod = new PackedFileSourceParent { FilePath = @"c:\mods\mod.pack" };

            double Each(Action<string> lookup)
            {
                lookup(paths[0]);
                var clock = Stopwatch.StartNew();
                foreach (var path in paths)
                    lookup(path);
                return clock.Elapsed.TotalMilliseconds / paths.Count;
            }
            var find = Each(path => container.FindFile(path));
            var own = Each(path => container.GetFullPath(container.FindFile(path)!));
            var modFile = Each(path => container.GetFullPath(new PackFile(Path.GetFileName(path), new PackedFileSource(mod, 0, 1, false, false, CompressionFormat.None, 0))));

            Assert.Multiple(() =>
            {
                Assert.That(own, Is.LessThan(find * 5), $"its own file: {own:0.00} ms against FindFile's {find:0.00} ms");
                Assert.That(modFile, Is.LessThan(find * 5), $"a mod's file: {modFile:0.00} ms against FindFile's {find:0.00} ms");
            });
        }
    }
}
