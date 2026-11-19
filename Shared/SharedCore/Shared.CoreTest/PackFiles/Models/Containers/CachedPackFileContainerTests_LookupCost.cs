using System.Diagnostics;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Utility;

namespace Shared.CoreTest.PackFiles.Models.Containers
{
    // The app loads the game's cache from its file with pooling off, and the context closed its connection after every query, so
    // each lookup opened the 300 MB file again: 3 ms a path, and the bin grid's thousand paths took 3 s of every edit. A lookup in
    // the cache's file must cost about what it does in memory, where a kept connection always hid it.
    internal class CachedPackFileContainerTests_LookupCost
    {
        [Test]
        public void FindFile_InTheCacheFile_CostsAboutWhatItDoesInMemory()
        {
            const int Count = 20_000;
            var files = Enumerable.Range(0, Count)
                .Select(i => ($"folder{i % 50}\\file{i}.anim", $"file{i}.anim", (long)i * 10, 10L, false, false, CompressionFormat.None, 0u))
                .ToArray();
            var paths = Enumerable.Range(0, 200).Select(i => i % 2 == 0 ? files[i * 97 % Count].Item1 : $"missing\\file{i}.anim").ToList();
            var dbPath = Path.Combine(Path.GetTempPath(), $"AE_LookupCost_{Guid.NewGuid():N}.db");

            double Each(CachedPackFileContainer container)
            {
                container.FindFile(paths[0]);
                var clock = Stopwatch.StartNew();
                foreach (var path in paths)
                    container.FindFile(path);
                return clock.Elapsed.TotalMilliseconds / paths.Count;
            }

            try
            {
                using var memory = CachedPackFileContainer.CreateFromFileList("Cost", files, useInMemoryDb: true, systemFilePath: @"c:\game\data", sourcePackFilePath: @"c:\game\data\pack1.pack");
                using var file = CachedPackFileContainer.CreateFromFileList("Cost", files, useInMemoryDb: false, dbFilePath: dbPath, systemFilePath: @"c:\game\data", sourcePackFilePath: @"c:\game\data\pack1.pack");
                var inMemory = Each(memory);
                var inFile = Each(file);
                Assert.That(inFile, Is.LessThan(inMemory * 5 + 0.05), $"in the file: {inFile:0.000} ms a lookup, in memory: {inMemory:0.000} ms");
            }
            finally
            {
                File.Delete(dbPath);
            }
        }
    }
}
