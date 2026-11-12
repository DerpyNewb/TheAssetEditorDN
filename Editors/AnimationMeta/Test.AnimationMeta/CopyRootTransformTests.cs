using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta
{
    // ANIMATED_PROP places the prop's own model space at the attach bone: Scale in the prop's space,
    // then Orientation and Position in the bone's. zeppelin01_cannon's root bone is turned 180
    // degrees, so a rule that replaced the root instead of keeping it drew the cannon backwards.
    public class CopyRootTransformTests
    {
        record Provider(GameSkeleton Skeleton) : ISkeletonProvider;

        [Test]
        public void PropModelSpace_LandsAtTheBone_Scaled_WithItsOwnRootKept()
        {
            var halfTurnY = new Quaternion(0, 1, 0, 0);
            var prop = CreateSkeleton([("animroot", -1, Vector3.Zero, halfTurnY), ("cannon", 0, new Vector3(0, 0, -1.45f), Quaternion.Identity)]);
            var attachRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
            var main = CreateSkeleton([("animroot", -1, Vector3.Zero, Quaternion.Identity), ("root", 0, new Vector3(0, 2, 0), attachRotation)]);

            var offsetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
            var offset = new Vector3(0.5f, 0.1f, 0.1f);
            var rule = new CopyRootTransform(new Provider(main), 1, offset, offsetRotation, 1.5f);

            var clip = new AnimationClip(BindPose(prop), prop);
            Assert.That(CopyRootTransform.RootFollowsBone(prop, clip, main.GetWorldTransform(1)), Is.False, "a root at the prop's own origin is in the bone's space");
            var skin = AnimationSampler.Sample(0, 0, prop, clip, [rule]).BoneTransforms[1].WorldTransform;

            // A vertex of the cannon mesh, in the prop model's own space.
            var vertex = new Vector3(0.1f, 0.2f, 2f);
            var expected = Vector3.Transform(vertex, Matrix.CreateScale(1.5f) * Matrix.CreateFromQuaternion(offsetRotation) * Matrix.CreateTranslation(offset) * main.GetWorldTransform(1));
            Assert.That(Vector3.Distance(Vector3.Transform(vertex, skin), expected), Is.LessThan(1e-4));
        }

        // Most vanilla props are animated in the main model's space instead, their root riding the attach bone: 121 of 156
        // surveyed with a bone, the Doomseeker's flail on be_prop_1 among them. Such a prop stays where its animation puts it,
        // the tag's Position still moving it in the bone's space; carried to the bone a second time, the flail hung a hand's
        // length out to his side.
        [TestCase(0f)]
        [TestCase(0.2f)]
        public void PropAnimatedInModelSpace_StaysWhereItsAnimationPutsIt(float offsetZ)
        {
            var attachRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
            var hand = new Vector3(0.5f, 0.7f, 0.1f);
            var main = CreateSkeleton([("animroot", -1, Vector3.Zero, Quaternion.Identity), ("be_prop_1", 0, hand, attachRotation)]);
            var prop = CreateSkeleton([("root", -1, hand, attachRotation), ("axe_base_0", 0, new Vector3(0, -0.3f, 0), Quaternion.Identity)]);
            var clip = new AnimationClip(BindPose(prop), prop);
            Assert.That(CopyRootTransform.RootFollowsBone(prop, clip, main.GetWorldTransform(1)), Is.True, "a root on the bone is in the model's space");

            var offset = new Vector3(0, 0, offsetZ);
            var rule = new CopyRootTransform(new Provider(main), 1, offset, Quaternion.Identity, 1, rootFollowsBone: true);
            var skin = AnimationSampler.Sample(0, 0, prop, clip, [rule]).BoneTransforms[1].WorldTransform;

            // The handle as animated (the clip is the bind pose), moved by Position along the bone's axes.
            var animated = prop.GetWorldTransform(1).Translation;
            var handle = Vector3.Transform(animated, skin);
            var expected = animated + Vector3.Transform(offset, attachRotation);
            Assert.That(Vector3.Distance(handle, expected), Is.LessThan(1e-4), $"handle at {handle}, animated at {animated}");
        }

        // The ratling gun's root is keyed a quarter turn about its barrels (X) for the whole clip, off an identity bind pose.
        // The game fires Azeros's gatling, built on it, unturned, so the root sits in its bind pose and the barrels stay put.
        [Test]
        public void RootKeyedToAFixedTurn_SitsInItsBindPose()
        {
            var quarterTurnX = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathHelper.PiOver2);
            var prop = CreateSkeleton([("root", -1, Vector3.Zero, Quaternion.Identity), ("barrels", 0, Vector3.Zero, Quaternion.Identity)]);
            var main = CreateSkeleton([("animroot", -1, Vector3.Zero, Quaternion.Identity), ("be_prop_3", 0, new Vector3(0.5f, 0.6f, 0.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f))]);
            var clip = new AnimationClip(Keyed(prop, Vector3.Zero, quarterTurnX, quarterTurnX), prop);
            Assert.That(CopyRootTransform.RootKeyIsFixedTurn(prop, clip), Is.True);

            var rule = new CopyRootTransform(new Provider(main), 1, Vector3.Zero, Quaternion.Identity, 1, bindRoot: prop.GetWorldTransform(0));
            var skin = AnimationSampler.Sample(0, 0, prop, clip, [rule]).BoneTransforms[1].WorldTransform;
            var handle = new Vector3(0.78f, -0.2f, 0.03f); // the grip on the gun's left flank
            var expected = Vector3.Transform(handle, main.GetWorldTransform(1));
            Assert.That(Vector3.Distance(Vector3.Transform(handle, skin), expected), Is.LessThan(1e-4));
        }

        // Only that shape: a root keyed at its bind pose (zeppelin01_cannon, half a turn about Y in both), at identity under a
        // turned bind pose (the Akshina Ambushers' crossbow), turning over the clip, or moving, keeps its keys.
        [TestCase("bind")]
        [TestCase("identity")]
        [TestCase("turning")]
        [TestCase("moving")]
        public void OtherRootKeys_AreNotAFixedTurn(string shape)
        {
            var bind = new Quaternion(0, 0.7071068f, 0.7071068f, 0);
            var prop = CreateSkeleton([("root", -1, Vector3.Zero, bind), ("cannon", 0, new Vector3(0, 0, -1.45f), Quaternion.Identity)]);
            var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathHelper.PiOver2);
            var file = shape switch
            {
                "bind" => Keyed(prop, Vector3.Zero, bind, bind),
                "identity" => Keyed(prop, Vector3.Zero, Quaternion.Identity, Quaternion.Identity),
                "turning" => Keyed(prop, Vector3.Zero, quarter, Quaternion.Identity),
                _ => Keyed(prop, new Vector3(0, 0, 0.5f), quarter, quarter),
            };
            Assert.That(CopyRootTransform.RootKeyIsFixedTurn(prop, new AnimationClip(file, prop)), Is.False);
        }

        // The skeleton's pose with the root keyed to each rotation in turn, one frame each; the last frame moves the root by `move`.
        static AnimationFile Keyed(GameSkeleton skeleton, Vector3 move, params Quaternion[] rootKeys)
        {
            var file = BindPose(skeleton);
            var part = file.AnimationParts[0];
            part.DynamicFrames.Clear();
            for (var i = 0; i < skeleton.BoneCount; i++) // without mappings the sampler ignores the frames and draws the bind pose
            {
                part.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(i));
                part.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(i));
            }
            for (var k = 0; k < rootKeys.Length; k++)
            {
                var frame = new AnimationFile.Frame();
                for (var i = 0; i < skeleton.BoneCount; i++)
                {
                    var (t, q) = i == 0 ? (k == rootKeys.Length - 1 ? move : Vector3.Zero, rootKeys[k]) : (skeleton.Translation[i], skeleton.Rotation[i]);
                    frame.Transforms.Add(new RmvVector3(t.X, t.Y, t.Z));
                    frame.Quaternion.Add(new RmvVector4(q.X, q.Y, q.Z, q.W));
                }
                part.DynamicFrames.Add(frame);
            }
            file.Header.AnimationTotalPlayTimeInSec = 1;
            file.Header.FrameRate = rootKeys.Length - 1;
            return file;
        }

        static AnimationFile BindPose(GameSkeleton skeleton)
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = skeleton.SkeletonName;
            var frame = new AnimationFile.Frame();
            for (var i = 0; i < skeleton.BoneCount; i++)
            {
                frame.Transforms.Add(new RmvVector3(skeleton.Translation[i].X, skeleton.Translation[i].Y, skeleton.Translation[i].Z));
                frame.Quaternion.Add(new RmvVector4(skeleton.Rotation[i].X, skeleton.Rotation[i].Y, skeleton.Rotation[i].Z, skeleton.Rotation[i].W));
            }
            file.Bones = Enumerable.Range(0, skeleton.BoneCount).Select(i => new AnimationFile.BoneInfo { Id = i, Name = skeleton.BoneNames[i], ParentId = skeleton.GetParentBoneIndex(i) }).ToArray();
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            file.AnimationParts.Add(part);
            return file;
        }

        static GameSkeleton CreateSkeleton((string Name, int Parent, Vector3 Translation, Quaternion Rotation)[] bones)
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones = bones.Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = b.Parent }).ToArray();
            var frame = new AnimationFile.Frame();
            foreach (var b in bones)
            {
                frame.Transforms.Add(new RmvVector3(b.Translation.X, b.Translation.Y, b.Translation.Z));
                frame.Quaternion.Add(new RmvVector4(b.Rotation.X, b.Rotation.Y, b.Rotation.Z, b.Rotation.W));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, null!);
        }
    }
}
