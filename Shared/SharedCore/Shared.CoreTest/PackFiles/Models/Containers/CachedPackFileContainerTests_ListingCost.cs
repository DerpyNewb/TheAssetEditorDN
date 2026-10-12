using System.Diagnostics;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;

namespace Shared.CoreTest.PackFiles.Models.Containers
{
    // Every Browse dialog lists the game's cache folder by folder to build its tree, then asks it for every texture to filter
    // that tree, and the cache never changes once loaded: the two took 1.3 s and 0.6 s of every click. Save is the one thing
    // that changes what the cache holds.
    internal class CachedPackFileContainerTests_ListingCost
    {
        static (string, string, long, long, bool, bool, CompressionFormat, uint)[] Files(string folder, int count) =>
            Enumerable.Range(0, count)
                .Select(i => ($"{folder}{i % 500}\\file{i}.dds", $"file{i}.dds", (long)i * 10, 10L, false, false, CompressionFormat.None, 0u))
                .ToArray();

        static CachedPackFileContainer Cache(string folder, int count) =>
            CachedPackFileContainer.CreateFromFileList("Cost", Files(folder, count), useInMemoryDb: true, systemFilePath: @"c:\game\data", sourcePackFilePath: @"c:\game\data\pack1.pack");

        static (T First, T Second, double FirstMs, double SecondMs) Twice<T>(Func<T> list)
        {
            var clock = Stopwatch.StartNew();
            var first = list();
            var firstMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var second = list();
            return (first, second, firstMs, clock.Elapsed.TotalMilliseconds);
        }

        [Test]
        public void GetAllFilesByFolder_TheSecondTime_DoesNotListTheCacheAgain()
        {
            using var container = Cache("folder", 200_000);
            var (_, second, firstMs, secondMs) = Twice(container.GetAllFilesByFolder);

            Assert.Multiple(() =>
            {
                Assert.That(second.Values.Sum(x => x.Count), Is.EqualTo(200_000));
                Assert.That(secondMs, Is.LessThan(firstMs / 10), $"first listing {firstMs:0} ms, second {secondMs:0} ms");
            });
        }

        [Test]
        public void SearchFiles_TheSameQueryTwice_DoesNotReadTheCacheAgain()
        {
            using var container = Cache("folder", 200_000);
            var (_, second, firstMs, secondMs) = Twice(() => container.SearchFiles(null, [".dds", ".png"]));

            Assert.Multiple(() =>
            {
                Assert.That(second, Has.Count.EqualTo(200_000));
                Assert.That(secondMs, Is.LessThan(firstMs / 10), $"first search {firstMs:0} ms, second {secondMs:0} ms");
            });
        }

        [Test]
        public void SearchFiles_ADifferentQuery_IsAnsweredAfresh()
        {
            using var container = Cache("folder", 100);
            container.SearchFiles(null, [".dds"]);

            var file7 = container.SearchFiles("file7", [".dds"]).Select(x => x.Path).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(container.SearchFiles(null, [".png"]), Is.Empty);
                Assert.That(file7, Has.Count.EqualTo(11).And.Contain(@"folder7\file7.dds"));
            });
        }

        [Test]
        public void Listings_AfterSave_ShowWhatWasSaved()
        {
            using var container = Cache("before", 10);
            Assert.That(container.GetAllFilesByFolder().Keys, Does.Contain("before0"));
            Assert.That(container.SearchFiles(null, [".dds"]).Select(x => x.Path), Has.None.EqualTo(@"after\new.dds"));

            var parent = new PackedFileSourceParent { FilePath = @"c:\game\data\pack2.pack" };
            var source = PackFileContainer.CreateCaPackFile("Saved", @"c:\game\data");
            source.SourcePackFilePaths.Add(parent.FilePath);
            source.AddOrUpdateFile("after\\new.dds", new PackFile("new.dds", new PackedFileSource(parent, 0, 10, false, false, CompressionFormat.None, 0)));
            container.Save("second", source);

            Assert.Multiple(() =>
            {
                Assert.That(container.GetAllFilesByFolder().Keys, Does.Contain("after"));
                Assert.That(container.SearchFiles(null, [".dds"]).Select(x => x.Path), Has.Some.EqualTo(@"after\new.dds"));
            });
        }
    }
}
