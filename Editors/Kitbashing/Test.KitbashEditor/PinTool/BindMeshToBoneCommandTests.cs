using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Test.KitbashEditor.PinTool
{
    // The Pin tool's Bone mode: every vertex goes 100% to one bone of the model's skeleton, as one undo step.
    [TestFixture]
    public class BindMeshToBoneCommandTests
    {
        [Test]
        public void Execute_WeightsEveryVertexFullyToTheBone()
        {
            var mesh = CreateMeshNode("old_skeleton", UiVertexFormat.Cinematic,
                Vertex(new Vector4(3, 4, 5, 6), new Vector4(0.4f, 0.3f, 0.2f, 0.1f)),
                Vertex(new Vector4(7, 0, 0, 0), new Vector4(1, 0, 0, 0)));

            var command = new BindMeshToBoneCommand();
            command.Configure([mesh], 12, "humanoid01");
            command.Execute();

            foreach (var vertex in mesh.Geometry.VertexArray)
            {
                Assert.That(vertex.BlendIndices.X, Is.EqualTo(12));
                Assert.That(vertex.BlendWeights, Is.EqualTo(new Vector4(1, 0, 0, 0)));
            }
            Assert.That(mesh.Geometry.SkeletonName, Is.EqualTo("humanoid01"));
            Assert.That(mesh.Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Cinematic), "a skinned mesh keeps its format");
        }

        [Test]
        public void Execute_StaticMesh_TakesTheFormatTheModelSkinsWith()
        {
            var model = new Rmv2ModelNode("model");
            var lod = model.AddObject(new Rmv2LodNode("lod", 0));
            lod.AddObject(CreateMeshNode("humanoid01", UiVertexFormat.Cinematic, Vertex(Vector4.Zero, new Vector4(1, 0, 0, 0))));
            var prop = lod.AddObject(CreateMeshNode("", UiVertexFormat.Static, Vertex(Vector4.Zero, Vector4.Zero)));
            prop.AnimationMatrixOverride = 4;

            var command = new BindMeshToBoneCommand();
            command.Configure([prop], 9, "humanoid01");
            command.Execute();

            Assert.That(prop.Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Cinematic));
            Assert.That(prop.Geometry.VertexArray[0].BlendIndices.X, Is.EqualTo(9));
            Assert.That(prop.Geometry.VertexArray[0].BlendWeights.X, Is.EqualTo(1));
            Assert.That(prop.AnimationMatrixOverride, Is.EqualTo(-1), "a skinned mesh does not also follow a rigid bone");
        }

        [Test]
        public void Execute_StaticMeshWithNoSkinnedSibling_BecomesWeighted()
        {
            var prop = CreateMeshNode("", UiVertexFormat.Static, Vertex(Vector4.Zero, Vector4.Zero));

            var command = new BindMeshToBoneCommand();
            command.Configure([prop], 2, "humanoid01");
            command.Execute();

            Assert.That(prop.Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Weighted));
            Assert.That(prop.Geometry.SkeletonName, Is.EqualTo("humanoid01"));
        }

        [Test]
        public void Undo_RestoresVertexDataFormatSkeletonAndMatrixIndex()
        {
            var prop = CreateMeshNode("", UiVertexFormat.Static, Vertex(new Vector4(5, 0, 0, 0), new Vector4(0.25f, 0, 0, 0)));
            prop.AnimationMatrixOverride = 4;
            var before = prop.Geometry.VertexArray.ToArray();

            var command = new BindMeshToBoneCommand();
            command.Configure([prop], 9, "humanoid01");
            command.Execute();
            command.Undo();

            Assert.That(prop.Geometry.VertexArray, Is.EqualTo(before));
            Assert.That(prop.Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Static));
            Assert.That(prop.Geometry.SkeletonName, Is.EqualTo(""));
            Assert.That(prop.AnimationMatrixOverride, Is.EqualTo(4));
        }

        static VertexPositionNormalTextureCustom Vertex(Vector4 boneIndices, Vector4 blendWeights) => new()
        {
            Position = new Vector4(0, 0, 0, 1),
            Normal = Vector3.Up,
            BlendIndices = boneIndices,
            BlendWeights = blendWeights,
        };

        static Rmv2MeshNode CreateMeshNode(string skeletonName, UiVertexFormat format, params VertexPositionNormalTextureCustom[] vertices)
        {
            var context = new Mock<IGraphicsCardGeometry>();
            context.Setup(x => x.Clone()).Returns(() => new Mock<IGraphicsCardGeometry>().Object);

            var mesh = new MeshObject(context.Object, skeletonName) { VertexArray = vertices, IndexArray = [0, 0, 0] };
            mesh.ChangeVertexType(format, false);
            mesh.UpdateSkeletonName(skeletonName);

            var material = new Mock<IRmvMaterial>();
            material.Setup(m => m.ModelName).Returns("test_mesh");
            material.Setup(m => m.Clone()).Returns(() => material.Object);
            var shader = new Mock<CapabilityMaterial>(MockBehavior.Loose, CapabilityMaterialsEnum.SpecGlossPbr_Default, ShaderTypes.Pbr_SpecGloss, null!);

            return new Rmv2MeshNode(mesh, material.Object, shader.Object, null!);
        }
    }
}
