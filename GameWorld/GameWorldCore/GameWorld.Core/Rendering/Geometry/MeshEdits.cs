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
