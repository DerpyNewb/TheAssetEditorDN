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
    // Every .anim CA ships in a version the writer takes (5 to 7), read and written back. Two things changed them: rotations
    // were truncated on write, so a value read as s / 32767 went back as s - 1, and a pose (a static frame, no dynamic
    // frames) always got a frame count of 3, where CA writes how long it is held, round(time x 20) + 1. Reads anim.pack,
    // anim2.pack and anim_3.pack from WH3_DATA; skipped without it.
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
                var heldPose = anim.AnimationParts.Single().DynamicFrames.Count == 0 && anim.AnimationParts[0].PoseFrameCount != 3;
                results.Add((clip.Path, heldPose, !AnimationFile.ConvertToBytes(anim).AsSpan().SequenceEqual(bytes)));
            });

            var files = results.Count;
            var changed = results.Where(x => x.Changed).Select(x => x.Path).Order().ToList();
            Assert.That(files, Is.GreaterThan(8000), "the packs held fewer v5-v7 animations than the 8,305 this was written against");
            Assert.That(results.Count(x => x.HeldPose), Is.GreaterThan(0), "no pose held longer than 3 frames, so nothing here tests the pose frame count");
            Assert.That(changed, Is.Empty, $"{changed.Count} of {files} changed");
        }

        // Every version 8 .anim CA ships, saved and read back: each bone's translation and rotation on every frame of every part
        // must come back. The writer uses the uncompressed encodings only, so a ranged track CA wrote compressed comes back within
        // one step of a 16-bit rotation; a file CA itself wrote uncompressed, with empty range maps, comes back byte for byte,
        // which is the evidence that the game reads the encoding the writer uses.
        [Test]
        public void Warhammer3_EveryCaVersion8Animation_SavesAndReadsBackTheSamePose()
        {
            var results = new ConcurrentBag<Version8Result>();
            Parallel.ForEach(CaClips(), clip =>
            {
                if (Version8RoundTrip(clip.Path, clip.File.DataSource.ReadData()) is { } result)
                    results.Add(result);
            });

            var files = results.Count;
            var failed = results.Where(x => x.Failure != null).Select(x => x.Failure!).Order().ToList();
            var worstTranslation = results.Select(x => x.WorstTranslation).DefaultIfEmpty().Max();
            var worstRotation = results.Select(x => x.WorstRotation).DefaultIfEmpty().Max();
            TestContext.Out.WriteLine($"{files} v8 files, {results.Count(x => x.MultiPart)} with more than one part, {results.Count(x => x.Identical)} byte for byte; " +
                $"worst translation {worstTranslation}, worst rotation {worstRotation}");
            Assert.That(files, Is.GreaterThan(0), "no version 8 animation in the packs");
            Assert.That(failed, Is.Empty, $"{failed.Count} of {files} failed: {string.Join("\n", failed.Take(10))}");
            Assert.That(worstTranslation, Is.LessThanOrEqualTo(0f), "translations are written as floats and must not move");
            Assert.That(worstRotation, Is.LessThanOrEqualTo(0.5f / short.MaxValue + 1e-6f), "a rotation moved more than half a 16-bit step");
        }

        // Every .anim in the game's three anim packs; skipped without WH3_DATA. The tests check them on every core at once, and without
        // an NUnit assert per bone per frame: done one by one with asserts, the version 8 test took seven minutes.
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

        record Version8Result(bool MultiPart, bool Identical, float WorstTranslation, float WorstRotation, string? Failure);

        // One clip saved and read back: null when it is not version 8, else how far its pose moved, or the first thing that differs.
        static Version8Result? Version8RoundTrip(string path, byte[] bytes)
        {
            if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != 8)
                return null;
            try
            {
                var original = AnimationFile.Create(new ByteChunk(bytes));
                var saved = AnimationFile.ConvertToBytes(original);
                var reread = AnimationFile.Create(new ByteChunk(saved));
                var (worstTranslation, worstRotation) = (0f, 0f);

                string? FirstDifference()
                {
                    if (reread.AnimationParts.Count != original.AnimationParts.Count)
                        return $"{reread.AnimationParts.Count} parts read back of {original.AnimationParts.Count}";
                    for (var p = 0; p < original.AnimationParts.Count; p++)
                    {
                        var (a, b) = (original.AnimationParts[p], reread.AnimationParts[p]);
                        if (b.DynamicFrames.Count != a.DynamicFrames.Count)
                            return $"part {p}: {b.DynamicFrames.Count} frames read back of {a.DynamicFrames.Count}";
                        if (b.PoseFrameCount != a.PoseFrameCount)
                            return $"part {p}: pose frame count {b.PoseFrameCount}, saved as {a.PoseFrameCount}";
                        for (var f = 0; f < Math.Max(1, a.DynamicFrames.Count); f++)
                        {
                            for (var bone = 0; bone < original.Bones.Length; bone++)
                            {
                                var (ta, tb) = (Translation(a, f, bone), Translation(b, f, bone));
                                if (ta.HasValue != tb.HasValue)
                                    return $"part {p} frame {f} bone {bone}: translation {(tb.HasValue ? "appeared" : "lost")}";
                                if (ta.HasValue)
                                    worstTranslation = Math.Max(worstTranslation, Math.Max(Math.Abs(ta.Value.X - tb!.Value.X), Math.Max(Math.Abs(ta.Value.Y - tb.Value.Y), Math.Abs(ta.Value.Z - tb.Value.Z))));
                                var (ra, rb) = (Rotation(a, f, bone), Rotation(b, f, bone));
                                if (ra.HasValue != rb.HasValue)
                                    return $"part {p} frame {f} bone {bone}: rotation {(rb.HasValue ? "appeared" : "lost")}";
                                if (ra.HasValue)
                                    worstRotation = Math.Max(worstRotation, Math.Max(Math.Max(Math.Abs(ra.Value.X - rb!.Value.X), Math.Abs(ra.Value.Y - rb.Value.Y)),
                                        Math.Max(Math.Abs(ra.Value.Z - rb.Value.Z), Math.Abs(ra.Value.W - rb.Value.W))));
                            }
                        }
                    }
                    return null;
                }

                var difference = FirstDifference();
                return new Version8Result(original.AnimationParts.Count > 1, saved.AsSpan().SequenceEqual(bytes), worstTranslation, worstRotation,
                    difference == null ? null : $"{path}: {difference}");
            }
            catch (Exception e)
            {
                return new Version8Result(false, false, 0, 0, $"{path}: {e.Message}");
            }
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
