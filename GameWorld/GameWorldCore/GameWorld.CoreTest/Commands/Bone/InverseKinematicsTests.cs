using GameWorld.Core.Animation;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Commands.Bone
{
    // The key frame editor's IK was switched off: it wrote model-space positions into the keys, which are in the parent's space,
    // so a drag threw the arm across the scene. It now turns the bones above the dragged one and leaves their lengths alone.
    public class InverseKinematicsTests
    {
        const int Root = 0, Pelvis = 1, Upper = 2, Lower = 3, Hand = 4, Other = 5;

        // root -> pelvis, 1 up -> upper arm, lower arm and hand, 1 apart along X; and a second bone on the pelvis.
        static GameSkeleton Skeleton()
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_arm";
            var bones = new (string Name, int Parent, Vector3 At)[]
            {
                ("root", AnimationFile.BoneIndexNoParent, Vector3.Zero), ("pelvis", Root, Vector3.UnitY), ("upper", Pelvis, Vector3.UnitX),
                ("lower", Upper, Vector3.UnitX), ("hand", Lower, Vector3.UnitX), ("other", Pelvis, -Vector3.UnitX),
            };
            var bind = new AnimationFile.Frame();
            file.Bones = bones.Select((x, i) => new AnimationFile.BoneInfo { Id = i, Name = x.Name, ParentId = x.Parent }).ToArray();
            foreach (var bone in bones)
            {
                bind.Transforms.Add(new RmvVector3(bone.At.X, bone.At.Y, bone.At.Z));
                bind.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(bind);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, null!);
        }

        static AnimationClip.KeyFrame Frame(GameSkeleton skeleton) => new()
        {
            Position = Enumerable.Range(0, skeleton.BoneCount).Select(i => skeleton.Translation[i]).ToList(),
            Rotation = Enumerable.Range(0, skeleton.BoneCount).Select(_ => Quaternion.Identity).ToList(),
            Scale = Enumerable.Range(0, skeleton.BoneCount).Select(_ => Vector3.One).ToList(),
        };

        static Vector3 At(AnimationClip.KeyFrame frame, GameSkeleton skeleton, int bone) => TransformBoneCommand.WorldTransform(frame, skeleton, bone).Translation;

        [Test]
        public void TheHand_ReachesATargetInReach_ByTurningOnlyTheChain()
        {
            var skeleton = Skeleton();
            var frame = Frame(skeleton);
            var before = frame.Clone();
            var target = new Vector3(2, 2.2f, 0.5f);

            var chain = TransformBoneCommand.SolveInverseKinematics(frame, skeleton, Hand, Upper, target);

            Assert.That(chain, Is.EqualTo(new[] { Lower, Upper }));
            Assert.That(Vector3.Distance(At(frame, skeleton, Hand), target), Is.LessThan(2e-4f));
            Assert.That((frame.Position, frame.Scale), Is.EqualTo((before.Position, before.Scale)), "only rotations change, so no bone grows or shrinks");
            foreach (var bone in new[] { Root, Pelvis, Hand, Other })
                Assert.That(frame.Rotation[bone], Is.EqualTo(before.Rotation[bone]), $"bone {bone} is outside the chain");
            Assert.That(At(frame, skeleton, Upper), Is.EqualTo(At(before, skeleton, Upper)), "the end bone turns where it stands");
        }

        [Test]
        public void ATargetOutOfReach_LeavesTheArmStretchedTowardIt()
        {
            var skeleton = Skeleton();
            var frame = Frame(skeleton);
            var shoulder = At(frame, skeleton, Upper);
            var target = shoulder + new Vector3(0, 0, 5);

            TransformBoneCommand.SolveInverseKinematics(frame, skeleton, Hand, Upper, target);

            Assert.That(Vector3.Distance(At(frame, skeleton, Hand), shoulder + new Vector3(0, 0, 2)), Is.LessThan(1e-2f));
        }

        [Test]
        public void TheChain_StopsAtTheEndBone_AndNeverTurnsTheRoot()
        {
            var skeleton = Skeleton();

            Assert.That(TransformBoneCommand.InverseKinematicsChain(skeleton, Hand, Lower), Is.EqualTo(new[] { Lower }));
            Assert.That(TransformBoneCommand.InverseKinematicsChain(skeleton, Hand, Root), Is.EqualTo(new[] { Lower, Upper, Pelvis }));
            Assert.That(TransformBoneCommand.InverseKinematicsChain(skeleton, Hand, Other), Is.EqualTo(new[] { Lower, Upper, Pelvis }), "an end bone off the chain");
        }

        // The gizmo need not be where the bone is in the key's space (a rider sits on its mount), so the hand moves as far as the
        // gizmo did, and the whole drag is one undo step.
        [Test]
        public void ADrag_MovesTheHandAsFarAsTheGizmo_AndUndoesAndRedoes()
        {
            var skeleton = Skeleton();
            var clip = new AnimationClip();
            clip.DynamicFrames.Add(Frame(skeleton));
            var loaded = clip.DynamicFrames[0].Clone();
            var state = new BoneSelectionState(null!) { CurrentAnimation = clip, Skeleton = skeleton, CurrentFrame = 0, EnableInverseKinematics = true, InverseKinematicsEndBoneIndex = Upper };
            state.BoneModifiedEvent += _ => { };
            var command = new TransformBoneCommand(null!);
            command.Configure([Hand], state);
            var gizmo = new Vector3(100, 0, 0);
            var move = new Vector3(-0.5f, 0.8f, 0.3f);

            command.ApplyTransformation(Matrix.CreateTranslation(gizmo), GizmoMode.Translate);
            command.ApplyTransformation(Matrix.CreateTranslation(gizmo + move), GizmoMode.Translate);
            command.Execute();
            var dragged = clip.DynamicFrames[0].Clone();

            Assert.That(Vector3.Distance(At(dragged, skeleton, Hand), At(loaded, skeleton, Hand) + move), Is.LessThan(2e-4f));
            command.Undo();
            Assert.That(clip.DynamicFrames[0].Rotation, Is.EqualTo(loaded.Rotation));
            command.Redo();
            Assert.That(clip.DynamicFrames[0].Rotation, Is.EqualTo(dragged.Rotation));
        }
    }
}
