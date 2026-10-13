using Editors.UnitSimulator.Personalities;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;

namespace Test.UnitSimulator
{
    public class AttachBonesTests
    {
        const string CasketTech = @"warmachines\engines\tmb_casket_of_souls\tech\tmb_casket_of_souls_01.anim";
        const string NagashTech = @"variantmeshes\wh_variantmodels\hu1p\vmp\vmp_nagash\tech\vmp_nagash_01.anim";

        static GameSkeleton Skeleton(IPackFileService pfs, string path) => new(AnimationFile.Create(pfs.FindFile(path)!), null);

        // Where an attach bone is when the animation skeleton stands as it does: its matrix relative to its parent in the tech
        // skeleton, by inverse rather than by AttachBones' scale-rotation-translation, carried onto the animation skeleton's parent.
        // Not the tech skeleton's own world matrix: the Casket's tech animroot is turned 180 degrees about Y against its animation
        // skeleton's, and the animations drive animroot. Shared with PersonalityTests.
        internal static Matrix OnAnimation(GameSkeleton tech, string bone, GameSkeleton animation)
        {
            var index = tech.GetBoneIndexByName(bone);
            var parent = tech.GetParentBoneIndex(index);
            return tech.GetWorldTransform(index) * Matrix.Invert(tech.GetWorldTransform(parent)) * animation.GetAnimatedWorldTranform(parent);
        }

        internal static float Off(Matrix a, Matrix b)
        {
            var (x, y) = (Matrix.ToFloatArray(a), Matrix.ToFloatArray(b));
            return x.Zip(y, (p, q) => Math.Abs(p - q)).Max();
        }

        [Test]
        public void TheCaskets_ApRiderPosition0_IsFoundOnItsParent()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var tech = Skeleton(pfs, CasketTech);
            var (bones, problem) = AttachBones.FromTech(Skeleton(pfs, @"animations\skeletons\casket_souls01.anim"), tech, CasketTech);
            Assert.That(problem, Is.Null);
            Assert.That(bones.Select(x => x.Name), Is.EqualTo(new[] { "ap_riderposition_0" }));
            Assert.That(bones[0].Parent, Is.EqualTo(tech.GetParentBoneIndex(tech.GetBoneIndexByName("ap_riderposition_0"))));
        }

        // Computed two ways (OnAnimation), the whole matrix: the rider takes the bone's turn as well as its place.
        [Test]
        public void AtTheBindPose_AnApBone_IsItsTechMatrixOnTheAnimationSkeleton()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var tech = Skeleton(pfs, CasketTech);
            var animation = Skeleton(pfs, @"animations\skeletons\casket_souls01.anim");
            var bone = AttachBones.FromTech(animation, tech, CasketTech).Bones.Single();

            var expected = OnAnimation(tech, "ap_riderposition_0", animation);
            Assert.That(Off(AttachBones.World(bone, animation), expected), Is.LessThan(0.001f));
            Assert.That(expected.Translation.Length(), Is.GreaterThan(0.1f), "the bone is off the origin, or the comparison means nothing");
        }

        // Nagash's ap_turret is appended on no parent (TechSkeletonTests 1a), at the origin.
        [Test]
        public void AnApBoneOnNoParent_IsFound()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var (bones, problem) = AttachBones.FromTech(Skeleton(pfs, @"animations\skeletons\humanoid01p.anim"), Skeleton(pfs, NagashTech), NagashTech);
            Assert.That(problem, Is.Null);
            Assert.That(bones.Single(x => x.Name == "ap_turret").Parent, Is.EqualTo(-1));
        }

        // A bone on no parent stands at its own matrix, whatever the skeleton does: Nagash's is at the origin, so a made-up one.
        [Test]
        public void ABoneOnNoParent_StandsAtItsOwnMatrix()
        {
            var skeleton = Skeleton(VanillaGame.Load(withDb: true).PackFileService, @"animations\skeletons\casket_souls01.anim");
            var local = Matrix.CreateRotationY(0.5f) * Matrix.CreateTranslation(1, 2, 3);
            Assert.That(AttachBones.World(new AttachBone("ap_test", -1, local), skeleton), Is.EqualTo(local));
        }

        [Test]
        public void AnAnimationSkeletonThatIsNotTheTechSkeletonsBase_IsNamed_NotMatched()
        {
            var pfs = VanillaGame.Load(withDb: true).PackFileService;
            var (bones, problem) = AttachBones.FromTech(Skeleton(pfs, @"animations\skeletons\horse01.anim"), Skeleton(pfs, CasketTech), CasketTech);
            Assert.That(bones, Is.Empty);
            Assert.That(problem, Does.Contain("tmb_casket_of_souls_01").And.Contain("horse01"));
        }

        // A skeleton that carries an ap_ bone of its own: the tech skeleton itself, standing in for one.
        [Test]
        public void AnAnimationSkeletonsOwnApBone_FollowsItself()
        {
            var tech = Skeleton(VanillaGame.Load(withDb: true).PackFileService, CasketTech);
            var bone = AttachBones.FromAnimation(tech).Single();
            var index = tech.GetBoneIndexByName("ap_riderposition_0");
            Assert.That((bone.Name, bone.Parent, bone.Local), Is.EqualTo(("ap_riderposition_0", index, Matrix.Identity)));
            Assert.That(AttachBones.World(bone, tech), Is.EqualTo(tech.GetWorldTransform(index)));
        }
    }
}
