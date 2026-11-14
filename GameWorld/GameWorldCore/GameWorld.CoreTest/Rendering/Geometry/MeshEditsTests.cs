using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Test.Rendering.Geometry
{
    internal class MeshEditsTests
    {
        [Test]
        public void Mirror_ReflectsAcrossX_AndKeepsCaConventions()
        {
            var mesh = CreateWavyGrid(6);
            var before = (VertexPositionNormalTextureCustom[])mesh.VertexArray.Clone();
            Assert.That(ConventionBreaks(mesh), Is.Empty, "the test mesh itself breaks the conventions");

            MeshEdits.Mirror(mesh, null);

            for (var i = 0; i < before.Length; i++)
            {
                Assert.That(mesh.VertexArray[i].Position, Is.EqualTo(new Vector4(-before[i].Position.X, before[i].Position.Y, before[i].Position.Z, before[i].Position.W)));
                Assert.That(mesh.VertexArray[i].Normal, Is.EqualTo(new Vector3(-before[i].Normal.X, before[i].Normal.Y, before[i].Normal.Z)));
                Assert.That(mesh.VertexArray[i].Tangent, Is.EqualTo(new Vector3(-before[i].Tangent.X, before[i].Tangent.Y, before[i].Tangent.Z)));
                Assert.That(mesh.VertexArray[i].BiNormal, Is.EqualTo(new Vector3(-before[i].BiNormal.X, before[i].BiNormal.Y, before[i].BiNormal.Z)));
            }
            Assert.That(before.Count(x => MathF.Abs(x.Tangent.X) > 0.1f && MathF.Abs(x.BiNormal.X) > 0.1f), Is.EqualTo(before.Length), "a frame with no x part cannot show a missing reflection");
            Assert.That(ConventionBreaks(mesh), Is.Empty);
        }

        [Test]
        public void Mirror_MovesWeightsOntoTheOppositeBones()
        {
            var mesh = CreateWavyGrid(3);
            mesh.ChangeVertexType(UiVertexFormat.Weighted, false);
            for (var i = 0; i < mesh.VertexArray.Length; i++)
            {
                mesh.VertexArray[i].BlendIndices = new Vector4(1, 7, 0, 0);
                mesh.VertexArray[i].BlendWeights = new Vector4(0.75f, 0.25f, 0, 0);
            }

            var swap = MeshEdits.LeftRightBoneSwap(Bones);
            MeshEdits.Mirror(mesh, swap);

            Assert.That(mesh.VertexArray.Select(x => x.BlendIndices).Distinct(), Is.EqualTo(new[] { new Vector4(2, 7, 0, 0) }));
            Assert.That(mesh.VertexArray.Select(x => x.BlendWeights).Distinct(), Is.EqualTo(new[] { new Vector4(0.75f, 0.25f, 0, 0) }));
        }

        static readonly Dictionary<int, string> Bones = new()
        {
            [0] = "root",
            [1] = "hand_left",
            [2] = "hand_right",
            [3] = "l_eye_0",
            [4] = "r_eye_0",
            [5] = "lefteye",
            [6] = "righteye",
            [7] = "cape_left_0",
            [8] = "upperleg_left",
            [9] = "upperleg_right",
        };

        [Test]
        public void LeftRightBoneSwap_PairsBonesByName()
        {
            var swap = MeshEdits.LeftRightBoneSwap(Bones);

            Assert.That(swap, Is.EquivalentTo(new Dictionary<int, int> { [1] = 2, [2] = 1, [3] = 4, [4] = 3, [5] = 6, [6] = 5, [8] = 9, [9] = 8 }));
        }

        // Every triangle against CA's conventions: the winding agrees with the vertex normals, the tangent runs along +u, the
        // binormal along +v, and cross(normal, tangent) . binormal has the sign of the uv area.
        internal static List<string> ConventionBreaks(MeshObject mesh)
        {
            var breaks = new List<string>();
            var v = mesh.VertexArray;
            var idx = mesh.IndexArray;
            for (var i = 0; i < idx.Length; i += 3)
            {
                var (a, b, c) = (v[idx[i]], v[idx[i + 1]], v[idx[i + 2]]);
                var p0 = Pos(a);
                var e1 = Pos(b) - p0;
                var e2 = Pos(c) - p0;
                if (Vector3.Dot(Vector3.Cross(e1, e2), a.Normal + b.Normal + c.Normal) <= 0)
                    breaks.Add($"face {i / 3} winds against its normals");

                var s1 = b.TextureCoordinate.X - a.TextureCoordinate.X;
                var s2 = c.TextureCoordinate.X - a.TextureCoordinate.X;
                var t1 = b.TextureCoordinate.Y - a.TextureCoordinate.Y;
                var t2 = c.TextureCoordinate.Y - a.TextureCoordinate.Y;
                var det = s1 * t2 - s2 * t1;
                var dPdu = (e1 * t2 - e2 * t1) / det;
                var dPdv = (e2 * s1 - e1 * s2) / det;
                foreach (var x in new[] { a, b, c })
                {
                    if (Vector3.Dot(x.Tangent, dPdu) <= 0)
                        breaks.Add($"face {i / 3}: tangent against +u");
                    if (Vector3.Dot(x.BiNormal, dPdv) <= 0)
                        breaks.Add($"face {i / 3}: binormal against +v");
                    if (MathF.Sign(Vector3.Dot(Vector3.Cross(x.Normal, x.Tangent), x.BiNormal)) != MathF.Sign(det))
                        breaks.Add($"face {i / 3}: handedness against the uv area");
                }
            }
            return breaks;
        }

        static Vector3 Pos(VertexPositionNormalTextureCustom x) => new(x.Position.X, x.Position.Y, x.Position.Z);

        // A height field off the centre line, its uvs sheared against x and y so every tangent and binormal has an x part,
        // and its frame built the way CA's is: tangent dP/du, binormal dP/dv.
        internal static MeshObject CreateWavyGrid(int size)
        {
            var vertices = new List<VertexPositionNormalTextureCustom>();
            for (var j = 0; j < size; j++)
            {
                for (var i = 0; i < size; i++)
                {
                    var u = i / (float)(size - 1);
                    var v = j / (float)(size - 1);
                    var x = 0.3f + u - 0.4f * v;
                    var y = v + 0.3f * u;
                    var dzdx = 0.6f * MathF.Cos(6 * x) * MathF.Cos(6 * y);
                    var dzdy = -0.6f * MathF.Sin(6 * x) * MathF.Sin(6 * y);
                    var tangent = Vector3.Normalize(new Vector3(1, 0.3f, dzdx + 0.3f * dzdy));
                    var binormal = Vector3.Normalize(new Vector3(-0.4f, 1, -0.4f * dzdx + dzdy));
                    vertices.Add(new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(x, y, 0.1f * MathF.Sin(6 * x) * MathF.Cos(6 * y), 1),
                        Normal = Vector3.Normalize(Vector3.Cross(tangent, binormal)),
                        Tangent = tangent,
                        BiNormal = binormal,
                        TextureCoordinate = new Vector2(u, v),
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

            var mesh = new MeshObject(new TestGraphicsCardGeometry(), "test_skeleton");
            mesh.ChangeVertexType(UiVertexFormat.Static, false);
            mesh.VertexArray = vertices.ToArray();
            mesh.IndexArray = indices.ToArray();
            return mesh;
        }
    }
}
