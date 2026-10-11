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

            var skin = AnimationSampler.Sample(0, 0, prop, new AnimationClip(BindPose(prop), prop), [rule]).BoneTransforms[1].WorldTransform;

            // A vertex of the cannon mesh, in the prop model's own space.
            var vertex = new Vector3(0.1f, 0.2f, 2f);
            var expected = Vector3.Transform(vertex, Matrix.CreateScale(1.5f) * Matrix.CreateFromQuaternion(offsetRotation) * Matrix.CreateTranslation(offset) * main.GetWorldTransform(1));
            Assert.That(Vector3.Distance(Vector3.Transform(vertex, skin), expected), Is.LessThan(1e-4));
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
