using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel;
using SharpGLTF.Schema2;
using Shared.GameFormats.RigidModel.Vertex;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Geometry;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using XNA = Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel.Types;
using System.Windows.Controls;
using System.CodeDom;
using System.Windows.Forms.VisualStyles;
using System.Drawing.Printing;
using System;
using System.Text;
using Editors.ImportExport.Common;
using Shared.GameFormats.Animation;
using static Shared.GameFormats.Animation.AnimationFile;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper
{
    /// <summary>
    /// Builds RmvMesh from Gltf asset (input: modelRoot)
    /// </summary>
    public class RmvMeshBuilder
    {
        /// <summary>One rmv2 part per glTF primitive, so a mesh with two materials keeps both; a primitive over RMV2's 65,536
        /// vertices is split into several parts. <paramref name="partSources"/>, when given, receives each part's
        /// (mesh, primitive) so its material can be found.</summary>
        public static RmvFile? Build(GltfImporterSettings settings, ModelRoot modelRoot, AnimationFile? animSkeletonFile, string SkeletonName,
            List<(int Mesh, int Primitive)>? partSources = null)
        {
            if (modelRoot == null)
                throw new ArgumentNullException(nameof(modelRoot), "Invalid Scene: ModelRoot can't be null");

            if (modelRoot.LogicalNodes == null)
                throw new ArgumentNullException(nameof(modelRoot), "Invalid Scene: root.LogicalNodes can't be null");

            if (!modelRoot.LogicalNodes.Any())
                throw new Exception("Invalid Scene: no (logical) nodes");

            if (modelRoot.LogicalMeshes == null || !modelRoot.LogicalMeshes.Any())
            {
                return null; // no meshes to import
            }

            List<string> boneNames = new List<string>();
            var test = boneNames?.Count;

            const int lodCount = 1; // All meshes go in LOD 0, LODs are sorted later

            var rmv2File = new RmvFile()
            {
                Header = new RmvFileHeader()
                {
                    _fileType = Encoding.ASCII.GetBytes("RMV2"),
                    SkeletonName = SkeletonName,
                    Version = RmvVersionEnum.RMV2_V7,
                    LodCount = lodCount
                },
                ModelList = new RmvModel[lodCount][],
            };

            rmv2File.LodHeaders = new RmvLodHeader[1];
            rmv2File.LodHeaders[0] = LodHeaderFactory.Create().CreateEmpty(RmvVersionEnum.RMV2_V7, 100.0f, 0, 0);
            var modelList = new List<RmvModel>();
            for (var meshIndex = 0; meshIndex < modelRoot.LogicalMeshes.Count; meshIndex++)
            {
                var mesh = modelRoot.LogicalMeshes[meshIndex];
                // We use NODE name as mesh name, because users typically name the NODE in Blender/Maya/3dsMax, not the mesh
                var meshNodeName =
                    (mesh.VisualParents != null && mesh.VisualParents.Any()) ?
                    mesh.VisualParents.First().Name : mesh.Name;

                if (mesh.Primitives == null || !mesh.Primitives.Any())
                    throw new Exception($"Invalid Mesh: No Primitives found in mesh. Primitives.Count = {mesh.Primitives?.Count}");

                for (var primitiveIndex = 0; primitiveIndex < mesh.Primitives.Count; primitiveIndex++)
                {
                    var parts = SplitAtVertexLimit(GenerateRmvMesh(mesh.Primitives[primitiveIndex], modelRoot, animSkeletonFile));
                    for (var part = 0; part < parts.Count; part++)
                    {
                        var name = primitiveIndex == 0 && part == 0 ? meshNodeName
                            : parts.Count == 1 ? $"{meshNodeName}_{primitiveIndex}" : $"{meshNodeName}_{primitiveIndex}_{part}";
                        modelList.Add(CreateRmvModel(parts[part], name, animSkeletonFile));
                        partSources?.Add((meshIndex, primitiveIndex));
                    }
                }
            }

            rmv2File.LodHeaders[0].MeshCount = (uint)modelList.Count;
            rmv2File.ModelList[0] = modelList.ToArray();
            rmv2File.RecalculateOffsets();            

            return rmv2File;
        }

        // The whole primitive as one list of vertices and triangles, before the RMV2 limit is applied: indices are int here.
        record FullMesh(CommonVertex[] Vertices, int[] Indices);

        private static FullMesh GenerateRmvMesh(MeshPrimitive primitive, ModelRoot modelRoot, AnimationFile? animSkeletonFile)
        {
            if (primitive == null)
                throw new Exception("Invalid Mesh: primitive[0] can't be null ");

            var vertexBufferColumns = primitive.GetVertexColumns();

            if (vertexBufferColumns == null)
                throw new ArgumentNullException(nameof(vertexBufferColumns), "Invalid Mesh: value cannot be null ");

            if (vertexBufferColumns.Positions == null || !vertexBufferColumns.Positions.Any())
                throw new Exception($"Invalid Mesh: No vertex data. Positions.Count = {vertexBufferColumns.Positions?.Count}");

            var vertices = new CommonVertex[vertexBufferColumns.Positions.Count()];
            for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                var vertexBuilder = vertexBufferColumns.GetVertex<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>(vertexIndex);
                vertices[vertexIndex] = ConvertToRmvVertex(vertexBuilder, modelRoot, animSkeletonFile);
            }

            var indices = primitive.GetIndices();
            var indexList = new int[indices.Count()];
            for (int i = 0; i < indices.Count(); i += 3) // reverse wind order, as we "mirrored" t
            {
                indexList[i + 0] = (int)indices[i + 0];
                indexList[i + 2] = (int)indices[i + 1];
                indexList[i + 1] = (int)indices[i + 2];
            }

            return new FullMesh(vertices, indexList);
        }

        /// <summary>RMV2 indexes vertices with 16 bits, so one part holds at most 65,536. A larger mesh is cut into parts by
        /// whole triangles, in order, each part taking the vertices its triangles use.</summary>
        internal static List<RmvMesh> SplitAtVertexLimit(CommonVertex[] vertices, int[] indices, int limit = ushort.MaxValue + 1)
        {
            var parts = new List<RmvMesh>();
            var remap = new Dictionary<int, ushort>();
            var partVertices = new List<CommonVertex>();
            var partIndices = new List<ushort>();

            void Flush()
            {
                var mesh = new RmvMesh { VertexList = partVertices.ToArray(), IndexList = partIndices.ToArray() };
                TangentBasisCalculator.CalculateForRmv2Mesh(mesh);
                parts.Add(mesh);
                remap.Clear();
                partVertices = [];
                partIndices = [];
            }

            for (var t = 0; t < indices.Length; t += 3)
            {
                var added = 0;
                for (var k = 0; k < 3; k++)
                    added += remap.ContainsKey(indices[t + k]) || indices.Skip(t).Take(k).Contains(indices[t + k]) ? 0 : 1;
                if (partVertices.Count + added > limit)
                    Flush();
                for (var k = 0; k < 3; k++)
                {
                    if (!remap.TryGetValue(indices[t + k], out var local))
                    {
                        local = (ushort)partVertices.Count;
                        remap[indices[t + k]] = local;
                        partVertices.Add(vertices[indices[t + k]]);
                    }
                    partIndices.Add(local);
                }
            }
            if (partIndices.Count != 0 || parts.Count == 0)
                Flush();
            return parts;
        }

        static List<RmvMesh> SplitAtVertexLimit(FullMesh mesh)
        {
            // A mesh within the limit keeps every vertex, unused ones included, exactly as before.
            if (mesh.Vertices.Length <= ushort.MaxValue + 1)
            {
                var whole = new RmvMesh { VertexList = mesh.Vertices, IndexList = mesh.Indices.Select(i => (ushort)i).ToArray() };
                TangentBasisCalculator.CalculateForRmv2Mesh(whole);
                return [whole];
            }
            return SplitAtVertexLimit(mesh.Vertices, mesh.Indices);
        }

        private static CommonVertex ConvertToRmvVertex(VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> vertexBuilder, ModelRoot modelRoot, AnimationFile animSkeletonFile)
        {
            var rmv2Vertex = new CommonVertex();

            rmv2Vertex.Position = new XNA.Vector4(-vertexBuilder.Geometry.Position.X, vertexBuilder.Geometry.Position.Y, vertexBuilder.Geometry.Position.Z, 1);
            rmv2Vertex.Uv = VecConv.GetXna(vertexBuilder.Material.TexCoord);
            rmv2Vertex.Normal = new XNA.Vector3(-vertexBuilder.Geometry.Normal.X, vertexBuilder.Geometry.Normal.Y, vertexBuilder.Geometry.Normal.Z);
            rmv2Vertex.Tangent = new XNA.Vector3(-vertexBuilder.Geometry.Tangent.X, vertexBuilder.Geometry.Tangent.Y, vertexBuilder.Geometry.Tangent.Z);
            rmv2Vertex.BiNormal = XNA.Vector3.Cross(rmv2Vertex.Normal, rmv2Vertex.Tangent) * vertexBuilder.Geometry.Tangent.W; // should produce th correct bitangent

            rmv2Vertex.WeightCount = (animSkeletonFile == null) ? 0 : 4;
            
            rmv2Vertex.BoneIndex = new byte[rmv2Vertex.WeightCount];
            rmv2Vertex.BoneWeight = new float[rmv2Vertex.WeightCount];
                        
            for (var bindingIndex = 0; bindingIndex < rmv2Vertex.WeightCount; bindingIndex++)
            {
                var boneTableIndex = GetMappedBoneTableIndex(vertexBuilder, modelRoot, animSkeletonFile, bindingIndex);

                rmv2Vertex.BoneIndex[bindingIndex] = (byte)boneTableIndex;
                rmv2Vertex.BoneWeight[bindingIndex] = vertexBuilder.Skinning.Weights[bindingIndex];
            }

            return rmv2Vertex;
        }

        /// <summary>
        /// Maps the gltf vertex joint index to th CA AnimationFile bone index
        /// </summary>       
        private static int GetMappedBoneTableIndex(VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> vertexBuilder, ModelRoot modelRoot, AnimationFile animSkeletonFile, int bindingIndex)
        {
            var binding = vertexBuilder.Skinning.GetBinding(bindingIndex); // Get binding

            if (modelRoot.LogicalSkins == null || !modelRoot.LogicalSkins.Any())
                throw new Exception($"No skins in scene. modelRoot.LogicalSkins = {modelRoot?.LogicalSkins}");

            var joint = modelRoot.LogicalSkins[0].GetJoint(binding.Index); // get skin joint node

            // use bone name, to obtain index to AnimationFile bone index
            var boneTableIndex = Array.FindIndex<BoneInfo>(animSkeletonFile.Bones, x => x.Name == joint.Joint.Name);
            return boneTableIndex;
        }

        private static RmvModel CreateRmvModel(RmvMesh rmv2Mesh, string modelName, AnimationFile? animSkeletonFile,  bool addBonesAsAttachmentPoints = false)
        {
            var materialHeader = new WeightedMaterial();
                        
            if (animSkeletonFile != null)
            {
                materialHeader.MaterialId = ModelMaterialEnum.weighted;
                materialHeader.BinaryVertexFormat = VertexFormat.Cinematic;
                MeshWeightValidator.Validate(rmv2Mesh);
            }
            else
            {
                materialHeader.MaterialId = ModelMaterialEnum.default_type;
                materialHeader.BinaryVertexFormat = VertexFormat.Static;                
            }

            var newModel = new RmvModel()
            {
                CommonHeader = RmvCommonHeader.CreateDefault(),
                Material = materialHeader,
                Mesh = rmv2Mesh
            };

            newModel.Material.ModelName = modelName;

            CalculateBoundBox(newModel);

            if (addBonesAsAttachmentPoints && animSkeletonFile != null)
            {
                var boneNames = animSkeletonFile.Bones.Select(x => x.Name).ToList();
                var attachmentPoints = AttachmentPointHelper.CreateFromBoneList(boneNames);
                newModel.Material.EnrichDataBeforeSaving(attachmentPoints, -1);
            }

            return newModel;
        }

        private static void CalculateBoundBox(RmvModel newModel)
        {
            var points = new XNA.Vector3[newModel.Mesh.VertexList.Length];

            for (var i = 0; i < newModel.Mesh.VertexList.Length; i++)
            {
                points[i].X = newModel.Mesh.VertexList[i].Position.X;
                points[i].Y = newModel.Mesh.VertexList[i].Position.Y;
                points[i].Z = newModel.Mesh.VertexList[i].Position.Z;
            }

            var testPoints = newModel.Mesh.VertexList.Select(item => item.Position).ToList();

            newModel.UpdateBoundingBox(XNA.BoundingBox.CreateFromPoints(points));
        }
    }
}
