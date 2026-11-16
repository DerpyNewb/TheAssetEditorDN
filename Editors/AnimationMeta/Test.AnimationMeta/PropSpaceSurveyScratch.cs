using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: for vanilla ANIMATED_PROP tags, is the prop animation's root in the main model's space (at the
    // attach bone) or in the bone's own space (at the origin)? Counted per AttachMethod.   CA_CACHE_DB  PER_METHOD (default 60)
    public class PropSpaceSurveyScratch
    {
        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_PropSpaceSurvey()
        {
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);
            var pfs = runner.PackFileService;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var perMethod = int.Parse(Environment.GetEnvironmentVariable("PER_METHOD") ?? "400");
            var perSkeleton = new Dictionary<string, int>();

            var skeletons = new Dictionary<string, GameSkeleton?>();
            GameSkeleton? Skeleton(string name)
            {
                if (!skeletons.TryGetValue(name, out var s))
                {
                    var f = pfs.FindFile($"animations/skeletons/{name}.anim");
                    skeletons[name] = s = f == null ? null : new GameSkeleton(AnimationFile.Create(f), null!);
                }
                return s;
            }

            var done = new Dictionary<int, int>();
            var tally = new Dictionary<string, int>();
            var metas = ca.GetAllFiles().Keys.Where(x => x.EndsWith(".anm.meta") && x.StartsWith("animations\\battle")).OrderBy(x => x).ToList();
            TestContext.Progress.WriteLine($"metas {metas.Count}");
            foreach (var metaPath in metas)
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(pfs.FindFile(metaPath)!.DataSource.ReadData()); } catch { continue; }
                foreach (var tag in parsed.Attributes.OfType<IAnimatedPropMeta>())
                {
                    var attach = (int)tag.GetType().GetProperties().First(p => p.Name == "AttachMethod").GetValue(tag)!;
                    if (done.GetValueOrDefault(attach) >= perMethod || string.IsNullOrEmpty(tag.AnimationName) || tag.BoneId < 0)
                        continue;
                    try
                    {
                        var mainFile = pfs.FindFile(metaPath.Replace(".anm.meta", ".anim"));
                        var propFile = pfs.FindFile(tag.AnimationName);
                        var rmv = pfs.FindFile(tag.ModelName.Replace(".wsmodel", ".rigid_model_v2").Replace(".WSMODEL", ".rigid_model_v2"));
                        if (mainFile == null || propFile == null || rmv == null)
                            continue;
                        var mainAnim = AnimationFile.Create(mainFile);
                        var main = Skeleton(mainAnim.Header.SkeletonName);
                        var prop = Skeleton(ModelFactory.Create().Load(rmv.DataSource.ReadData()).Header.SkeletonName);
                        if (main == null || prop == null || tag.BoneId >= main.BoneCount)
                            continue;
                        var family = $"{attach}|{main.SkeletonName}|{prop.SkeletonName}";
                        if (perSkeleton.GetValueOrDefault(family) >= 2)
                            continue;
                        perSkeleton[family] = perSkeleton.GetValueOrDefault(family) + 1;
                        var mainClip = new AnimationClip(mainAnim, main);
                        var propClip = new AnimationClip(AnimationFile.Create(propFile), prop);
                        // Halfway through the tag, on both clocks.
                        var t = tag.StartTime + Math.Max(0, Math.Min(tag.EndTime, mainClip.PlayTimeInSec) - tag.StartTime) / 2;
                        var bone = AnimationSampler.Sample(Math.Clamp(t / mainClip.PlayTimeInSec, 0, 1), main, mainClip).GetSkeletonAnimatedWorld(main, tag.BoneId);
                        var root = AnimationSampler.Sample(Math.Clamp((t - tag.StartTime) / propClip.PlayTimeInSec, 0, 1), prop, propClip).GetSkeletonAnimatedWorld(prop, 0);
                        // The prop's centroid: in the main model's space it lies near the bone; in the bone's space, near the origin.
                        var propFrame = AnimationSampler.Sample(Math.Clamp((t - tag.StartTime) / propClip.PlayTimeInSec, 0, 1), prop, propClip);
                        // The first bone under the root: a flail's or whip's handle, held at the bone.
                        var centroid = propFrame.GetSkeletonAnimatedWorld(prop, Math.Min(1, prop.BoneCount - 1)).Translation;
                        var toBone = Vector3.Distance(centroid, bone.Translation);
                        var toOrigin = centroid.Length();
                        if (bone.Translation.Length() < 0.05f)
                            continue;
                        var verdict = Math.Abs(toBone - toOrigin) < 0.05f ? "same" : toBone < toOrigin ? "MODEL" : "LOCAL";
                        static float Angle(Quaternion a, Quaternion b) => MathHelper.ToDegrees(2 * MathF.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0, 1)));
                        root.Decompose(out _, out var rootRot, out _);
                        bone.Decompose(out _, out var boneRot, out _);
                        var rot = $"rotToBone={Angle(rootRot, boneRot):0} rotToId={Angle(rootRot, Quaternion.Identity):0}";
                        done[attach] = done.GetValueOrDefault(attach) + 1;
                        string Flag(string name) => tag.GetType().GetProperties().FirstOrDefault(p => p.Name == name)?.GetValue(tag)?.ToString() ?? "-";
                        var a = (ParsedMetadataAttribute)tag;
                        // Four candidate formulas, scored by how far each puts the handle from the attach bone.
                        var handle = propFrame.GetSkeletonAnimatedWorld(prop, Math.Min(1, prop.BoneCount - 1));
                        var rel = handle * Matrix.Invert(root);
                        var bindRoot = Matrix.CreateFromQuaternion(prop.Rotation[0]) * Matrix.CreateTranslation(prop.Translation[0]);
                        var scores = new (string Name, Matrix M)[] { ("model", handle), ("mult", handle * bone), ("replace", rel * bone), ("bindroot", rel * bindRoot * bone) }
                            .Select(x => (x.Name, D: Vector3.Distance(x.M.Translation, bone.Translation))).ToList();
                        var near = string.Join(",", scores.Where(x => x.D < 0.25f).Select(x => x.Name));
                        foreach (var s in scores.Where(x => x.D < 0.25f))
                            tally[$"attach={attach} ok:{s.Name}"] = tally.GetValueOrDefault($"attach={attach} ok:{s.Name}") + 1;
                        if (near.Length == 0)
                            tally[$"attach={attach} ok:none"] = tally.GetValueOrDefault($"attach={attach} ok:none") + 1;
                        rot += " near=" + (near.Length == 0 ? "none" : near) + " " + string.Join(" ", scores.Select(x => $"{x.Name}={x.D:0.00}"));
                        if (near.StartsWith("model"))
                        {
                            // Authored in model space: every bone's model position is the truth to match.
                            float Worst(Func<Matrix, Matrix> place) => Enumerable.Range(0, prop.BoneCount).Max(i =>
                            {
                                var w = propFrame.GetSkeletonAnimatedWorld(prop, i);
                                return Vector3.Distance(place(w * Matrix.Invert(root)).Translation, w.Translation);
                            });
                            var wr = Worst(r => r * bone);
                            var wb = Worst(r => r * bindRoot * bone);
                            tally[$"attach={attach} worst replace<0.05"] = tally.GetValueOrDefault($"attach={attach} worst replace<0.05") + (wr < 0.05f ? 1 : 0);
                            tally[$"attach={attach} worst bindroot<0.05"] = tally.GetValueOrDefault($"attach={attach} worst bindroot<0.05") + (wb < 0.05f ? 1 : 0);
                            tally[$"attach={attach} worst rows"] = tally.GetValueOrDefault($"attach={attach} worst rows") + 1;
                            rot += $" worstReplace={wr:0.000} worstBind={wb:0.000}";
                        }
                        var key = $"attach={attach} {verdict}";
                        tally[key] = tally.GetValueOrDefault(key) + 1;
                        TestContext.Out.WriteLine($"ROW attach={attach} {verdict,-5} toBone={toBone:0.000} toOrigin={toOrigin:0.000} {rot} skel={main.SkeletonName} prop={prop.SkeletonName} bone={tag.BoneId}:{main.BoneNames[tag.BoneId]} pos={tag.Position} {metaPath}");
                    }
                    catch (Exception e)
                    {
                        TestContext.Out.WriteLine($"ERR {metaPath}: {e.Message}");
                    }
                }
            }
            foreach (var (k, v) in tally.OrderBy(x => x.Key))
                TestContext.Out.WriteLine($"TALLY {k} {v}");
        }
    }
}
