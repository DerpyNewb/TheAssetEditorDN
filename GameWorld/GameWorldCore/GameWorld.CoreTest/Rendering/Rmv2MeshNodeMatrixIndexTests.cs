using GameWorld.Core.SceneNodes;
using GameWorld.Core.Test.TestUtility;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace GameWorld.Core.Test.Rendering
{
    // A MatrixIndex >= 0 pins a mesh rigidly to that bone, with skinning off. Vanilla WH3 sets one
    // only on static meshes (building destruction pieces); every weighted or cinematic vanilla mesh
    // has -1. Some modding tools write 0 on skinned character meshes, which the game still skins,
    // so reading it as a pin froze the whole body on the root bone in bind pose.
    public class Rmv2MeshNodeMatrixIndexTests
    {
        [TestCase(UiVertexFormat.Static, 0, 0)]
        [TestCase(UiVertexFormat.Static, 3, 3)]
        [TestCase(UiVertexFormat.Static, -1, -1)]
        [TestCase(UiVertexFormat.Cinematic, 0, -1)]
        [TestCase(UiVertexFormat.Weighted, 2, -1)]
        public void MatrixIndex_PinsOnlyStaticMeshes(UiVertexFormat format, int matrixIndex, int expectedOverride)
        {
            var geometry = MeshTestHelper.CreateTriangleMesh();
            geometry.ChangeVertexType(format);

            var node = new Rmv2MeshNode(geometry, new WeightedMaterial { MatrixIndex = matrixIndex }, null!, null!);

            Assert.That(node.AnimationMatrixOverride, Is.EqualTo(expectedOverride));
        }
    }
}
