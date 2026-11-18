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

        // Every version 8 .anim CA ships, saved and read back: each bone's translation and rotation on every frame of every part
        // must come back. The writer uses the uncompressed encodings only, so a ranged track CA wrote compressed comes back within
        // one step of a 16-bit rotation; a file CA itself wrote uncompressed, with empty range maps, comes back byte for byte,
        // which is the evidence that the game reads the encoding the writer uses.
        [Test]
        public void Warhammer3_EveryCaVersion8Animation_SavesAndReadsBackTheSamePose()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");

            var runner = new AssetEditorTestRunner();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            var files = 0;
            var multiPart = 0;
            var identical = 0;
            var worstTranslation = 0f;
            var worstRotation = 0f;
            var failed = new List<string>();
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, packName), true);
                foreach (var (path, file) in container.GetAllFiles())
                {
                    if (!path.EndsWith(".anim"))
                        continue;
                    var bytes = file.DataSource.ReadData();
                    if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != 8)
                        continue;

                    files++;
                    try
                    {
                        var original = AnimationFile.Create(file);
                        multiPart += original.AnimationParts.Count > 1 ? 1 : 0;
                        var saved = AnimationFile.ConvertToBytes(original);
                        if (saved.AsSpan().SequenceEqual(bytes))
                            identical++;
                        var reread = AnimationFile.Create(new Shared.ByteParsing.ByteChunk(saved));

                        Assert.That(reread.AnimationParts, Has.Count.EqualTo(original.AnimationParts.Count));
                        for (var p = 0; p < original.AnimationParts.Count; p++)
                        {
                            var (a, b) = (original.AnimationParts[p], reread.AnimationParts[p]);
                            Assert.That(b.DynamicFrames, Has.Count.EqualTo(a.DynamicFrames.Count));
                            Assert.That(b.PoseFrameCount, Is.EqualTo(a.PoseFrameCount));
                            var frames = Math.Max(1, a.DynamicFrames.Count);
                            for (var f = 0; f < frames; f++)
                            {
                                for (var bone = 0; bone < original.Bones.Length; bone++)
                                {
                                    var (ta, tb) = (Translation(a, f, bone), Translation(b, f, bone));
                                    Assert.That(ta.HasValue, Is.EqualTo(tb.HasValue));
                                    if (ta.HasValue)
                                        worstTranslation = Math.Max(worstTranslation, Math.Max(Math.Abs(ta.Value.X - tb!.Value.X), Math.Max(Math.Abs(ta.Value.Y - tb.Value.Y), Math.Abs(ta.Value.Z - tb.Value.Z))));
                                    var (ra, rb) = (Rotation(a, f, bone), Rotation(b, f, bone));
                                    Assert.That(ra.HasValue, Is.EqualTo(rb.HasValue));
                                    if (ra.HasValue)
                                        worstRotation = Math.Max(worstRotation, new[] { ra.Value.X - rb!.Value.X, ra.Value.Y - rb.Value.Y, ra.Value.Z - rb.Value.Z, ra.Value.W - rb.Value.W }.Max(Math.Abs));
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        failed.Add($"{path}: {e.Message}");
                    }
                }
            }

            TestContext.Out.WriteLine($"{files} v8 files, {multiPart} with more than one part, {identical} byte for byte; worst translation {worstTranslation}, worst rotation {worstRotation}");
            Assert.That(files, Is.GreaterThan(0), "no version 8 animation in the packs");
            Assert.That(failed, Is.Empty, $"{failed.Count} of {files} failed: {string.Join("\n", failed.Take(10))}");
            Assert.That(worstTranslation, Is.LessThanOrEqualTo(0f), "translations are written as floats and must not move");
            Assert.That(worstRotation, Is.LessThanOrEqualTo(0.5f / short.MaxValue + 1e-6f), "a rotation moved more than half a 16-bit step");
        }

        static Shared.GameFormats.RigidModel.Transforms.RmvVector3? Translation(AnimationFile.AnimationPart part, int frame, int bone)
        {
            var m = part.TranslationMappings[bone];
            return m.IsStatic ? part.StaticFrame!.Transforms[m.Id] : m.IsDynamic ? part.DynamicFrames[frame].Transforms[m.Id] : null;
        }

        static Shared.GameFormats.RigidModel.Transforms.RmvVector4? Rotation(AnimationFile.AnimationPart part, int frame, int bone)
        {
            var m = part.RotationMappings[bone];
            return m.IsStatic ? part.StaticFrame!.Quaternion[m.Id] : m.IsDynamic ? part.DynamicFrames[frame].Quaternion[m.Id] : null;
        }
    }
}
