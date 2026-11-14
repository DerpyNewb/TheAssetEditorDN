using GameWorld.Core.Commands.Object;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Test.Rendering.Geometry;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace GameWorld.Core.Test.Commands.Object
{
    internal class EditMeshesCommandTests
    {
        [Test]
        public void Undo_GivesBackTheExactGeometryAndPivot_AndRedoEditsAgain()
        {
            var node = new Rmv2MeshNode(MeshEditsTests.CreateWavyGrid(5), new WeightedMaterial(), null!, null!) { PivotPoint = new Vector3(1, 2, 3) };
            var original = node.Geometry;
            var vertices = (VertexPositionNormalTextureCustom[])original.VertexArray.Clone();
            var indices = (ushort[])original.IndexArray.Clone();
            var command = new EditMeshesCommand();
            command.Configure([node], "Mirror", x =>
            {
                MeshEdits.Mirror(x.Geometry, null);
                x.PivotPoint = new Vector3(-x.PivotPoint.X, x.PivotPoint.Y, x.PivotPoint.Z);
            });

            command.Execute();
            Assert.That(node.Geometry.VertexArray[0].Position.X, Is.EqualTo(-vertices[0].Position.X));
            Assert.That(node.PivotPoint, Is.EqualTo(new Vector3(-1, 2, 3)));
            Assert.That(original.VertexArray, Is.EqualTo(vertices), "the edit changed the geometry undo hands back");
            Assert.That(original.IndexArray, Is.EqualTo(indices), "the edit changed the geometry undo hands back");

            command.Undo();
            Assert.That(node.Geometry, Is.SameAs(original));
            Assert.That(node.Geometry.VertexArray, Is.EqualTo(vertices));
            Assert.That(node.Geometry.IndexArray, Is.EqualTo(indices));
            Assert.That(node.PivotPoint, Is.EqualTo(new Vector3(1, 2, 3)));

            command.Execute();
            Assert.That(node.Geometry.VertexArray[0].Position.X, Is.EqualTo(-vertices[0].Position.X));
            Assert.That(node.PivotPoint, Is.EqualTo(new Vector3(-1, 2, 3)));
        }
    }
}
