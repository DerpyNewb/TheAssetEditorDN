using System.Collections.Concurrent;
using System.Text;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;

namespace Shared.CoreTest.PackFiles
{
    // Scratch: every pack under PACK_SURVEY's folders (';'-separated) through the loader, damaged or refused ones listed.
    internal class PackLoadSurveyScratch
    {
        [Test]
        public void Survey()
        {
            var roots = Environment.GetEnvironmentVariable("PACK_SURVEY");
            if (string.IsNullOrWhiteSpace(roots))
                Assert.Ignore("PACK_SURVEY not set");

            var packs = roots.Split(';').SelectMany(r => Directory.EnumerateFiles(r, "*.pack", SearchOption.AllDirectories)).ToList();
            var damaged = new ConcurrentBag<string>();
            var refused = new ConcurrentBag<string>();
            var files = 0L;
            Parallel.ForEach(packs, path =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    using var reader = new BinaryReader(stream, Encoding.ASCII);
                    var pack = PackFileSerializerLoader.Load(path, stream.Length, reader, new CaPackDuplicateFileResolver());
                    Interlocked.Add(ref files, pack.GetFileCount());
                    if (pack.DamagedFiles.Count != 0)
                        damaged.Add($"{path}: {pack.DamagedFiles.Count} damaged, first {pack.DamagedFiles[0]}");
                }
                catch (Exception e)
                {
                    refused.Add($"{path}: {e.GetType().Name}: {e.Message}");
                }
            });

            TestContext.Out.WriteLine($"SURVEY packs={packs.Count} files={files} damaged={damaged.Count} refused={refused.Count}");
            foreach (var line in damaged.Concat(refused).Order())
                TestContext.Out.WriteLine("SURVEY " + line);
        }
    }
}
