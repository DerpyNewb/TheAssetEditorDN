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

        [Test]
        public void BakePivot_MovesThePivotIntoEveryVertex()
        {
            var mesh = CreateWavyGrid(3);
            var before = (VertexPositionNormalTextureCustom[])mesh.VertexArray.Clone();

            MeshEdits.BakePivot(mesh, new Vector3(0.5f, -2, 3));

            Assert.That(mesh.VertexArray.Select(x => x.Position), Is.EqualTo(before.Select(x => x.Position + new Vector4(0.5f, -2, 3, 0))));
        }

        [Test]
        public void FlipFaces_TurnsTheFacesRound_AndKeepsCaConventions()
        {
            var mesh = CreateWavyGrid(5);
            var before = (VertexPositionNormalTextureCustom[])mesh.VertexArray.Clone();

            MeshEdits.FlipFaces(mesh);

            Assert.That(mesh.VertexArray.Select(x => x.Normal), Is.EqualTo(before.Select(x => -x.Normal)));
            Assert.That(ConventionBreaks(mesh), Is.Empty);
        }

        [Test]
        public void RecalculateNormals_KeepsACubesEdgesHard()
        {
            var mesh = CreateCube();
            var faceAxes = mesh.VertexArray.Select(x => x.Normal).ToList();
            Scramble(mesh);

            MeshEdits.RecalculateNormals(mesh);

            for (var i = 0; i < faceAxes.Count; i++)
                Assert.That(Vector3.Distance(mesh.VertexArray[i].Normal, faceAxes[i]), Is.LessThan(1e-5f), $"vertex {i} took a neighbouring face's normal");
            Assert.That(ConventionBreaks(mesh), Is.Empty);
            AssertFramesSquare(mesh);
        }

        [Test]
        public void RecalculateNormals_SmoothsASphere_WithNoSeamWhereItsUvsWrap()
        {
            var mesh = CreateSphere(16, 9);
            Scramble(mesh);

            MeshEdits.RecalculateNormals(mesh);

            // The open rim has faces on one side only, so it leans; every ring inside it should point straight out.
            foreach (var v in mesh.VertexArray.Where(x => MathF.Abs(x.Position.Y) < 0.9f))
                Assert.That(MathHelper.ToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(v.Normal, Vector3.Normalize(Pos(v))), -1, 1))), Is.LessThan(2), $"{Pos(v)} is not smooth");
            var seams = mesh.VertexArray.Select((v, i) => (v, i)).GroupBy(x => Pos(x.v)).Where(x => x.Count() > 1).ToList();
            Assert.That(seams.Count, Is.EqualTo(9), "the sphere has no uv seam to test");
            foreach (var seam in seams)
                Assert.That(seam.Select(x => x.v.Normal).Distinct().Count(), Is.EqualTo(1), $"the seam at {seam.Key} shades in two ways");
            Assert.That(ConventionBreaks(mesh), Is.Empty);
            AssertFramesSquare(mesh);
        }

        // Mirrored uvs, as on the second half of a symmetric model, turn the frame's handedness round.
        [TestCase(false)]
        [TestCase(true)]
        public void RecalculateNormals_BuildsCasFrame_OnPlainAndMirroredUvs(bool mirroredUvs)
        {
            var mesh = CreateWavyGrid(6, mirroredUvs);
            Scramble(mesh);

            MeshEdits.RecalculateNormals(mesh);

            Assert.That(ConventionBreaks(mesh), Is.Empty);
            AssertFramesSquare(mesh);
        }

        static void AssertFramesSquare(MeshObject mesh)
        {
            foreach (var v in mesh.VertexArray)
            {
                Assert.That(MathF.Abs(Vector3.Dot(v.Normal, v.Tangent)), Is.LessThan(1e-5f), "tangent not square to the normal");
                Assert.That(MathF.Abs(Vector3.Dot(v.BiNormal, v.Tangent)) + MathF.Abs(Vector3.Dot(v.BiNormal, v.Normal)), Is.LessThan(1e-5f), "binormal not square to the others");
                Assert.That(v.Tangent.Length(), Is.EqualTo(1).Within(1e-5f));
                Assert.That(v.BiNormal.Length(), Is.EqualTo(1).Within(1e-5f));
            }
        }

        // Normals, tangents and binormals that point nowhere useful, so nothing passes by leaving them alone.
        static void Scramble(MeshObject mesh)
        {
            for (var i = 0; i < mesh.VertexArray.Length; i++)
            {
                mesh.VertexArray[i].Normal = Vector3.Normalize(new Vector3(0.3f, -1, 0.2f * i));
                mesh.VertexArray[i].Tangent = Vector3.UnitY;
                mesh.VertexArray[i].BiNormal = -Vector3.UnitY;
            }
        }

        // 24 vertices, four per side, so every edge is hard. Each side's normal is set as the expected answer.
        static MeshObject CreateCube()
        {
            var vertices = new List<VertexPositionNormalTextureCustom>();
            var indices = new List<ushort>();
            foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
            {
                // Two axes across the side with cross(across, up) = normal, so the corners wind counter-clockwise about it.
                var across = Math.Abs(normal.Y) > 0 ? Vector3.UnitX * normal.Y : Vector3.Cross(Vector3.UnitY, normal);
                var up = Vector3.Cross(normal, across);
                var first = (ushort)vertices.Count;
                foreach (var (u, v) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
                {
                    var p = normal + across * (2 * u - 1) + up * (2 * v - 1);
                    vertices.Add(new VertexPositionNormalTextureCustom { Position = new Vector4(p, 1), Normal = normal, TextureCoordinate = new Vector2(u, v) });
                }
                indices.AddRange([first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)]);
            }
            return CreateMesh(vertices, indices);
        }

        // A sphere without its poles, u around and v up; the last column repeats the first column's places at u = 1.
        static MeshObject CreateSphere(int segments, int rings)
        {
            var vertices = new List<VertexPositionNormalTextureCustom>();
            for (var r = 0; r < rings; r++)
            {
                var latitude = MathHelper.ToRadians(-70 + 140f * r / (rings - 1));
                for (var c = 0; c <= segments; c++)
                {
                    var around = MathF.Tau * (c % segments) / segments;
                    var p = new Vector3(MathF.Cos(around) * MathF.Cos(latitude), MathF.Sin(latitude), -MathF.Sin(around) * MathF.Cos(latitude));
                    vertices.Add(new VertexPositionNormalTextureCustom { Position = new Vector4(p, 1), TextureCoordinate = new Vector2(c / (float)segments, r / (float)(rings - 1)) });
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

        static MeshObject CreateMesh(List<VertexPositionNormalTextureCustom> vertices, List<ushort> indices)
        {
            var mesh = new MeshObject(new TestGraphicsCardGeometry(), "test_skeleton");
            mesh.ChangeVertexType(UiVertexFormat.Static, false);
            mesh.VertexArray = vertices.ToArray();
            mesh.IndexArray = indices.ToArray();
            return mesh;
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
        internal static MeshObject CreateWavyGrid(int size, bool mirroredUvs = false)
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
                        TextureCoordinate = new Vector2(mirroredUvs ? 1 - u : u, v),
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
