using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Test.Animation.Posing.TestSkeletons;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class PoseMirrorTests
    {
        static readonly Bone[] BodyBones =
        [
            new("root", -1, Vector3.Zero),
            new("pelvis", 0, Vector3.UnitY),
            new("spine", 1, new Vector3(0, 0.5f, 0)),
            new("upperarm_left", 2, new Vector3(0.3f, 0.3f, 0)),
            new("lowerarm_left", 3, new Vector3(0.3f, 0, 0)),
            new("hand_left", 4, new Vector3(0.3f, 0, 0)),
            new("upperarm_right", 2, new Vector3(-0.3f, 0.3f, 0)),
            new("lowerarm_right", 6, new Vector3(-0.3f, 0, 0)),
            new("hand_right", 7, new Vector3(-0.3f, 0, 0)),
            new("left_eye", 2, new Vector3(0.05f, 0.6f, 0.1f)),   // no right_eye
        ];
        const int Spine = 2, UpperL = 3, HandL = 5, UpperR = 6, HandR = 8, Eye = 9;

        static GameSkeleton Body() => Build(BodyBones);

        static AnimationClip.KeyFrame RandomPose(GameSkeleton skeleton, int seed)
        {
            var random = new Random(seed);
            float R() => (float)random.NextDouble() - 0.5f;
            var frame = BindFrame(skeleton);
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
                frame.Rotation[bone] = Quaternion.Normalize(frame.Rotation[bone] * Quaternion.CreateFromYawPitchRoll(R(), R(), R()));
            frame.Position[1] += new Vector3(R(), R(), R()) * 0.2f;
            return frame;
        }

        static void AssertSameFrame(AnimationClip.KeyFrame actual, AnimationClip.KeyFrame expected)
        {
            for (var bone = 0; bone < expected.Rotation.Count; bone++)
            {
                Assert.That(AngleBetween(actual.Rotation[bone], expected.Rotation[bone]), Is.LessThan(1e-4f), $"rotation of bone {bone}");
                Assert.That(Vector3.Distance(actual.Position[bone], expected.Position[bone]), Is.LessThan(1e-4f), $"position of bone {bone}");
            }
        }

        static Vector3 Flip(Vector3 v) => new(-v.X, v.Y, v.Z);

        [Test]
        public void Pairs_FindLeftAndRight_AndLeaveTheSpineAlone()
        {
            var pairs = PoseMirror.Pairs(Body());

            Assert.That(pairs, Is.EqualTo(new[] { 0, 1, 2, 6, 7, 8, 3, 4, 5, Eye }));
        }

        [Test]
        public void MirrorAxis_IsX() => Assert.That(PoseMirror.MirrorAxis(Body()), Is.EqualTo(0));

        [Test]
        public void Flip_Twice_IsTheIdentity()
        {
            var skeleton = Body();
            var frame = RandomPose(skeleton, 3);
            var original = frame.Clone();

            PoseMirror.Apply(frame, skeleton, MirrorMode.Flip);
            PoseMirror.Apply(frame, skeleton, MirrorMode.Flip);

            AssertSameFrame(frame, original);
        }

        [Test]
        public void Flip_MovesARaisedLeftHandToTheRight()
        {
            var skeleton = Body();
            var frame = BindFrame(skeleton);
            frame.Rotation[UpperL] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
            frame.Rotation[Spine] = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);   // a twist, which a mirror reverses
            var leftHand = At(frame, skeleton, HandL);
            var rightHand = At(frame, skeleton, HandR);

            PoseMirror.Apply(frame, skeleton, MirrorMode.Flip);

            Assert.That(Vector3.Distance(At(frame, skeleton, HandR), Flip(leftHand)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(At(frame, skeleton, HandL), Flip(rightHand)), Is.LessThan(1e-4f));
        }

        [Test]
        public void LeftOntoRight_LeavesTheLeftAndTheSpineUnchanged()
        {
            var skeleton = Body();
            var frame = RandomPose(skeleton, 5);
            var before = frame.Clone();

            PoseMirror.Apply(frame, skeleton, MirrorMode.LeftOntoRight);

            foreach (var bone in new[] { 0, 1, Spine, UpperL, 4, HandL, Eye })
            {
                Assert.That(frame.Rotation[bone], Is.EqualTo(before.Rotation[bone]), $"rotation of bone {bone}");
                Assert.That(frame.Position[bone], Is.EqualTo(before.Position[bone]), $"position of bone {bone}");
            }
            Assert.That(frame.Rotation[UpperR], Is.Not.EqualTo(before.Rotation[UpperR]));
        }

        [Test]
        public void LeftOntoRight_WithTheSpineAtBind_MirrorsTheHandExactly()
        {
            var skeleton = Body();
            var frame = BindFrame(skeleton);
            frame.Rotation[UpperL] = Quaternion.CreateFromYawPitchRoll(0.4f, -0.3f, 0.6f);
            var leftHand = At(frame, skeleton, HandL);

            PoseMirror.Apply(frame, skeleton, MirrorMode.LeftOntoRight);

            Assert.That(Vector3.Distance(At(frame, skeleton, HandR), Flip(leftHand)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(At(frame, skeleton, HandL), leftHand), Is.LessThan(1e-6f));
        }

        [Test]
        public void AsymmetricBind_FlipTwiceIsStillTheIdentity()
        {
            var bones = BodyBones.ToArray();
            bones[UpperL] = bones[UpperL] with { At = new Vector3(0.35f, 0.3f, 0.02f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f) };
            var skeleton = Build(bones);
            var frame = RandomPose(skeleton, 9);
            var original = frame.Clone();

            PoseMirror.Apply(frame, skeleton, MirrorMode.Flip);
            PoseMirror.Apply(frame, skeleton, MirrorMode.Flip);

            AssertSameFrame(frame, original);
        }

        [Test]
        public void ShuffledBoneOrder_GivesTheSameResult()
        {
            // Children listed before their parents: the hands and lower arms first.
            int[] order = [8, 5, 7, 4, 9, 6, 3, 2, 1, 0];
            var newIndex = new int[order.Length];
            for (var i = 0; i < order.Length; i++)
                newIndex[order[i]] = i;
            var shuffledBones = order.Select(old => BodyBones[old] with { Parent = BodyBones[old].Parent == -1 ? -1 : newIndex[BodyBones[old].Parent] }).ToArray();
            var plain = Body();
            var shuffled = Build(shuffledBones);
            var plainFrame = RandomPose(plain, 11);
            var shuffledFrame = BindFrame(shuffled);
            for (var old = 0; old < order.Length; old++)
            {
                shuffledFrame.Rotation[newIndex[old]] = plainFrame.Rotation[old];
                shuffledFrame.Position[newIndex[old]] = plainFrame.Position[old];
            }

            PoseMirror.Apply(plainFrame, plain, MirrorMode.Flip);
            PoseMirror.Apply(shuffledFrame, shuffled, MirrorMode.Flip);

            for (var old = 0; old < order.Length; old++)
                Assert.That(Vector3.Distance(At(shuffledFrame, shuffled, newIndex[old]), At(plainFrame, plain, old)), Is.LessThan(1e-4f), BodyBones[old].Name);
        }

        [Test]
        public void NoPairs_Throws()
        {
            var skeleton = Arm();

            Assert.That(PoseMirror.MirrorAxis(skeleton), Is.Null);
            Assert.Throws<InvalidOperationException>(() => PoseMirror.Apply(BindFrame(skeleton), skeleton, MirrorMode.Flip));
        }

        [Test]
        public void UnpairedLeftBone_IsItsOwnMirror()
        {
            var skeleton = Body();
            var frame = BindFrame(skeleton);
            frame.Rotation[Eye] = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);

            Assert.That(PoseMirror.Pairs(skeleton)[Eye], Is.EqualTo(Eye));
            PoseMirror.Apply(frame, skeleton, MirrorMode.Flip);

            Assert.That(AngleBetween(frame.Rotation[Eye], Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.4f)), Is.LessThan(1e-5f));
        }
    }
}
