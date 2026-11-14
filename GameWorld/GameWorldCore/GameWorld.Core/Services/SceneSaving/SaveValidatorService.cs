using System.Linq;
using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Utility;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;

namespace GameWorld.Core.Services.SceneSaving
{
    /// <summary>What is wrong with a model before it is saved. Kitbash asks whether to save anyway when this finds anything.</summary>
    public class SaveValidatorService
    {
        // CA's 56.7 million four-weight vertices in WH3 miss 1 by 0.0118 at most, three 255ths of byte rounding.
        const float WeightTolerance = 0.02f;

        private readonly IPackFileService _packFileService;

        public SaveValidatorService(IPackFileService packFileService)
        {
            _packFileService = packFileService;
        }

        public ErrorList Validate(MainEditableNode mainNode, GeometrySaveSettings settings)
        {
            var errorList = new ErrorList();

            var skeleton = mainNode.SkeletonNode.Skeleton;
            var meshes = mainNode.GetMeshesInLod(0, settings.OnlySaveVisible);
            if (meshes.Count == 0)
                return errorList;

            // Different skeletons
            if (skeleton != null)
            {
                var activeSkeletonName = skeleton.SkeletonName;
                var skeletonNames = meshes.Select(x => x.Geometry.SkeletonName).Distinct().ToList();

                if (skeletonNames.Count != 1)
                    errorList.Error("Skeleton", "Model contains meshes with multiple skeleton references. They will not animate well in game.");

                skeletonNames.Remove(activeSkeletonName);
                if (skeletonNames.Count != 0)
                    errorList.Error("Skeleton", "Model contains meshes that have not been re-rigged. They will not behave well in game.");
            }

            // Mismatch between static and animated vertex
            var vertexTypes = meshes.Select(x => x.Geometry.VertexFormat).Distinct().ToList();
            if (vertexTypes.Contains(UiVertexFormat.Static) && skeleton != null)
                errorList.Error("Vertex", "Model has a skeleton, but contains meshes with non-animated vertices. Rig them or they will not behave as expected in game.");

            if ((vertexTypes.Contains(UiVertexFormat.Weighted) || vertexTypes.Contains(UiVertexFormat.Cinematic)) && skeleton == null)
                errorList.Error("Vertex", "Model does not have a skeleton, but has animated vertex data.");

            // Large model count
            if (meshes.Count > 50)
                errorList.Warning("Mesh Count", "Model contains a large number of meshes, which might cause performance issues.");

            if (ModelCombiner.HasPotentialCombineMeshes(meshes, out _))
                errorList.Warning("Mesh", "Model contains multiple meshes that can be merged. Consider merging them for performance reasons.");

            // Different pivots
            var pivots = meshes.Select(x => x.PivotPoint).Distinct().ToList();
            if (pivots.Count > 1)
                errorList.Warning("Pivot Point", "Model contains several different pivot points. This is almost never intended.");

            // Animation and Pivotpoint
            if (pivots.Count == 1 && skeleton != null)
            {
                if ((pivots.First().X == 0 && pivots.First().Y == 0 && pivots.First().Z == 0) == false)
                    errorList.Warning("Pivot Point", "Model has a skeleton and a non-zero pivot point. This is almost never intended.");
            }

            // Every LOD past 0 is rebuilt from LOD 0 on save, unless the dialog says to leave the LODs as they are.
            var lodCount = settings.LodGenerationMethod == LodStrategy.NoLodGeneration ? mainNode.GetLodNodes().Count : 1;
            var saved = Enumerable.Range(0, lodCount)
                .SelectMany(lod => mainNode.GetMeshesInLod(lod, settings.OnlySaveVisible).Select(mesh => (Mesh: mesh, Name: lod == 0 ? mesh.Name : $"{mesh.Name} (LOD {lod})")))
                .ToList();

            foreach (var (mesh, name) in saved)
                CheckWeights(mesh, name, errorList);
            CheckTextures(saved, errorList);

            return errorList;
        }

        static void CheckWeights(Rmv2MeshNode mesh, string name, ErrorList errorList)
        {
            var weightCount = mesh.Geometry.WeightCount;
            if (weightCount == 0)
                return;

            int unweighted = 0, unbalanced = 0;
            foreach (var vertex in mesh.Geometry.VertexArray)
            {
                var weights = vertex.BlendWeights;
                var total = vertex.GetBoneWeights().Take(weightCount).Sum();
                if (total <= 0)
                    unweighted++;
                // CA stores 12.3 million of its 20.9 million two-weight vertices with only the first weight set, the second being
                // what the first leaves, so only a two-weight vertex with both set can be off.
                else if ((weightCount == 4 || weights.Y > 0) && MathF.Abs(total - 1) > WeightTolerance)
                    unbalanced++;
            }

            // MeshBuilderService.NormalizeBoneWeights runs on save: it scales the weights to 1, and puts a vertex with none fully on its first bone.
            var vertexCount = mesh.Geometry.VertexArray.Length;
            if (unweighted != 0)
                errorList.Error(name, $"{unweighted} of {vertexCount} vertices have no bone weights. The save binds each one fully to its first bone.");
            if (unbalanced != 0)
                errorList.Warning(name, $"{unbalanced} of {vertexCount} vertices have bone weights that don't add up to 1. The save scales them to 1, so they may move differently in game than here.");
        }

        void CheckTextures(List<(Rmv2MeshNode Mesh, string Name)> meshes, ErrorList errorList)
        {
            // The textures the save writes into the model, read the way the save reads them.
            var serializer = new MaterialToRmvSerializer();
            var uses = meshes.SelectMany(x => serializer.CreateMaterialFromCapabilityMaterial(x.Mesh.Material).GetAllTextures().Select(texture => (texture.TexureType, texture.Path, x.Name)));

            foreach (var path in uses.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                var users = string.Join(", ", path.Select(x => x.Name).Distinct());
                var file = _packFileService.FindFile(path.Key);
                if (file == null)
                    errorList.Warning(path.Key, $"This texture is not in any loaded pack. Used by {users}.");
                else if (path.Any(x => x.TexureType == TextureType.BaseColour) && ImageLoader.ReadImageInformation(file.DataSource.ReadData()) is { IsDdsImage: true, HasSrgbFlag: false } info)
                    errorList.Warning(path.Key, $"This base colour is {info.FormatName}, which has no sRGB flag. The game reads it as linear and draws it lighter. Save it as BC1_UNORM_SRGB, BC3_UNORM_SRGB or BC7_UNORM_SRGB. Used by {users}.");
            }
        }
    }
}
