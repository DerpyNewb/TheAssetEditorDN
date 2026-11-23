using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Every .anim CA ships in versions 5 to 7, read and written back. Two things changed them: rotations were truncated on
    // write, so a value read as s / 32767 went back as s - 1, and a pose (a static frame, no dynamic frames) always got a
    // frame count of 3, where CA writes how long it is held, round(time x 20) + 1. Reads anim.pack, anim2.pack and
    // anim_3.pack from WH3_DATA; skipped without it. Version 8 is AnimationFileV8WriterTests' whole-game test.
    public class AnimationFileRoundTripTests
    {
        [Test]
        public void Warhammer3_EveryCaAnimationTheWriterTakes_ComesBackByteForByte()
        {
            var clips = CaClips();
            var results = new ConcurrentBag<(string Path, bool HeldPose, bool Changed)>();
            Parallel.ForEach(clips, clip =>
            {
                var bytes = clip.File.DataSource.ReadData();
                if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) is not (5 or 6 or 7))
                    return;
                var anim = AnimationFile.Create(new ByteChunk(bytes));
                var heldPose = anim.AnimationParts.Single().DeclaredFrameCount is not (null or 3);
                results.Add((clip.Path, heldPose, !AnimationFile.ConvertToBytes(anim).AsSpan().SequenceEqual(bytes)));
            });

            var files = results.Count;
            var changed = results.Where(x => x.Changed).Select(x => x.Path).Order().ToList();
            Assert.That(files, Is.GreaterThan(8000), "the packs held fewer v5-v7 animations than the 8,305 this was written against");
            Assert.That(results.Count(x => x.HeldPose), Is.GreaterThan(0), "no pose held longer than 3 frames, so nothing here tests the pose frame count");
            Assert.That(changed, Is.Empty, $"{changed.Count} of {files} changed");
        }

        // Every .anim in the game's three anim packs; skipped without WH3_DATA. Checked on every core at once.
        static List<(string Path, PackFile File)> CaClips()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");
            var loader = new AssetEditorTestRunner().ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            return new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .SelectMany(x => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, x), true).GetAllFiles())
                .Where(x => x.Key.EndsWith(".anim"))
                .Select(x => (x.Key, x.Value))
                .ToList();
        }
    }
}
