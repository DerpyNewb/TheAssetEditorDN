using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.GameFormats.Animation;

namespace GameWorld.Core.Animation.Posing
{
    /// <summary>
    /// Joint limits measured from CA's clips, cached per skeleton. <c>&lt;skeleton&gt;.json</c> holds the measured limits and the
    /// stamp of the CA packs they came from, so a game patch makes them stale; <c>&lt;skeleton&gt;.overrides.json</c> holds the
    /// author's per-bone changes, which a re-measure never touches. Both are keyed by bone name, not index.
    /// </summary>
    public sealed class JointLimitStore(ISkeletonAnimationLookUpHelper lookup, string folder)
    {
        public static string DefaultFolder => Path.Combine(DirectoryHelper.ApplicationDirectory, "Animation", "JointLimits");

        static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        sealed class LimitDto
        {
            public float TwistCenter { get; set; }
            public float TwistHalfWidth { get; set; }
            public float[] SwingMin { get; set; } = [0, 0];
            public float[] SwingMax { get; set; } = [0, 0];
        }

        sealed class FileDto
        {
            public string? Stamp { get; set; }
            /// <summary>Bone name to limit. In an overrides file a null limit means the bone is free.</summary>
            public Dictionary<string, LimitDto?> Limits { get; set; } = [];
        }

        string MeasuredPath(GameSkeleton skeleton) => Path.Combine(folder, skeleton.SkeletonName + ".json");
        string OverridesPath(GameSkeleton skeleton) => Path.Combine(folder, skeleton.SkeletonName + ".overrides.json");

        /// <summary>The cached limits with the overrides applied; null when nothing was measured, the file is unreadable or the CA packs changed.</summary>
        public JointLimits? TryLoad(GameSkeleton skeleton)
        {
            var measured = Read(MeasuredPath(skeleton));
            if (measured == null || measured.Stamp == null || measured.Stamp != Stamp(CaReferences(skeleton)))
                return null;
            return ApplyOverrides(skeleton, ToLimits(skeleton, measured));
        }

        /// <summary>Measures every CA clip for the skeleton and caches the result. Null, with nothing written, when there are none.</summary>
        public Task<JointLimits?> MeasureAsync(GameSkeleton skeleton, IProgress<int>? progress, CancellationToken cancel) => Task.Run(() =>
        {
            var references = CaReferences(skeleton);
            if (references.Count == 0)
                return null;

            var measured = JointLimitMeasurer.Measure(skeleton, LoadClips(skeleton, references, progress, cancel), cancel: cancel);
            cancel.ThrowIfCancellationRequested();

            var dto = new FileDto { Stamp = Stamp(references) };
            foreach (var (bone, limit) in measured.Limits)
                dto.Limits[skeleton.BoneNames[bone]] = ToDto(limit);
            Write(MeasuredPath(skeleton), dto);
            return ApplyOverrides(skeleton, measured);
        }, cancel);

        /// <summary>Overrides one bone's limit; a null limit makes the bone free.</summary>
        public void SaveOverride(GameSkeleton skeleton, int bone, BoneLimit? limit)
        {
            var overrides = Read(OverridesPath(skeleton)) ?? new FileDto();
            overrides.Limits[skeleton.BoneNames[bone]] = limit == null ? null : ToDto(limit);
            Write(OverridesPath(skeleton), overrides);
        }

        /// <summary>Removes one bone's override, so its measured limit applies again.</summary>
        public void ClearOverride(GameSkeleton skeleton, int bone)
        {
            var overrides = Read(OverridesPath(skeleton));
            if (overrides != null && overrides.Limits.Remove(skeleton.BoneNames[bone]))
                Write(OverridesPath(skeleton), overrides);
        }

        List<AnimationReference> CaReferences(GameSkeleton skeleton) =>
            lookup.GetAnimationsForSkeleton(skeleton.SkeletonName).Where(x => x.Container?.IsCaPackFile == true).ToList();

        static IEnumerable<AnimationClip> LoadClips(GameSkeleton skeleton, List<AnimationReference> references, IProgress<int>? progress, CancellationToken cancel)
        {
            var loaded = 0;
            foreach (var reference in references)
            {
                cancel.ThrowIfCancellationRequested();
                AnimationClip? clip = null;
                try
                {
                    var file = reference.Container.FindFile(reference.AnimationFile);
                    if (file != null)
                        clip = new AnimationClip(AnimationFile.Create(file), skeleton);
                }
                catch (Exception)
                {
                    // ponytail: a clip that does not parse is skipped silently; log it if limits ever look too narrow.
                }
                progress?.Report(++loaded);
                if (clip != null)
                    yield return clip;
            }
        }

        /// <summary>The newest write time among the CA packs the clips come from; it changes with a game patch.</summary>
        static string Stamp(List<AnimationReference> references)
        {
            var newest = references.Select(x => x.Container.SystemFilePath).Distinct()
                .Where(x => x != null && File.Exists(x)).Select(x => File.GetLastWriteTimeUtc(x!))
                .DefaultIfEmpty(DateTime.MinValue).Max();
            return newest.ToString("o", CultureInfo.InvariantCulture) + "/" + references.Count;
        }

        JointLimits ApplyOverrides(GameSkeleton skeleton, JointLimits limits)
        {
            var overrides = Read(OverridesPath(skeleton));
            if (overrides == null)
                return limits;
            foreach (var (name, limit) in overrides.Limits)
            {
                var bone = skeleton.GetBoneIndexByName(name);
                if (bone >= 0)
                    limits = limits.WithOverride(bone, limit == null ? null : FromDto(limit));
            }
            return limits;
        }

        static JointLimits ToLimits(GameSkeleton skeleton, FileDto dto)
        {
            var limits = new Dictionary<int, BoneLimit>();
            foreach (var (name, limit) in dto.Limits)
            {
                var bone = skeleton.GetBoneIndexByName(name);
                if (bone >= 0 && limit != null)
                    limits[bone] = FromDto(limit);
            }
            return new JointLimits(skeleton.SkeletonName, limits);
        }

        static LimitDto ToDto(BoneLimit limit) => new()
        {
            TwistCenter = limit.TwistCenter,
            TwistHalfWidth = limit.TwistHalfWidth,
            SwingMin = [limit.SwingMin.X, limit.SwingMin.Y],
            SwingMax = [limit.SwingMax.X, limit.SwingMax.Y],
        };

        static BoneLimit FromDto(LimitDto dto) =>
            new(dto.TwistCenter, dto.TwistHalfWidth, new Vector2(dto.SwingMin[0], dto.SwingMin[1]), new Vector2(dto.SwingMax[0], dto.SwingMax[1]));

        static FileDto? Read(string path)
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions) : null;
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                return null;
            }
        }

        /// <summary>Written beside the target and moved over it, so a crash or a cancel never leaves half a file.</summary>
        static void Write(string path, FileDto dto)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions));
            File.Move(temp, path, true);
        }
    }
}
