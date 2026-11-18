using System.Diagnostics;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;

namespace Shared.CoreTest.PackFiles.Models.Containers
{
    // Scratch, never commit: how long the game's cached container takes to answer the app's lookups.
    internal class CachedLookupScratch
    {
        [Test, Explicit]
        public void Time()
        {
            var db = Environment.GetEnvironmentVariable("CA_CACHE_DB")!;
            using var c = new CachedPackFileContainer("ca", db);
            var o = TestContext.Out;
            var clock = Stopwatch.StartNew();
            o.WriteLine($"files {c.GetFileCount()} in {clock.ElapsedMilliseconds} ms");
            clock.Restart();
            var dds = c.FindAllWithExtention(".dds").Skip(1000).Take(40).Select(x => x.FileName).ToList();
            o.WriteLine($"FindAllWithExtention .dds: {clock.ElapsedMilliseconds} ms");

            double Each(int n, Action<int> act)
            {
                act(0);
                var sw = Stopwatch.StartNew();
                for (var i = 1; i <= n; i++) act(i);
                return sw.Elapsed.TotalMilliseconds / n;
            }
            o.WriteLine($"FindFile: {Each(39, i => c.FindFile(dds[i])):0.000} ms each");
            var files = dds.Select(p => c.FindFile(p)!).ToList();
            o.WriteLine($"GetFullPath, its own file: {Each(5, i => c.GetFullPath(files[i])):0.000} ms each");
            var foreignSource = new PackedFileSourceParent { FilePath = @"c:\mods\other.pack" };
            o.WriteLine($"GetFullPath, another pack's file of a CA name: {Each(5, i => c.GetFullPath(new PackFile(files[i].Name, new PackedFileSource(foreignSource, 0, 1, false, false, Shared.Core.PackFiles.Utility.CompressionFormat.None, 0)))):0.000} ms each");
            o.WriteLine($"GetFullPath, another pack's file of a new name: {Each(5, i => c.GetFullPath(new PackFile($"derpy_new_{i}.dds", new PackedFileSource(foreignSource, 0, 1, false, false, Shared.Core.PackFiles.Utility.CompressionFormat.None, 0)))):0.000} ms each");
            o.WriteLine($"ContainsFile: {Each(39, i => c.ContainsFile(dds[i])):0.000} ms each");
            clock.Restart();
            var folder = c.GetDirectoryContent(Path.GetDirectoryName(dds[0])!);
            o.WriteLine($"GetDirectoryContent ({folder.Count}): {clock.ElapsedMilliseconds} ms");
            clock.Restart();
            var byFolder = c.GetAllFilesByFolder();
            o.WriteLine($"GetAllFilesByFolder ({byFolder.Count} folders): {clock.ElapsedMilliseconds} ms");
            clock.Restart();
            var search = c.SearchFiles(null, [".dds", ".png"]);
            o.WriteLine($"SearchFiles .dds/.png ({search.Count}): {clock.ElapsedMilliseconds} ms");
            o.WriteLine($"largest folders: " + string.Join(", ", byFolder.OrderByDescending(x => x.Value.Count).Take(5).Select(x => $"{x.Key} {x.Value.Count}")));
        }
    }
}
