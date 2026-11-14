using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Rendering.Geometry
{
    // Whole-mesh edits that keep CA's vertex conventions, measured on 400 vanilla models: a face winds counter-clockwise
    // about its normal (cross(p1 - p0, p2 - p0) points along the vertex normals), the tangent runs along +u and the
    // binormal along +v, so cross(normal, tangent) . binormal takes the sign of the face's uv area.
    public static class MeshEdits
    {
        // Left is -x and right +x on every vanilla skeleton (9,608 of 10,100 left bones sit at -x), so a mirror reflects x.
        public static void Mirror(MeshObject mesh, IReadOnlyDictionary<int, int>? boneSwap)
        {
            var vertices = mesh.VertexArray;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i].Position.X = -vertices[i].Position.X;
                vertices[i].Normal.X = -vertices[i].Normal.X;
                vertices[i].Tangent.X = -vertices[i].Tangent.X;
                vertices[i].BiNormal.X = -vertices[i].BiNormal.X;

                if (boneSwap != null && mesh.VertexFormat != UiVertexFormat.Static)
                {
                    vertices[i].BlendIndices.X = Swap(vertices[i].BlendIndices.X, boneSwap);
                    vertices[i].BlendIndices.Y = Swap(vertices[i].BlendIndices.Y, boneSwap);
                    vertices[i].BlendIndices.Z = Swap(vertices[i].BlendIndices.Z, boneSwap);
                    vertices[i].BlendIndices.W = Swap(vertices[i].BlendIndices.W, boneSwap);
                }
            }

            // A reflection turns every face inside out; the reversed winding turns it back.
            ReverseWinding(mesh);
        }

        public static void ReverseWinding(MeshObject mesh)
        {
            var indices = mesh.IndexArray;
            for (var i = 0; i + 2 < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }

        // A mesh draws offset by its pivot, which Rmv2MeshNode.Render applies before the node's own transform, so moving the
        // pivot into every vertex, with the pivot then cleared, draws the mesh in the same place whatever that transform is.
        public static void BakePivot(MeshObject mesh, Vector3 pivot)
        {
            var vertices = mesh.VertexArray;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i].Position.X += pivot.X;
                vertices[i].Position.Y += pivot.Y;
                vertices[i].Position.Z += pivot.Z;
            }
        }

        // Turns the faces round: the winding reverses and the normals point the other way. The tangent and binormal still run
        // along +u and +v, so they stay as they are.
        public static void FlipFaces(MeshObject mesh)
        {
            ReverseWinding(mesh);
            var vertices = mesh.VertexArray;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i].Normal = -vertices[i].Normal;
        }

        // A face counts towards a vertex's normal when it turns less than this from the faces the vertex is on, so an edge
        // sharper than this stays hard. Recomputing the 355,612 vertices of 200 vanilla models, 60 came within 9.6 degrees of
        // CA's own normals on average, against 11.3 at 30 and 16.8 with no edge kept hard.
        public const float HardEdgeDegrees = 60;

        // Smooth normals from the faces, the faces at every vertex in the same place counting, so a uv seam leaves no seam in
        // the shading; then tangents and binormals rebuilt from the uvs.
        public static void RecalculateNormals(MeshObject mesh, float hardEdgeDegrees = HardEdgeDegrees)
        {
            var vertices = mesh.VertexArray;
            var indices = mesh.IndexArray;
            var faceNormals = new Vector3[indices.Length / 3];
            var ownFaces = new List<int>?[vertices.Length];
            var facesAt = new Dictionary<Vector3, List<int>>();
            for (var f = 0; f < faceNormals.Length; f++)
            {
                var a = Position(vertices[indices[3 * f]]);
                // Twice the face's area, so a big face counts for more than a sliver.
                faceNormals[f] = Vector3.Cross(Position(vertices[indices[3 * f + 1]]) - a, Position(vertices[indices[3 * f + 2]]) - a);
                for (var corner = 0; corner < 3; corner++)
                {
                    var v = indices[3 * f + corner];
                    (ownFaces[v] ??= []).Add(f);
                    var key = PlaceKey(vertices[v]);
                    if (!facesAt.TryGetValue(key, out var list))
                        facesAt[key] = list = [];
                    list.Add(f);
                }
            }

            var minCos = MathF.Cos(MathHelper.ToRadians(hardEdgeDegrees));
            for (var v = 0; v < vertices.Length; v++)
            {
                if (ownFaces[v] is not { } own)
                    continue;
                var ownDirection = Vector3.Zero;
                foreach (var f in own)
                    ownDirection += faceNormals[f];
                if (ownDirection.LengthSquared() == 0)
                    continue;
                ownDirection.Normalize();

                var sum = Vector3.Zero;
                foreach (var f in facesAt[PlaceKey(vertices[v])])
                {
                    if (Vector3.Dot(faceNormals[f], ownDirection) >= minCos * faceNormals[f].Length())
                        sum += faceNormals[f];
                }
                vertices[v].Normal = sum.LengthSquared() > 0 ? Vector3.Normalize(sum) : ownDirection;
            }

            RecalculateTangents(mesh);
        }

        // Each face's +u and +v directions, weighted by its area, summed per vertex. The tangent is +u made square to the
        // normal and the binormal is cross(normal, tangent), turned to run along +v.
        static void RecalculateTangents(MeshObject mesh)
        {
            var vertices = mesh.VertexArray;
            var indices = mesh.IndexArray;
            var alongU = new Vector3[vertices.Length];
            var alongV = new Vector3[vertices.Length];
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var (a, b, c) = (vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                var e1 = Position(b) - Position(a);
                var e2 = Position(c) - Position(a);
                var s1 = b.TextureCoordinate.X - a.TextureCoordinate.X;
                var s2 = c.TextureCoordinate.X - a.TextureCoordinate.X;
                var t1 = b.TextureCoordinate.Y - a.TextureCoordinate.Y;
                var t2 = c.TextureCoordinate.Y - a.TextureCoordinate.Y;
                // dP/du and dP/dv scaled by the uv area; times the area's sign they point along +u and +v.
                var uvArea = s1 * t2 - s2 * t1;
                if (uvArea == 0)
                    continue;
                var u = (e1 * t2 - e2 * t1) * MathF.Sign(uvArea);
                var w = (e2 * s1 - e1 * s2) * MathF.Sign(uvArea);
                if (u.LengthSquared() == 0 || w.LengthSquared() == 0)
                    continue;
                var area = Vector3.Cross(e1, e2).Length();
                u = Vector3.Normalize(u) * area;
                w = Vector3.Normalize(w) * area;
                for (var corner = 0; corner < 3; corner++)
                {
                    alongU[indices[i + corner]] += u;
                    alongV[indices[i + corner]] += w;
                }
            }

            for (var v = 0; v < vertices.Length; v++)
            {
                var normal = vertices[v].Normal;
                var tangent = alongU[v] - normal * Vector3.Dot(normal, alongU[v]);
                if (tangent.LengthSquared() < 1e-12f)
                    tangent = vertices[v].Tangent - normal * Vector3.Dot(normal, vertices[v].Tangent);
                if (tangent.LengthSquared() < 1e-12f)
                    tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
                tangent.Normalize();

                var binormal = Vector3.Cross(normal, tangent);
                if (Vector3.Dot(binormal, alongV[v]) < 0)
                    binormal = -binormal;
                vertices[v].Tangent = tangent;
                vertices[v].BiNormal = binormal;
            }
        }

        static Vector3 Position(VertexPositionNormalTextureCustom v) => new(v.Position.X, v.Position.Y, v.Position.Z);

        // Adding 0 turns -0 into 0, so two vertices in one place share a key.
        static Vector3 PlaceKey(VertexPositionNormalTextureCustom v) => new(v.Position.X + 0f, v.Position.Y + 0f, v.Position.Z + 0f);

        static float Swap(float index, IReadOnlyDictionary<int, int> boneSwap) => boneSwap.TryGetValue((int)index, out var other) ? other : index;

        // Each bone's opposite by name. Vanilla marks a side with a whole word: left/right (20,000 bones), l/r (bigcat02c's
        // l_eye_0) or lft/rgt, and a few glue it on (humanoid02's lefteye). The words are swapped first, then the glued
        // form; a bone whose opposite is not in the skeleton keeps its index. Of the vanilla bones this pairs, 94% have
        // their partner at the mirrored position; the rest are bind poses that are not symmetric, such as flags.
        public static Dictionary<int, int> LeftRightBoneSwap(IReadOnlyDictionary<int, string> boneNames)
        {
            var byName = boneNames.GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.First().Key);
            var swap = new Dictionary<int, int>();
            foreach (var (index, name) in boneNames)
            {
                foreach (var candidate in new[] { SwapWords(name), SwapGlued(name) })
                {
                    if (candidate != name && byName.TryGetValue(candidate, out var other))
                    {
                        swap[index] = other;
                        break;
                    }
                }
            }
            return swap;
        }

        static string SwapWords(string name) => Regex.Replace(name, "(?<=^|[^a-z])(left|right|l|r|lft|rgt)(?=$|[^a-z])", x => x.Value switch
        {
            "left" => "right",
            "right" => "left",
            "l" => "r",
            "r" => "l",
            "lft" => "rgt",
            _ => "lft",
        });

        static string SwapGlued(string name) => Regex.Replace(name, "left|right", x => x.Value == "left" ? "right" : "left");
    }
}
