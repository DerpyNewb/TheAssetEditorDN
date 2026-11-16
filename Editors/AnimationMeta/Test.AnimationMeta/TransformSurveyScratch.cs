using System.IO;
using GameWorld.Core.Animation;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;
using Microsoft.Xna.Framework;

namespace Test.AnimationMeta
{
    // Scratch, not committed: how vanilla uses TRANSFORM, by target and source bone name.
    public class TransformSurveyScratch
    {
        [Test, Explicit]
        public void Scratch_TransformSurvey()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var containers = new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .Select(p => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, p), true)).ToList();
            var skeletons = new Dictionary<string, string[]?>();
            string[]? Bones(string name)
            {
                if (skeletons.TryGetValue(name, out var b)) return b;
                foreach (var c in containers)
                {
                    var f = c.FindFile($"animations/skeletons/{name}.anim");
                    if (f == null) continue;
                    try { var a = AnimationFile.Create(f); return skeletons[name] = a.Bones.Select(x => x.Name).ToArray(); } catch { }
                }
                return skeletons[name] = null;
            }

            var pairs = new Dictionary<string, int>();
            var examples = new Dictionary<string, string>();
            int total = 0, withSource = 0, toProp = 0, rotated = 0, moved = 0, timed = 0;
            foreach (var c in containers)
            foreach (var (path, file) in c.GetAllFiles().Where(x => x.Key.EndsWith(".anm.meta")))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                foreach (var t in parsed.GetItemsOfType<Transform_v10>())
                {
                    total++;
                    var parts = path.Replace('\\', '/').Split('/');
                    var skel = parts.Length > 2 ? parts[2] : "?";
                    var bones = Bones(skel);
                    string Name(int i) => bones != null && i >= 0 && i < bones.Length ? bones[i] : $"#{i}";
                    var target = Name(t.TargetNode);
                    var source = Name(t.SourceNode);
                    if (t.SourceNode != t.TargetNode && t.SourceNode != 0 && t.SourceNode != -1) withSource++;
                    if (target.StartsWith("be_prop")) toProp++;
                    var e = MathUtil.QuaternionToEulerDegree(new Microsoft.Xna.Framework.Quaternion(t.Orientation));
                    if (e.Length() > 0.5f) rotated++;
                    if (t.Position.Length() > 0.001f) moved++;
                    if (t.EndTime > t.StartTime) timed++;
                    var key = $"{skel} target={target} source={source}";
                    pairs[key] = pairs.GetValueOrDefault(key) + 1;
                    examples.TryAdd(key, $"{path} t={t.StartTime:0.00}-{t.EndTime:0.00} pos={t.Position} rot={e} blend={t.BlendInTime}/{t.BlendOutTime} src#={t.SourceNode} tgt#={t.TargetNode}");
                }
            }
            TestContext.Out.WriteLine($"TOTAL {total} withOtherSource {withSource} targetsProp {toProp} rotated {rotated} moved {moved} timedWindow {timed}");
            foreach (var (k, n) in pairs.Where(x => x.Key.Contains("be_prop")).OrderByDescending(x => x.Value))
                TestContext.Out.WriteLine($"PROP {n,5} {k}  e.g. {examples[k]}");
            foreach (var (k, n) in pairs.OrderByDescending(x => x.Value).Take(0))
                TestContext.Out.WriteLine($"PAIR {n,5} {k}  e.g. {examples[k]}");
        }
    
        [Test, Explicit]
        public void Scratch_TransformPropProbe()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var containers = new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .Select(p => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, p), true)).ToList();
            PackFile? Find(string path) => containers.Select(c => c.FindFile(path)).FirstOrDefault(x => x != null);

            var cases = new List<(string meta, string skel)>();
            foreach (var c in containers)
            foreach (var (path, file) in c.GetAllFiles().Where(x => x.Key.EndsWith(".anm.meta")))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                var props = parsed.GetItemsOfType<Transform_v10>().Where(t => t.TargetNode >= 1 && t.TargetNode <= 6).ToList();
                if (props.Count == 0) continue;
                var docks = parsed.GetItemsOfType<DockEquipment>().Select(d => $"{d.AnimationSlotName}:{d.PropBoneId}@{d.StartTime:0.00}").ToList();
                var skel = path.Replace('\\', '/').Split('/')[2];
                var skelFileX = containers.Select(cc => cc.FindFile($"animations/skeletons/{path.Replace("\\", "/").Split('/')[2]}.anim")).FirstOrDefault(x => x != null);
                if (skelFileX != null)
                {
                    var names = AnimationFile.Create(skelFileX).Bones.Select(x => x.Name).ToArray();
                    foreach (var t in props)
                    foreach (var d in parsed.GetItemsOfType<DockEquipment>())
                    {
                        var docked = Array.IndexOf(names, "be_prop_" + (d.PropBoneId - 1));
                        if (docked != t.TargetNode) continue;
                        var dockEnd = d.EndTime;
                        var overlap = t.EndTime >= d.StartTime || t.StartTime >= d.StartTime;
                        TestContext.Out.WriteLine($"SAMEBONE {path} {names[t.TargetNode]} transform {t.StartTime:0.00}-{t.EndTime:0.00} blend {t.BlendInTime}/{t.BlendOutTime} rot={MathUtil.QuaternionToEulerDegree(new Quaternion(t.Orientation))} pos={t.Position}  dock {d.AnimationSlotName} {d.StartTime:0.00}-{d.EndTime:0.00} overlap={overlap}");
                    }
                }
                TestContext.Out.WriteLine($"DOCKS {path} transforms={string.Join(",", props.Select(t => t.TargetNode))} docks=[{string.Join(" ", docks)}]");
                cases.Add((path.Replace('\\', '/'), skel));
            }

            foreach (var (meta, skelName) in cases.Take(40))
            {
                var animPath = meta.Replace(".anm.meta", ".anim");
                var animFile = Find(animPath); var skelFile = Find($"animations/skeletons/{skelName}.anim");
                if (animFile == null || skelFile == null) { TestContext.Out.WriteLine($"PROBE skip {meta} anim={animFile != null} skel={skelFile != null}"); continue; }
                var skeleton = new GameSkeleton(AnimationFile.Create(skelFile), null!);
                AnimationClip clip;
                try { clip = new AnimationClip(AnimationFile.Create(animFile), skeleton); } catch (Exception e) { TestContext.Out.WriteLine($"PROBE skip {meta} {e.Message}"); continue; }
                var parsed = parser.ParseFile(Find(meta)!.DataSource.ReadData());
                foreach (var t in parsed.GetItemsOfType<Transform_v10>().Where(t => t.TargetNode >= 1 && t.TargetNode <= 6))
                {
                    var bone = t.TargetNode;
                    var name = skeleton.BoneNames[bone];
                    var before = AnimationSampler.Sample(0.5f, skeleton, clip).BoneTransforms[bone].WorldTransform;
                    var after = AnimationSampler.Sample(0.5f, skeleton, clip, new List<GameWorld.Core.Animation.AnimationChange.IAnimationChangeRule> { new Editors.AnimationMeta.SuperView.Visualisation.Rules.TransformBoneRule(t) }).BoneTransforms[bone].WorldTransform;
                    before.Decompose(out _, out var rb, out var tb); after.Decompose(out _, out var ra, out var ta);
                    var angle = MathHelper.ToDegrees(2 * MathF.Acos(Math.Min(1f, Math.Abs(Quaternion.Dot(rb, ra)))));
                    var want = MathHelper.ToDegrees(2 * MathF.Acos(Math.Min(1f, Math.Abs(new Quaternion(t.Orientation).W))));
                    TestContext.Out.WriteLine($"PROBE {meta} {name} tagTurn={want:0.0} boneTurned={angle:0.0} moved={(ta - tb).Length():0.000} tagMove={t.Position.Length():0.000} parent={skeleton.GetParentBoneIndex(bone)}");
                }
            }
        }
    
        [Test, Explicit]
        public void Scratch_TransformTiming()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var containers = new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .Select(p => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, p), true)).ToList();
            var cats = new Dictionary<string, int>();
            var ex = new Dictionary<string, string>();
            void Add(string k, string e) { cats[k] = cats.GetValueOrDefault(k) + 1; ex.TryAdd(k, e); }
            foreach (var c in containers)
            foreach (var (path, file) in c.GetAllFiles().Where(x => x.Key.EndsWith(".anm.meta")))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                var all = parsed.GetItemsOfType<Transform_v10>().ToList();
                foreach (var t in all)
                {
                    var e = $"{path} {t.StartTime:0.000}-{t.EndTime:0.000} blend {t.BlendInTime}/{t.BlendOutTime} n={all.Count(x => x.TargetNode == t.TargetNode)} sameBone";
                    var b = t.BlendInTime > 0 || t.BlendOutTime > 0 ? "blend" : "noblend";
                    if (t.StartTime == 0 && t.EndTime == 0) Add($"s=e=0 {b}", e);
                    else if (t.StartTime == t.EndTime) Add($"s=e>0 {b}", e);
                    else if (t.EndTime > t.StartTime)
                    {
                        var w = t.EndTime - t.StartTime;
                        Add($"e>s {b} {(t.BlendInTime + t.BlendOutTime > w + 1e-4 ? "blendsExceedWindow" : "blendsFit")}", e);
                    }
                    else if (t.EndTime == 0) Add($"e=0<s {b}", e);
                    else Add($"e<s {b}", e);
                    if (all.Count(x => x.TargetNode == t.TargetNode) > 1) Add("sharesBoneWithAnother", e);
                    var len = t.Orientation.Length();
                    if (Math.Abs(len - 1) > 0.01f) Add($"nonUnitOrientation", e + $" q={t.Orientation} len={len:0.000}");
                }
            }
            foreach (var (k, n) in cats.OrderByDescending(x => x.Value))
                TestContext.Out.WriteLine($"TIMING {n,5} {k}  e.g. {ex[k]}");
        }
    
        [Test, Explicit]
        public void Scratch_SpliceSurvey()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var containers = new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .Select(p => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, p), true)).ToList();
            var cats = new Dictionary<string, int>();
            var ex = new Dictionary<string, string>();
            void Add(string k, string e) { cats[k] = cats.GetValueOrDefault(k) + 1; ex.TryAdd(k, e); }
            foreach (var c in containers)
            foreach (var (path, file) in c.GetAllFiles().Where(x => x.Key.EndsWith(".anm.meta")))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                foreach (var t in parsed.GetItemsOfType<Splice_v11>().Where(x => x is not SpliceOverride_v11 && x is not SpliceOverride_v12))
                {
                    var e = $"{path} {t.StartTime:0.00}-{t.EndTime:0.00} anim={t.Animation} ref={t.ReferenceAnimation} bone={t.GenericBoneIndex} depth={t.GenericBoneDepth} gw={t.GenericWeight} n/t/l/r/legs={t.NeckWeight}/{t.TorsoWeight}/{t.LeftArmWeight}/{t.RightArmWeight}/{t.LegsWeight} blend={t.BlendInTime}/{t.BlendOutTime} add={t.Additive} wp={t.WeaponPose} st={t.Stretch} ex={t.ExcludeStripped}";
                    var parts = string.Join("", new[] { ("N", t.NeckWeight), ("T", t.TorsoWeight), ("L", t.LeftArmWeight), ("R", t.RightArmWeight), ("G", t.LegsWeight) }.Where(x => x.Item2 != 0).Select(x => x.Item1));
                    var shape = parts.Length > 0 ? $"bodyParts[{parts}]" : t.GenericBoneIndex >= 0 && t.GenericWeight > 0 ? "genericBone" : "noWeight";
                    Add($"shape {shape}", e);
                    if (t.Additive != "false") Add("additive", e);
                    if (t.ReferenceAnimation.Length != 0) Add("referenceAnim", e);
                    if (t.WeaponPose != "false") Add("weaponPose", e);
                    if (t.Stretch != "false") Add("stretch", e);
                    if (t.ExcludeStripped != "false") Add("excludeStripped", e);
                    if (t.GenericBoneDepth != 0) Add("depth!=0", e);
                    Add(t.StartTime == 0 && t.EndTime == 0 ? "time 0-0" : t.EndTime > t.StartTime ? "time window" : "time other", e);
                    var folder = t.Animation.Replace('\\', '/').Split('/');
                    Add("animFolder " + (folder.Length > 3 ? folder[^2] : "?"), e);
                }
            }
            foreach (var (k, n) in cats.OrderByDescending(x => x.Value).Take(40))
                TestContext.Out.WriteLine($"SPLICE {n,5} {k}  e.g. {ex[k]}");
        }
    
        [Test, Explicit]
        public void Scratch_SkeletonRows()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var c = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, "db.pack"), true);
            TestContext.Out.WriteLine($"SR load {sw.ElapsedMilliseconds} ms");
            runner.PackFileService.AddContainer(c);
            TestContext.Out.WriteLine($"SR containers {runner.PackFileService.GetAllPackfileContainers().Count}");
            var folders = c.GetAllFilesByFolder();
            TestContext.Out.WriteLine($"SR folders {folders.Count} {sw.ElapsedMilliseconds} ms; " + string.Join(" | ", folders.Keys.Where(k => k.Contains("skeleton")).Take(5)));
            var problems = new List<string>();
            var rows = Shared.GameFormats.DB.DbTables.Read(runner.PackFileService, "battle_skeletons_tables", problems);
            TestContext.Out.WriteLine($"SR rows {rows.Count} {sw.ElapsedMilliseconds} ms problems {string.Join(" / ", problems)}");
            foreach (var r in rows.Take(3)) TestContext.Out.WriteLine($"SR row {r.Text("name")} neck={r.Text("neck_node")}");
        }

        [Test, Explicit]
        public void Scratch_ZeroWindowBlends()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var containers = new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .Select(p => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, p), true)).ToList();
            var cats = new Dictionary<string, int>();
            var ex = new Dictionary<string, string>();
            void Add(string k, string e) { cats[k] = cats.GetValueOrDefault(k) + 1; ex.TryAdd(k, e); }
            foreach (var c in containers)
            foreach (var (path, file) in c.GetAllFiles().Where(x => x.Key.EndsWith(".anm.meta")))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                var persistent = path.Contains("persistent", StringComparison.OrdinalIgnoreCase) ? "persistent" : "anim";
                foreach (var t in parsed.Attributes.OfType<DecodedMetaEntryBase>().Where(x => x is Splice_v11 or Transform_v10))
                {
                    var (bi, bo) = t is Splice_v11 s ? (s.BlendInTime, s.BlendOutTime) : t is Transform_v10 tr ? (tr.BlendInTime, tr.BlendOutTime) : (0, 0);
                    var kind = t is Splice_v11 ? "SPLICE" : "TRANSFORM";
                    var window = t.StartTime == 0 && t.EndTime == 0 ? "0-0" : t.EndTime > t.StartTime ? "window" : t.EndTime == t.StartTime ? "instant" : "end<start";
                    Add($"{kind} {window} blends={(bi > 0 || bo > 0 ? "yes" : "no")} {persistent}", $"{path} {t.StartTime:0.00}-{t.EndTime:0.00} blend {bi:0.00}/{bo:0.00}");
                    if (t is Splice_v11 sp && window == "0-0" && (bi > 0 || bo > 0))
                        Add($"ZB splice blend {bi:0.00}/{bo:0.00} anim={sp.Animation.Split('/').Last()[..Math.Min(18, sp.Animation.Split('/').Last().Length)]} add={sp.Additive}", path);
                }
            }
            foreach (var (k, n) in cats.OrderBy(x => x.Key))
                TestContext.Out.WriteLine($"ZW {n,5} {k}  e.g. {ex[k]}");
        }

        [Test, Explicit]
        public void Scratch_SpliceDepth()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var containers = new[] { "anim.pack", "anim2.pack", "anim_3.pack" }
                .Select(p => loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, p), true)).ToList();
            var skels = new Dictionary<string, AnimationFile?>();
            AnimationFile? Skel(string n) { if (!skels.TryGetValue(n, out var f)) { var pf = containers.Select(c => c.FindFile($"animations/skeletons/{n}.anim")).FirstOrDefault(x => x != null); skels[n] = f = pf == null ? null : AnimationFile.Create(pf); } return f; }
            var seen = new Dictionary<string, int>();
            foreach (var c in containers)
            foreach (var (path, file) in c.GetAllFiles().Where(x => x.Key.EndsWith(".anm.meta")))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                foreach (var t in parsed.GetItemsOfType<Splice_v11>().Where(x => x.GenericBoneDepth != 0 && x.GenericBoneIndex >= 0))
                {
                    var skelName = path.Replace(@"\", "/").Split('/')[2];
                    var sk = Skel(skelName);
                    if (sk == null) continue;
                    var parents = sk.Bones.Select(b => b.ParentId).ToArray();
                    int Level(int b, int root) { var l = 0; while (b != root && b >= 0) { b = parents[b]; l++; } return b == root ? l : -1; }
                    var byLevel = Enumerable.Range(0, parents.Length).Select(b => Level(b, t.GenericBoneIndex)).Where(l => l > 0).GroupBy(l => l).OrderBy(g => g.Key).Select(g => $"L{g.Key}:{g.Count()}");
                    var key = $"{skelName} bone={t.GenericBoneIndex}({(t.GenericBoneIndex < sk.Bones.Length ? sk.Bones[t.GenericBoneIndex].Name : "?")}) depth={t.GenericBoneDepth} below=[{string.Join(" ", byLevel)}] anim={t.Animation.Split('/').Last()}";
                    seen[key] = seen.GetValueOrDefault(key) + 1;
                }
            }
            foreach (var (k, n) in seen.OrderByDescending(x => x.Value).Take(30))
                TestContext.Out.WriteLine($"DEPTH {n,5} {k}");
            TestContext.Out.WriteLine($"DEPTHVALUES " + string.Join(" ", seen.GroupBy(x => x.Key.Split("depth=")[1].Split(' ')[0]).Select(g => $"{g.Key}:{g.Sum(x => x.Value)}")));
        }
    }
}
