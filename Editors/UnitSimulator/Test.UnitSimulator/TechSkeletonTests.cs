using Editors.UnitSimulator.Personalities;
using Editors.UnitSimulator.Tables;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.DB;

namespace Test.UnitSimulator
{
    // The personalities design's working facts (spec section 6, test 1), measured before anything is built on them. A fail sends
    // the design back to the author.
    public class TechSkeletonTests
    {
        static Dictionary<string, DbRow> ByKey(IEnumerable<DbRow> rows, string column) =>
            rows.GroupBy(x => x.Text(column)).ToDictionary(x => x.Key, x => x.Last());

        // Why a tech .anim is not built on its animation skeleton, or null: every animated bone keeps its name and parent at its index.
        static string? NotBuiltOn(AnimationFile.BoneInfo[] animation, AnimationFile.BoneInfo[] tech)
        {
            if (tech.Length < animation.Length)
                return $"{tech.Length} bones, its animation skeleton has {animation.Length}";
            for (var i = 0; i < animation.Length; i++)
                if (tech[i].Name != animation[i].Name || tech[i].ParentId != animation[i].ParentId)
                    return $"bone {i} is {tech[i].Name} under {tech[i].ParentId}, the animation skeleton's is {animation[i].Name} under {animation[i].ParentId}";
            return null;
        }

        // 1a, narrowed by the author 2026-10-13 after the first run: a tech folder also holds destruction animations (on a "building"
        // skeleton that does not exist), a proxy, a turret part and a skeleton of another version. Those are listed and counted, as 1b
        // counts engines, and the simulator does not offer them; every tech .anim built on its animation skeleton is gated. An ap_ bone
        // on no parent (Nagash's ap_turret) is an appended attach bone too.
        [Test]
        public void EveryVanillaTechSkeleton_BuiltOnItsAnimationSkeleton_AppendsApBones()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var folders = DbTables.Read(pfs, "variants_tables").Select(x => x.Text("tech_folder")).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var byFolder = folders.ToDictionary(x => x, x => TechFolders.Skeletons(pfs, x), StringComparer.OrdinalIgnoreCase);
            var skeletons = byFolder.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var (notBuiltOn, problems) = (new List<string>(), new List<string>());
            var added = 0;
            foreach (var path in skeletons)
            {
                var tech = AnimationFile.Create(pfs.FindFile(path)!);
                if (pfs.FindFile($@"animations\skeletons\{tech.Header.SkeletonName}.anim") is not { } animationFile)
                {
                    notBuiltOn.Add($"{path}: no animations/skeletons/{tech.Header.SkeletonName}.anim");
                    continue;
                }
                var animation = AnimationFile.Create(animationFile).Bones;
                if (NotBuiltOn(animation, tech.Bones) is { } why)
                {
                    notBuiltOn.Add($"{path}: {why}");
                    continue;
                }
                for (var i = animation.Length; i < tech.Bones.Length; i++)
                    if (!tech.Bones[i].Name.StartsWith("ap_", StringComparison.Ordinal) || tech.Bones[i].ParentId < -1 || tech.Bones[i].ParentId >= animation.Length)
                        problems.Add($"{path}: bone {i}, {tech.Bones[i].Name} under {tech.Bones[i].ParentId}, is not an ap_ bone on an animated one or the root");
                added += tech.Bones.Length - animation.Length;
                // The tab builds each file it offers as a GameSkeleton, which reads every bone's pose from frame 0.
                try { _ = new GameSkeleton(tech, null); }
                catch (ArgumentOutOfRangeException) { problems.Add($"{path}: no frame 0 with a pose for every bone"); }
            }

            TestContext.Out.WriteLine($"{folders.Count} tech folders, {byFolder.Count(x => x.Value.Count == 0)} with no .anim in the loaded packs, " +
                $"{skeletons.Count} tech .anim files, {notBuiltOn.Count} not built on their animation skeleton (counted, not gated), " +
                $"{added} bones appended to the rest, {problems.Count} problems");
            foreach (var line in notBuiltOn.Concat(problems).Take(30))
                TestContext.Out.WriteLine(line);
            Assert.That(skeletons, Has.Count.EqualTo(126));
            Assert.That(notBuiltOn, Has.Count.EqualTo(6));
            Assert.That(problems, Is.Empty);
        }

