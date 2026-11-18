using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;

namespace Test.SkeletonEditor
{
    // A bone moved onto a new parent stays where it is in the world, and so does everything under it. Only a parent listed
    // before the bone is taken, since moving the bone in the list would renumber it.
    internal class GameSkeleton_SetParentBoneTests
    {
        //   0 root
        //   +- 1 hip        (1, 0, 0), turned 90 degrees about Y
        //   |  +- 3 knee    (0, -1, 0) from hip
        //   +- 2 chest      (0, 2, 0), turned 45 degrees about X
        static GameSkeleton Build()
        {
            var skeleton = GameSkeleton.CreateFromAnimationFile(new AnimationFile
            {
                Bones =
                [
                    new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = -1 },
                    new AnimationFile.BoneInfo { Id = 1, Name = "hip", ParentId = 0 },
                    new AnimationFile.BoneInfo { Id = 2, Name = "chest", ParentId = 0 },
                    new AnimationFile.BoneInfo { Id = 3, Name = "knee", ParentId = 1 },
                ]
            }, null!);
            skeleton.Translation[1] = new Vector3(1, 0, 0);
            skeleton.Rotation[1] = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
            skeleton.Translation[2] = new Vector3(0, 2, 0);
            skeleton.Rotation[2] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver4);
            skeleton.Translation[3] = new Vector3(0, -1, 0);
            skeleton.RebuildSkeletonMatrix();
            return skeleton;
        }

        static float Distance(Matrix a, Matrix b) =>
            Enumerable.Range(0, 16).Max(i => Math.Abs(a[i / 4, i % 4] - b[i / 4, i % 4]));

        [Test]
        public void ABoneMovedUnderAnotherKeepsItsPlaceAndItsChildrensPlace()
        {
            var skeleton = Build();
            var before = Enumerable.Range(0, skeleton.BoneCount).Select(skeleton.GetWorldTransform).ToList();

            // The chest moves under the hip, which is listed before it and is turned, so its local transform must change.
            skeleton.SetParentBone(2, 1);

            Assert.That(skeleton.GetParentBoneIndex(2), Is.EqualTo(1));
            Assert.That(skeleton.Translation[2], Is.Not.EqualTo(new Vector3(0, 2, 0)), "the local transform was rewritten");
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
                Assert.That(Distance(skeleton.GetWorldTransform(bone), before[bone]), Is.LessThan(1e-5f), skeleton.BoneNames[bone]);
            Assert.That(skeleton.BoneNames, Is.EqualTo(new[] { "root", "hip", "chest", "knee" }), "no bone is renumbered");
        }

        [Test]
        public void ABoneCanLoseItsParent()
        {
            var skeleton = Build();
            var before = skeleton.GetWorldTransform(3);

            skeleton.SetParentBone(3, -1);

            Assert.That(skeleton.GetParentBoneIndex(3), Is.EqualTo(-1));
            Assert.That(Distance(skeleton.GetWorldTransform(3), before), Is.LessThan(1e-5f));
        }

        [TestCase(1, 2)]   // a parent listed after the bone
        [TestCase(1, 1)]   // itself
        public void AParentListedAfterTheBoneIsRefused(int bone, int parent)
        {
            var skeleton = Build();
            Assert.Throws<ArgumentException>(() => skeleton.SetParentBone(bone, parent));
            Assert.That(skeleton.GetParentBoneIndex(bone), Is.EqualTo(0), "nothing changed");
        }
    }
}
