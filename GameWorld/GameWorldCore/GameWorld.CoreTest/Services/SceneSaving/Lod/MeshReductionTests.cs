using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Services.SceneSaving.Lod.MeshDecimatorIntegration;
using GameWorld.Core.Services.SceneSaving.Lod.MeshOptimizerIntegration;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Test.Services.SceneSaving.Lod
{
    internal class MeshReductionTests
    {
        static IEnumerable<TestCaseData> Reducers()
        {
            yield return new TestCaseData((Func<MeshObject, float, MeshObject>)DecimatorMeshOptimizer.GetReducedMeshCopy).SetArgDisplayNames("FastQuadric");
            yield return new TestCaseData((Func<MeshObject, float, MeshObject>)MeshOptimizerSimplifier.GetReducedMeshCopy).SetArgDisplayNames("MeshOptimizer");
        }

        [TestCase]
        public void MeshOptimizer_FactorOne_DrawsTheSameTriangles()
        {
            // Arrange
            var mesh = CreateTube(16, 10);

            // Act
            var reduced = MeshOptimizerSimplifier.GetReducedMeshCopy(mesh, 1);

            // Assert
            var originalCorners = mesh.IndexArray.Select(i => mesh.VertexArray[i]);
            var reducedCorners = reduced.IndexArray.Select(i => reduced.VertexArray[i]);
            Assert.That(reducedCorners, Is.EqualTo(originalCorners));
            Assert.That(reduced.VertexArray, Has.Length.EqualTo(reduced.IndexArray.Distinct().Count()));
        }

        [TestCase]
        public void MeshOptimizer_TargetBelowWhatTheOpenEdgesAllow_KeepsTheShape()
        {
            // Arrange
            var mesh = CreateSheetWithSphere();

            // Act
            var reduced = MeshOptimizerSimplifier.GetReducedMeshCopy(mesh, 0.1f);

            // Assert
            Assert.That(MaxDistanceToSurface(mesh, reduced), Is.LessThan(0.05f * Diagonal(mesh)));
        }

        [TestCaseSource(nameof(Reducers))]
        public void Reduce_HalfFactor_KeepsHalfTheTriangles(Func<MeshObject, float, MeshObject> reduce)
        {
            // Arrange
            var mesh = CreateWavyGrid(21, mirrored: false);
            var originalTriangles = mesh.IndexArray.Length / 3;

            // Act
            var reduced = reduce(mesh, 0.5f);

            // Assert
            var reducedTriangles = reduced.IndexArray.Length / 3;
            Assert.That(reducedTriangles, Is.InRange(originalTriangles * 0.45, originalTriangles * 0.5 + 2));
        }

        [TestCaseSource(nameof(Reducers))]
        public void Reduce_MirroredTangentFrame_KeepsHandedness(Func<MeshObject, float, MeshObject> reduce)
        {
            // Arrange
            var mesh = CreateWavyGrid(21, mirrored: true);

            // Act
            var reduced = reduce(mesh, 0.5f);

            // Assert
            var rightHanded = reduced.VertexArray.Count(v => Vector3.Dot(Vector3.Cross(v.Normal, v.Tangent), v.BiNormal) > 0);
            Assert.That(rightHanded, Is.EqualTo(0));
        }

        [TestCaseSource(nameof(Reducers))]
        public void Reduce_UvSeam_KeepsOneUvPerSideAtEachPosition(Func<MeshObject, float, MeshObject> reduce)
        {
            // Arrange
            var mesh = CreateTube(16, 10);

            // Act
            var reduced = reduce(mesh, 0.5f);

            // Assert
            var mostUvsAtOnePosition = UsedVertices(reduced)
                .GroupBy(PositionKey)
                .Max(g => g.Select(v => v.TextureCoordinate).Distinct().Count());
            Assert.That(mostUvsAtOnePosition, Is.LessThanOrEqualTo(2));
        }

        [TestCaseSource(nameof(Reducers))]
        public void Reduce_UvSeam_NoTriangleStretchesAcrossTheTexture(Func<MeshObject, float, MeshObject> reduce)
        {
            // Arrange
            var mesh = CreateTube(16, 10);

            // Act
            var reduced = reduce(mesh, 0.1f);

            // Assert
            var stretched = 0;
            for (var i = 0; i < reduced.IndexArray.Length; i += 3)
            {
                var u = Enumerable.Range(0, 3).Select(k => reduced.VertexArray[reduced.IndexArray[i + k]].TextureCoordinate.X).ToArray();
                if (u.Max() - u.Min() > 0.5f)
                    stretched++;
            }
            Assert.That(stretched, Is.EqualTo(0));
        }

        [TestCaseSource(nameof(Reducers))]
        public void Reduce_OpenEdge_KeepsEveryVertex(Func<MeshObject, float, MeshObject> reduce)
        {
            // Arrange: flat, so dropping a vertex from a straight edge costs nothing unless the edge is locked
            var mesh = CreateWavyGrid(21, mirrored: false, height: 0);
            var rimPositions = mesh.VertexArray.Where(v => v.Position.X is 0 or 1 || v.Position.Y is 0 or 1).Select(PositionKey).ToHashSet();

            // Act
            var reduced = reduce(mesh, 0.1f);

            // Assert
            var reducedPositions = UsedVertices(reduced).Select(PositionKey).ToHashSet();
            Assert.That(rimPositions.Except(reducedPositions), Is.Empty);
        }

        static IEnumerable<VertexPositionNormalTextureCustom> UsedVertices(MeshObject mesh) => mesh.IndexArray.Distinct().Select(i => mesh.VertexArray[i]);

        static float Diagonal(MeshObject mesh)
        {
            var positions = mesh.VertexArray.Select(v => v.Position3()).ToList();
            return Vector3.Distance(positions.Aggregate(Vector3.Min), positions.Aggregate(Vector3.Max));
        }

        // How far the reduced surface strays from the original, measured at every original vertex
        static float MaxDistanceToSurface(MeshObject original, MeshObject reduced)
        {
            var worst = 0f;
            foreach (var p in UsedVertices(original).Select(v => v.Position3()))
            {
                var nearest = float.MaxValue;
                for (var i = 0; i < reduced.IndexArray.Length; i += 3)
                {
                    var a = reduced.VertexArray[reduced.IndexArray[i]].Position3();
                    var b = reduced.VertexArray[reduced.IndexArray[i + 1]].Position3();
                    var c = reduced.VertexArray[reduced.IndexArray[i + 2]].Position3();
                    nearest = MathF.Min(nearest, Vector3.Distance(p, ClosestPointOnTriangle(p, a, b, c)));
                }
                worst = MathF.Max(worst, nearest);
            }
            return worst;
        }

        // Ericson, Real-Time Collision Detection 5.1.5
        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            var d1 = Vector3.Dot(ab, ap); var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            var bp = p - b;
            var d3 = Vector3.Dot(ab, bp); var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            var cp = p - c;
            var d5 = Vector3.Dot(ab, cp); var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            var va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            var denom = 1 / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        static (float, float, float) PositionKey(VertexPositionNormalTextureCustom v) => (MathF.Round(v.Position.X, 4), MathF.Round(v.Position.Y, 4), MathF.Round(v.Position.Z, 4));

        // A height field over the unit square, about two triangles per vertex like a real model.
        static MeshObject CreateWavyGrid(int size, bool mirrored, float height = 0.1f)
        {
            var vertices = new List<VertexPositionNormalTextureCustom>();
            for (var j = 0; j < size; j++)
            {
                for (var i = 0; i < size; i++)
                {
                    var x = i / (float)(size - 1);
                    var y = j / (float)(size - 1);
                    var dzdx = 6 * height * MathF.Cos(6 * x) * MathF.Cos(6 * y);
                    var dzdy = -6 * height * MathF.Sin(6 * x) * MathF.Sin(6 * y);
                    var normal = Vector3.Normalize(new Vector3(-dzdx, -dzdy, 1));
                    var tangent = Vector3.Normalize(new Vector3(1, 0, dzdx));
                    var binormal = Vector3.Cross(normal, tangent) * (mirrored ? -1 : 1);

                    vertices.Add(new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(x, y, height * MathF.Sin(6 * x) * MathF.Cos(6 * y), 1),
                        Normal = normal,
                        Tangent = tangent,
                        BiNormal = binormal,
                        TextureCoordinate = new Vector2(x, y),
                    });
                }
            }

            var indices = new List<ushort>();
            for (var j = 0; j < size - 1; j++)
            {
                for (var i = 0; i < size - 1; i++)
                {
                    var v00 = (ushort)(j * size + i);
                    var v10 = (ushort)(v00 + 1);
                    var v01 = (ushort)(v00 + size);
                    var v11 = (ushort)(v01 + 1);
                    indices.AddRange([v00, v10, v11, v00, v11, v01]);
                }
            }

            return CreateMesh(vertices, indices);
        }

        // An open tube whose UVs wrap once around it, so the last column repeats the first column's positions at u = 1.
        static MeshObject CreateTube(int segments, int rings)
        {
            var vertices = new List<VertexPositionNormalTextureCustom>();
            for (var r = 0; r < rings; r++)
            {
                for (var c = 0; c <= segments; c++)
                {
                    var height = r / (float)(rings - 1);
                    var angle = MathF.Tau * (c % segments) / segments + height * 2;
                    var radius = 1 + 0.3f * MathF.Sin(height * 9);
                    var normal = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
                    var tangent = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0);

                    vertices.Add(new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(normal.X * radius, normal.Y * radius, height * 2, 1),
                        Normal = normal,
                        Tangent = tangent,
                        BiNormal = Vector3.Cross(normal, tangent),
                        TextureCoordinate = new Vector2(c / (float)segments, height),
                    });
                }
            }

            var columns = segments + 1;
            var indices = new List<ushort>();
            for (var r = 0; r < rings - 1; r++)
            {
                for (var c = 0; c < segments; c++)
                {
                    var v00 = (ushort)(r * columns + c);
                    var v10 = (ushort)(v00 + 1);
                    var v01 = (ushort)(v00 + columns);
                    var v11 = (ushort)(v01 + 1);
                    indices.AddRange([v00, v10, v11, v00, v11, v01]);
                }
            }

            return CreateMesh(vertices, indices);
        }

        // An open sheet, whose locked edges stop it shrinking far, with a closed sphere above it that could be reduced to nothing.
        static MeshObject CreateSheetWithSphere()
        {
            var sheet = CreateWavyGrid(21, mirrored: false);
            var vertices = sheet.VertexArray.ToList();
            var indices = sheet.IndexArray.ToList();

            const int stacks = 8;
            const int slices = 16;
            var first = vertices.Count;
            for (var r = 0; r <= stacks; r++)
            {
                for (var c = 0; c <= slices; c++)
                {
                    var theta = MathF.PI * r / stacks;
                    var phi = MathF.Tau * (c % slices) / slices;
                    var normal = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Sin(theta) * MathF.Sin(phi), MathF.Cos(theta));
                    if (r == 0 || r == stacks)
                        normal = new Vector3(0, 0, MathF.Cos(theta));
                    var position = new Vector3(0.5f, 0.5f, 0.8f) + normal * 0.3f;
                    vertices.Add(new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(position, 1),
                        Normal = normal,
                        Tangent = Vector3.UnitX,
                        BiNormal = Vector3.Cross(normal, Vector3.UnitX),
                        TextureCoordinate = new Vector2(c / (float)slices, r / (float)stacks),
                    });
                }
            }

            for (var r = 0; r < stacks; r++)
            {
                for (var c = 0; c < slices; c++)
                {
                    var v00 = (ushort)(first + r * (slices + 1) + c);
                    var v01 = (ushort)(v00 + 1);
                    var v10 = (ushort)(v00 + slices + 1);
                    var v11 = (ushort)(v10 + 1);
                    if (r != 0)
                        indices.AddRange([v00, v01, v11]);
                    if (r != stacks - 1)
                        indices.AddRange([v00, v11, v10]);
                }
            }

            return CreateMesh(vertices, indices);
        }

        static MeshObject CreateMesh(List<VertexPositionNormalTextureCustom> vertices, List<ushort> indices)
        {
            var mesh = new MeshObject(new TestGraphicsCardGeometry(), "test_skeleton");
            mesh.ChangeVertexType(UiVertexFormat.Static, false);
            mesh.VertexArray = vertices.ToArray();
            mesh.IndexArray = indices.ToArray();
            return mesh;
        }
    }
}
