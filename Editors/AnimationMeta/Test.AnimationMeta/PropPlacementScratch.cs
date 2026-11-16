using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: where a prop's own animation puts its bones, against the main skeleton's attach bone.
    //   CA_CACHE_DB  MAIN_SKEL  MAIN_ANIM  PROP_MESH  PROP_ANIM  ATTACH_BONE  TIMES (comma list of seconds)
    public class PropPlacementScratch
    {
        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_PropPlacement()
        {
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);
            var pfs = runner.PackFileService;
            TestContext.Progress.WriteLine("P1 container");

            GameSkeleton Skeleton(string name) => new(AnimationFile.Create(pfs.FindFile($"animations/skeletons/{name}.anim")!), null!);
            var main = Skeleton(Environment.GetEnvironmentVariable("MAIN_SKEL")!);
            var mainClip = new AnimationClip(AnimationFile.Create(pfs.FindFile(Environment.GetEnvironmentVariable("MAIN_ANIM")!)!), main);

            TestContext.Progress.WriteLine("P2 main clip");
            var meshPath = Environment.GetEnvironmentVariable("PROP_MESH")!;
            var rmv = pfs.FindFile(meshPath.Replace(".wsmodel", ".rigid_model_v2"))!;
            var propSkeletonName = ModelFactory.Create().Load(rmv.DataSource.ReadData()).Header.SkeletonName;
            TestContext.Progress.WriteLine("P3 prop skeleton " + propSkeletonName);
            var prop = Skeleton(propSkeletonName);
            var propAnim = AnimationFile.Create(pfs.FindFile(Environment.GetEnvironmentVariable("PROP_ANIM")!)!);
            var propClip = new AnimationClip(propAnim, prop);
            TestContext.Out.WriteLine($"PROP skeleton {propSkeletonName} bones {prop.BoneCount}: {string.Join(", ", Enumerable.Range(0, prop.BoneCount).Select(i => $"{i}={prop.BoneNames[i]}<{prop.GetParentBoneIndex(i)}"))}");
            TestContext.Out.WriteLine($"PROP anim skeleton {propAnim.Header.SkeletonName} len {propClip.PlayTimeInSec} main len {mainClip.PlayTimeInSec}");
            TestContext.Out.WriteLine($"PROP bind root {prop.Translation[0]} {prop.Rotation[0]}");

            var attach = int.Parse(Environment.GetEnvironmentVariable("ATTACH_BONE")!);
            foreach (var t in (Environment.GetEnvironmentVariable("TIMES") ?? "0").Split(',').Select(float.Parse))
            {
                var m = AnimationSampler.Sample(Math.Clamp(t / mainClip.PlayTimeInSec, 0, 1), main, mainClip);
                var p = AnimationSampler.Sample(Math.Clamp(t / propClip.PlayTimeInSec, 0, 1), prop, propClip);
                var bone = m.GetSkeletonAnimatedWorld(main, attach);
                var handR = m.GetSkeletonAnimatedWorld(main, main.GetBoneIndexByName("hand_right"));
                var handL = m.GetSkeletonAnimatedWorld(main, main.GetBoneIndexByName("hand_left"));
                TestContext.Out.WriteLine($"t={t}: main {main.BoneNames[attach]} at {bone.Translation}  hand_right {handR.Translation}  hand_left {handL.Translation}");
                for (var i = 0; i < Math.Min(prop.BoneCount, 4); i++)
                    TestContext.Out.WriteLine($"   prop {prop.BoneNames[i]} model-space {p.GetSkeletonAnimatedWorld(prop, i).Translation}  composed-at-bone {(p.GetSkeletonAnimatedWorld(prop, i) * bone).Translation}");
                TestContext.Out.WriteLine($"   prop last {prop.BoneNames[prop.BoneCount - 1]} model-space {p.GetSkeletonAnimatedWorld(prop, prop.BoneCount - 1).Translation}");
            }
        }
    }
}
