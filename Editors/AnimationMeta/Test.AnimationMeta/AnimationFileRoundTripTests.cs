using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Every .anim CA ships in a version the writer takes (5 to 7), read and written back. Two things changed them: rotations
    // were truncated on write, so a value read as s / 32767 went back as s - 1, and a pose (a static frame, no dynamic
    // frames) always got a frame count of 3, where CA writes how long it is held, round(time x 20) + 1. Reads anim.pack,
    // anim2.pack and anim_3.pack from WH3_DATA; skipped without it.
    public class AnimationFileRoundTripTests
    {
        [Test]
        public void Warhammer3_EveryCaAnimationTheWriterTakes_ComesBackByteForByte()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");

            var runner = new AssetEditorTestRunner();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            var files = 0;
            var heldPoses = 0;
            var changed = new List<string>();
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, packName), true);
                foreach (var (path, file) in container.GetAllFiles())
                {
                    if (!path.EndsWith(".anim"))
                        continue;
                    var bytes = file.DataSource.ReadData();
                    if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) is not (5 or 6 or 7))
                        continue;

                    files++;
                    var anim = AnimationFile.Create(file);
                    if (anim.AnimationParts.Single().DynamicFrames.Count == 0 && anim.AnimationParts[0].PoseFrameCount != 3)
                        heldPoses++;
                    if (!AnimationFile.ConvertToBytes(anim).AsSpan().SequenceEqual(bytes))
                        changed.Add(path);
                }
            }

            Assert.That(files, Is.GreaterThan(8000), "the packs held fewer v5-v7 animations than the 8,305 this was written against");
            Assert.That(heldPoses, Is.GreaterThan(0), "no pose held longer than 3 frames, so nothing here tests the pose frame count");
            Assert.That(changed, Is.Empty, $"{changed.Count} of {files} changed");
        }
    }
}