        // 1b. Units with an engine are counted, not gated: engines are sub-project 3. Narrowed by the author 2026-10-13 after the first
        // run: a hero who is his own personality (its key is the land unit's), playing a mounted rider table on a bone his mount has no
        // skeleton for (two Engineers on the Mechanical Steed, Caradryan), is counted, not gated. The simulator puts such a part at the
        // mount's origin, a plain rider's seat; that the game does the same is not measured.
        [Test]
        public void EveryMountOnlyUnitsAttachPoints_AreBonesOfItsMountsSkeletons()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var units = ByKey(DbTables.Read(pfs, "land_units_tables"), "key");
            var mounts = ByKey(DbTables.Read(pfs, "mounts_tables"), "key");
            var variants = ByKey(DbTables.Read(pfs, "variants_tables"), "variant_name");
            var tables = AnimTableEdits.AllByName(pfs);
            var (mountOnly, withEngine) = (0, 0);
            var (ownRider, problems) = (new List<string>(), new List<string>());
            foreach (var unit in DbTables.Read(pfs, "land_units_to_battle_personalities_junctions_tables").GroupBy(x => x.Text("land_unit")))
            {
                var row = units[unit.Key];
                if (row.Text("engine") != "")
                {
                    withEngine++;
                    continue;
                }
                if (row.Text("mount") == "")
                    continue;
                mountOnly++;
                var mount = mounts[row.Text("mount")];
                var bones = new HashSet<string>();
                if (tables.TryGetValue(mount.Text("animation"), out var table) && pfs.FindFile($@"animations\skeletons\{table.SkeletonName}.anim") is { } skeleton)
                    bones.UnionWith(AnimationFile.Create(skeleton).Bones.Select(x => x.Name));
                if (variants.TryGetValue(mount.Text("variant"), out var variant) && variant.Text("tech_folder") != "")
                    foreach (var path in TechFolders.Skeletons(pfs, variant.Text("tech_folder")))
                        bones.UnionWith(AnimationFile.Create(pfs.FindFile(path)!).Bones.Select(x => x.Name));
                var missing = unit.Where(x => x.Text("riders_attachment_point") != "" && !bones.Contains(x.Text("riders_attachment_point"))).ToList();
                if (missing.Count != 0 && missing.All(x => x.Text("battle_personality") == unit.Key))
                    ownRider.Add($"{unit.Key}: its own personality on {missing[0].Text("riders_attachment_point")}, which {mount.Text("variant")} has no skeleton for");
                else
                    problems.AddRange(missing.Select(x => $"{unit.Key}: {x.Text("riders_attachment_point")} is on neither {mount.Text("variant")}'s tech skeleton nor {mount.Text("animation")}'s skeleton"));
            }

            TestContext.Out.WriteLine($"{mountOnly} mount-only units: {mountOnly - ownRider.Count} gated, {ownRider.Count} heroes on a bone their mount lacks (counted); " +
                $"{withEngine} with an engine counted, not gated; {problems.Count} problems");
            foreach (var line in ownRider.Concat(problems).Take(30))
                TestContext.Out.WriteLine(line);
            Assert.That(mountOnly, Is.EqualTo(454));
            Assert.That(ownRider, Has.Count.EqualTo(3));
            Assert.That(problems, Is.Empty);
        }

        // 1c. Placing a personality's origin on its bone seats it only if its animation is not authored with an offset baked in. Read as:
        // the root (the hip) within 10 cm of the vertical through the origin, and under 1.5 m above it (plan ruling on spec 6.1c).
        [Test]
        public void ThePersonalitiesStand_KeepTheirRootOverTheirOrigin()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var personalities = ByKey(DbTables.Read(pfs, "battle_personalities_tables"), "key");
            var tables = AnimTableEdits.AllByName(pfs);
            var keys = new[] { "wh2_dlc09_tmb_casket_of_souls_rider1", "wh2_dlc09_tmb_warsphinx_rider_0", "wh2_dlc09_tmb_warsphinx_rider_1", "wh2_dlc09_tmb_warsphinx_rider_2", "wh2_dlc09_tmb_warsphinx_rider_3" };
            foreach (var key in keys)
            {
                var table = tables[personalities[key].Text("man_animations_table")];
                var stand = table.Entries.First(x => x.SlotName == "STAND");
                var skeleton = new GameSkeleton(AnimationFile.Create(pfs.FindFile($@"animations\skeletons\{table.SkeletonName}.anim")!), null);
                var clip = new AnimationClip(AnimationFile.Create(pfs.FindFile(stand.AnimationFile)!), skeleton);
                var root = skeleton.GetBoneIndexByName("root");
                Assert.That(root, Is.Not.EqualTo(-1), key);

                var (farthest, highest) = (0f, 0f);
                for (var i = 0; i <= 60; i++)
                {
                    var at = AnimationSampler.Sample(i / 60f, skeleton, clip).GetSkeletonAnimatedWorld(skeleton, root).Translation;
                    farthest = Math.Max(farthest, new Vector2(at.X, at.Z).Length());
                    highest = Math.Max(highest, at.Y);
                }
                TestContext.Out.WriteLine($"{key} ({table.Name} STAND): root at most {farthest:0.000} m off the vertical through its origin, at most {highest:0.000} m up");
                Assert.That(farthest, Is.LessThan(0.10f), key);
                Assert.That(highest, Is.LessThan(1.5f), key);
            }
        }
    }
}
