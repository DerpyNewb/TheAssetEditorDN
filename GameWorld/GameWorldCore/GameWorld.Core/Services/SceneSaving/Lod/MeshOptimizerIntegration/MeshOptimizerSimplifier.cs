using System.Runtime.InteropServices;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;

namespace GameWorld.Core.Services.SceneSaving.Lod.MeshOptimizerIntegration
{
    // meshoptimizer 1.3, built from source as Native/meshoptimizer.dll (see Native/README.md).
    // The output only references original vertices, so skin weights, UVs and tangent frames come through untouched.
    public static class MeshOptimizerSimplifier
    {
        const string dllFileName = "meshoptimizer.dll";
        const uint SimplifyLockBorder = 1 << 0;
        const float NormalWeight = 0.5f;

        // 5% of the mesh size. Without a cap, a mesh whose open edges stop it shrinking eats whole closed parts (an orc's legs) to reach the count.
        const float MaxError = 0.05f;

        [DllImport(dllFileName, CallingConvention = CallingConvention.Cdecl)]
        static extern nuint meshopt_simplifyWithAttributes(uint[] destination, uint[] indices, nuint indexCount,
            float[] vertexPositions, nuint vertexCount, nuint vertexPositionsStride,
            float[] vertexAttributes, nuint vertexAttributesStride, float[] attributeWeights, nuint attributeCount,
            byte[]? vertexLock, nuint targetIndexCount, float targetError, uint options, out float resultError);

        public static MeshObject GetReducedMeshCopy(MeshObject original, float factor)
        {
            var vertexCount = original.VertexArray.Length;
            var positions = new float[vertexCount * 3];
            var attributes = new float[vertexCount * 5];
            for (var i = 0; i < vertexCount; i++)
            {
                var vertex = original.VertexArray[i];
                positions[i * 3 + 0] = vertex.Position.X;
                positions[i * 3 + 1] = vertex.Position.Y;
                positions[i * 3 + 2] = vertex.Position.Z;
                attributes[i * 5 + 0] = vertex.Normal.X;
                attributes[i * 5 + 1] = vertex.Normal.Y;
                attributes[i * 5 + 2] = vertex.Normal.Z;
                attributes[i * 5 + 3] = vertex.TextureCoordinate.X;
                attributes[i * 5 + 4] = vertex.TextureCoordinate.Y;
            }

            var uvWeight = UvWeight(original);
            float[] weights = [NormalWeight, NormalWeight, NormalWeight, uvWeight, uvWeight];

            var indices = original.IndexArray.Select(x => (uint)x).ToArray();
            var targetIndexCount = (int)(indices.Length / 3 * factor) * 3;
            var destination = new uint[indices.Length];

            var resultCount = (int)meshopt_simplifyWithAttributes(destination, indices, (nuint)indices.Length,
                positions, (nuint)vertexCount, sizeof(float) * 3,
                attributes, sizeof(float) * 5, weights, (nuint)weights.Length,
                null, (nuint)targetIndexCount, MaxError, SimplifyLockBorder, out _);

            var remap = new int[vertexCount];
            Array.Fill(remap, -1);
            var vertices = new List<VertexPositionNormalTextureCustom>();
            var newIndices = new ushort[resultCount];
            for (var i = 0; i < resultCount; i++)
            {
                var oldIndex = destination[i];
                if (remap[oldIndex] == -1)
                {
                    remap[oldIndex] = vertices.Count;
                    vertices.Add(original.VertexArray[oldIndex]);
                }
                newIndices[i] = (ushort)remap[oldIndex];
            }

            var clone = original.Clone(false);
            clone.IndexArray = newIndices;
            clone.VertexArray = vertices.ToArray();
            clone.RebuildIndexBuffer();
            clone.RebuildVertexBuffer();
            return clone;
        }

        // The reciprocal of the average UV triangle size, which is how the meshoptimizer README suggests weighting UVs
        static float UvWeight(MeshObject mesh)
        {
            var triangleCount = mesh.IndexArray.Length / 3;
            var area = 0.0;
            for (var i = 0; i < mesh.IndexArray.Length; i += 3)
            {
                var a = mesh.VertexArray[mesh.IndexArray[i]].TextureCoordinate;
                var b = mesh.VertexArray[mesh.IndexArray[i + 1]].TextureCoordinate;
                var c = mesh.VertexArray[mesh.IndexArray[i + 2]].TextureCoordinate;
                area += Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            }

            return area > 0 ? (float)(1 / Math.Sqrt(area / triangleCount)) : 0;
        }
    }
}
