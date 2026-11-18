using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel.Vertex;

namespace Test.ImportExport.Importing.Importers;

// RMV2 indexes vertices with 16 bits. A glTF mesh past 65,536 vertices used to be refused; it is now cut into parts by
// whole triangles, and every triangle must come back with the same three corners.
public class GltfMeshSplitTests
{
    [Test]
    public void AMeshOverTheLimitIsCutIntoPartsThatKeepEveryTriangle()
    {
        // A strip: triangle t uses vertices t, t + 1 and t + 2, so neighbours share two corners, as real meshes do.
        const int VertexCount = 70_000;
        var vertices = Enumerable.Range(0, VertexCount).Select(i => new CommonVertex { Position = new Vector4(i, i % 7, i % 13, 1) }).ToArray();
        var indices = Enumerable.Range(0, VertexCount - 2).SelectMany(t => new[] { t, t + 1, t + 2 }).ToArray();

        var parts = RmvMeshBuilder.SplitAtVertexLimit(vertices, indices);

        Assert.That(parts, Has.Count.EqualTo(2));
        Assert.That(parts.Select(p => p.VertexList.Length), Has.All.LessThanOrEqualTo(65_536));
        var before = Triangles(vertices, indices.Select(i => i)).ToList();
        var after = parts.SelectMany(p => Triangles(p.VertexList, p.IndexList.Select(i => (int)i))).ToList();
        Assert.That(after, Is.EqualTo(before), "every triangle, in order, with the same corners");
    }

    [Test]
    public void AMeshAtTheLimitStaysOnePart()
    {
        var vertices = Enumerable.Range(0, 65_536).Select(i => new CommonVertex { Position = new Vector4(i, 0, 0, 1) }).ToArray();
        var indices = Enumerable.Range(0, 65_534).SelectMany(t => new[] { t, t + 1, t + 2 }).ToArray();

        Assert.That(RmvMeshBuilder.SplitAtVertexLimit(vertices, indices), Has.Count.EqualTo(1));
    }

    static IEnumerable<(float, float, float)> Triangles(CommonVertex[] vertices, IEnumerable<int> indices) =>
        indices.Chunk(3).Select(t => (vertices[t[0]].Position.X, vertices[t[1]].Position.X, vertices[t[2]].Position.X));
}
