using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands.Bone.Clipboard;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Test.Animation.Posing.TestSkeletons;

namespace GameWorld.Core.Test.Animation
{
    public class FrameClipboardTests
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
        ];
        const int Spine = 2, UpperL = 3, LowerL = 4, HandL = 5, UpperR = 6, LowerR = 7, HandR = 8;
        static readonly int[] LeftArm = [UpperL, LowerL, HandL];
        static readonly FrameClipboard.Channels All = new(true, true, true);

        static GameSkeleton Body() => Build(BodyBones);

        /// <summary>A clip whose every frame turns every bone by its own amount, so no two frames or bones match.</summary>
        static AnimationClip Clip(GameSkeleton skeleton, int frames)
        {
            var clip = new AnimationClip();
            for (var i = 0; i < frames; i++)
            {
                var frame = BindFrame(skeleton);
                for (var bone = 0; bone < skeleton.BoneCount; bone++)
                {
                    frame.Rotation[bone] = Quaternion.Normalize(frame.Rotation[bone] * Quaternion.CreateFromYawPitchRoll(0.05f * (i + 1), 0.03f * (bone + 1), 0.02f * (i - bone)));
                    frame.Position[bone] += new Vector3(0.01f * i, 0.002f * bone, 0);
                    frame.Scale[bone] = Vector3.One * (1 + 0.01f * i);
                }
                clip.DynamicFrames.Add(frame);
            }
            return clip;
        }

        static void AssertSameBone(AnimationClip.KeyFrame actual, AnimationClip.KeyFrame expected, int bone, string what)
        {
            Assert.That(actual.Rotation[bone], Is.EqualTo(expected.Rotation[bone]), $"{what}: rotation of bone {bone}");
            Assert.That(actual.Position[bone], Is.EqualTo(expected.Position[bone]), $"{what}: position of bone {bone}");
            Assert.That(actual.Scale[bone], Is.EqualTo(expected.Scale[bone]), $"{what}: scale of bone {bone}");
        }

        [Test]
        public void CopyThenPaste_WritesTheCopiedBonesFromTheTargetFrameOn_AndNothingElse()
        {
            var skeleton = Body();
            var clip = Clip(skeleton, 8);
            var before = clip.DynamicFrames.Select(x => x.Clone()).ToList();

            var copied = FrameClipboard.Copy(clip, skeleton, LeftArm, 1, 2);
            var result = FrameClipboard.Paste(clip, skeleton, copied, 5, All, flipped: false);

            Assert.That(result.Frames, Is.EqualTo(2));
            Assert.That(result.Skipped, Is.Zero);
            Assert.That(result.Bones, Is.EquivalentTo(LeftArm));
            for (var frame = 0; frame < 8; frame++)
                for (var bone = 0; bone < skeleton.BoneCount; bone++)
                {
                    var source = frame is 5 or 6 && LeftArm.Contains(bone) ? before[frame - 4] : before[frame];
                    AssertSameBone(clip.DynamicFrames[frame], source, bone, $"frame {frame}");
                }
        }

        [Test]
        public void Paste_PastTheEnd_WritesWhatFits_AndCountsTheRest()
        {
            var skeleton = Body();
            var clip = Clip(skeleton, 6);
            var copied = FrameClipboard.Copy(clip, skeleton, [Spine], 0, 2);

            var result = FrameClipboard.Paste(clip, skeleton, copied, 4, All, flipped: false);

            Assert.That((result.Frames, result.Skipped), Is.EqualTo((2, 1)));
        }

        [Test]
        public void Paste_MatchesBonesByName_IntoASkeletonWithAnotherOrder()
        {
            var source = Body();
            var reordered = Build([BodyBones[0], BodyBones[1], BodyBones[2], BodyBones[6], BodyBones[7], BodyBones[8], BodyBones[3], BodyBones[4], BodyBones[5]]);
            var from = Clip(source, 3);
            var into = Clip(reordered, 3);

            var result = FrameClipboard.Paste(into, reordered, FrameClipboard.Copy(from, source, [HandL], 1, 1), 0, All, flipped: false);

            var handInto = reordered.GetBoneIndexByName("hand_left");
            Assert.That(result.Bones, Is.EqualTo(new[] { handInto }));
            Assert.That(into.DynamicFrames[0].Rotation[handInto], Is.EqualTo(from.DynamicFrames[1].Rotation[HandL]));
        }

        [Test]
        public void Paste_ReportsBonesTheTargetSkeletonLacks()
        {
            var skeleton = Body();
            var copied = FrameClipboard.Copy(Clip(skeleton, 2), skeleton, [HandL, HandR], 0, 0);
            var armless = Build(BodyBones[..6]);

            var result = FrameClipboard.Paste(Clip(armless, 2), armless, copied, 0, All, flipped: false);

            Assert.That(result.MissingBones, Is.EqualTo(new[] { "hand_right" }));
        }

        [Test]
        public void Paste_LeavesTheChannelsThatAreOff()
        {
            var skeleton = Body();
            var clip = Clip(skeleton, 4);
            var before = clip.DynamicFrames[3].Clone();

            FrameClipboard.Paste(clip, skeleton, FrameClipboard.Copy(clip, skeleton, [Spine], 0, 0), 3, new(false, true, false), flipped: false);

            Assert.That(clip.DynamicFrames[3].Rotation[Spine], Is.EqualTo(clip.DynamicFrames[0].Rotation[Spine]));
            Assert.That(clip.DynamicFrames[3].Position[Spine], Is.EqualTo(before.Position[Spine]));
            Assert.That(clip.DynamicFrames[3].Scale[Spine], Is.EqualTo(before.Scale[Spine]));
        }

        // The old global clipboard keyed its frames by their frame numbers in the source clip; a copy made with it still
        // pastes from its first frame.
        [Test]
        public void AnOldCopy_KeyedByItsSourceFrames_PastesFromItsFirstFrame()
        {
            var skeleton = Body();
            var clip = Clip(skeleton, 4);
            var copied = FrameClipboard.Copy(clip, skeleton, [Spine], 2, 3);
            var old = new BoneTransformClipboardData { SkeletonName = copied.SkeletonName };
            foreach (var (key, frame) in copied.Frames)
                old.Frames[key + 40] = frame;

            FrameClipboard.Paste(clip, skeleton, old, 0, All, flipped: false);

            Assert.That(clip.DynamicFrames[0].Rotation[Spine], Is.EqualTo(clip.DynamicFrames[2].Rotation[Spine]));
            Assert.That(clip.DynamicFrames[1].Rotation[Spine], Is.EqualTo(clip.DynamicFrames[3].Rotation[Spine]));
        }

        [Test]
        public void PasteFlipped_OfEveryBone_IsTheMirroredFrame()
        {
            var skeleton = Body();
            var clip = Clip(skeleton, 4);
            var expected = clip.DynamicFrames[1].Clone();
            PoseMirror.Apply(expected, skeleton, MirrorMode.Flip);

            FrameClipboard.Paste(clip, skeleton, FrameClipboard.Copy(clip, skeleton, Enumerable.Range(0, skeleton.BoneCount).ToList(), 1, 1), 3, All, flipped: true);

            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                Assert.That(AngleBetween(clip.DynamicFrames[3].Rotation[bone], expected.Rotation[bone]), Is.LessThan(1e-4f), $"rotation of bone {bone}");
                Assert.That(Vector3.Distance(clip.DynamicFrames[3].Position[bone], expected.Position[bone]), Is.LessThan(1e-4f), $"position of bone {bone}");
            }
        }

        // Blender's Paste Flipped: the left arm copied lands on the right arm, and the left arm where it pastes is left alone.
        [Test]
        public void PasteFlipped_OfTheLeftArm_LandsOnTheRightArm()
        {
            var skeleton = Body();
            var clip = Clip(skeleton, 4);
            var target = clip.DynamicFrames[3].Clone();
            // With the body above the arm as it is on the target frame, the right arm becomes the mirror of the copied left one.
            var expected = target.Clone();
            foreach (var bone in LeftArm)
            {
                expected.Rotation[bone] = clip.DynamicFrames[1].Rotation[bone];
                expected.Position[bone] = clip.DynamicFrames[1].Position[bone];
            }
            PoseMirror.Apply(expected, skeleton, MirrorMode.Flip);

            var result = FrameClipboard.Paste(clip, skeleton, FrameClipboard.Copy(clip, skeleton, LeftArm, 1, 1), 3, All, flipped: true);

            Assert.That(result.Bones, Is.EquivalentTo(new[] { UpperR, LowerR, HandR }));
            foreach (var bone in LeftArm)
                AssertSameBone(clip.DynamicFrames[3], target, bone, "the left arm");
            foreach (var bone in new[] { UpperR, LowerR, HandR })
                Assert.That(AngleBetween(clip.DynamicFrames[3].Rotation[bone], expected.Rotation[bone]), Is.LessThan(1e-4f), $"rotation of bone {bone}");
        }
    }
}
