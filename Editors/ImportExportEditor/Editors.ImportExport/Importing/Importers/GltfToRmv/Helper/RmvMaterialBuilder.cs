using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Editors.ImportExport.Importing.Importers.PngToDds;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using SharpDX.DirectWrite;
using SharpGLTF.Schema2;
using TextureType = Shared.GameFormats.RigidModel.Types.TextureType;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Microsoft.VisualBasic;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper
{
    // TODO: MOVE THIS TO A SHARED LOCATION
    public class TextureTypeHelper
    {
        static private readonly Dictionary<string, (TextureType texutureType, string namePostFix)> _stringIdToTextureType = new Dictionary<string, (TextureType, string)>()
            {
                {"BaseColor", (TextureType.BaseColour, "base_colour")},
                {"Normal", (TextureType.Normal, "normal")},
                {"MetallicRoughness", (TextureType.MaterialMap, "material_map")},
                {"Diffuse", (TextureType.Diffuse, "diffuse")},
                {"Specular", (TextureType.Specular, "specular")},
                {"Glossiness", (TextureType.Gloss, "gloss_map") }
            };

        static public bool GetRmvTextureTypeFromGltfIdString(string textureTypeString, out TextureType outTextureType, out string postFix)
        {
            if (_stringIdToTextureType.TryGetValue(textureTypeString, out var textureType))
            {
                outTextureType = textureType.texutureType;
                postFix = textureType.namePostFix; 
                return true;
            }

            outTextureType = TextureType.Diffuse;
            postFix = "diffuse";
            return false;
        }
    }
    public class RmvMaterialBuilder
    {
        private readonly IPackFileService _packFileService;
        private readonly IStandardDialogs _exceptionService;        

        public RmvMaterialBuilder(IPackFileService packFileSerivce, IStandardDialogs exceptionService)
        {
            _packFileService = packFileSerivce;
            _exceptionService = exceptionService;
        }

        /// <param name="partSources">Each rmv2 part's (mesh, primitive), as RmvMeshBuilder made them; null when every mesh is
        /// one part from its first primitive.</param>
        public void BuildRmvFileMaterials(GltfImporterSettings settings, SharpGLTF.Schema2.ModelRoot modelRoot, RmvFile rmvFile, List<(int Mesh, int Primitive)>? partSources = null)
        {
            partSources ??= Enumerable.Range(0, modelRoot.LogicalMeshes.Count).Select(i => (i, 0)).ToList();
            ValidateInput_BuildRmvFileMaterials(modelRoot, rmvFile, partSources.Count);

            for (int i = 0; i < partSources.Count; i++)
            {
                var mesh = modelRoot.LogicalMeshes[partSources[i].Mesh];
                BuildRmvModelMaterial(settings, mesh, partSources[i].Primitive, rmvFile.ModelList[0][i]);
            }

            rmvFile.RecalculateOffsets();
        }

        private void BuildRmvModelMaterial(GltfImporterSettings settings, Mesh mesh, int primitiveIndex, RmvModel rmvModel)
        {
            var primitive = mesh.Primitives[primitiveIndex];
            if (!ValidateInput_BuildRmvModelMaterial(primitive)) return;

            var gltfMaterial = primitive.Material;

            foreach (var itText in gltfMaterial.Channels)
            {
                if (itText.Texture == null) continue;

                if (!TextureTypeHelper.GetRmvTextureTypeFromGltfIdString(
                    itText.Key,
                    out var textureType,
                    out var postFixString)) continue; // gltf string id doesn't match any of the rmv texture types

                var gameType = settings.SelectedGame;                
                
                // A mesh with several primitives has a material per primitive, so each gets its own name. Parts split off one
                // primitive at the vertex limit share its textures, which are added once.
                var textureName = mesh.Primitives.Count > 1 ? $"{mesh.Name}_{primitiveIndex}" : mesh.Name;
                var texturePackFolder = GetTexturePackFolder(settings, textureName, postFixString);
                var DEBUG___textureName = itText.Texture.PrimaryImage.Name;

                // import texture PNG -> DDS. The image's bytes, not its path: a .glb's textures are inside it and have none.
                var convert = textureType switch
                {
                    TextureType.Normal => settings.ConvertNormalTextureFromBlueToOrangeType,
                    TextureType.MaterialMap => settings.ConvertMaterialFromBlenderType,
                    _ => false,
                };
                var image = itText.Texture.PrimaryImage.Content.Content.ToArray();
                var ddsPackFile = PngToDdsImporter.Import(image, textureType, gameType, Path.GetFileName(texturePackFolder), convert);

                rmvModel.Material.SetTexture(textureType, texturePackFolder);

                // make sure we don't add the same file to .pack more the once, as several meshes, may use the same texture, 
                if (!settings.DestinationPackFileContainer.ContainsFile(texturePackFolder))
                {
                    var newFile = new NewPackFileEntry(Path.GetDirectoryName(texturePackFolder) ?? "", ddsPackFile);
                    _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, [newFile]);
                }

            }
        }

        private static string GetTexturePackFolder(GltfImporterSettings settings, string meshName, string postFixString)
        {
            // Lower case with no spaces, as CA's are: since 6.1 an upper-case letter in a pack path crashes the game, and the name
            // used to carry a space before its suffix ("body _base_colour.dds"). Forward slashes, as the rmv2 stores them.
            var textureNameBase = (meshName.Any() ? meshName : Path.GetFileNameWithoutExtension(settings.InputGltfFile)).Trim().Replace(' ', '_').ToLowerInvariant();
            var textureFileName = postFixString.Any() ? $"{textureNameBase}_{postFixString}.dds" : $"{textureNameBase}.dds";

            return $"{settings.DestinationPackPath.Replace('\\', '/').Trim('/').ToLowerInvariant()}/tex/{textureFileName}";
        }

        private static void ValidateInput_BuildRmvFileMaterials(ModelRoot modelRoot, RmvFile rmvFile, int partCount)
        {
            if (modelRoot == null)
                throw new ArgumentNullException(nameof(modelRoot), "Invalid Scene: ModelRoot can't be null");

            if (modelRoot.LogicalNodes == null)
                throw new ArgumentNullException(nameof(modelRoot), "Invalid Scene: root.LogicalNodes can't be null");

            if (!modelRoot.LogicalNodes.Any())
                throw new Exception("Invalid Scene: no (logical) nodes");

            if (!rmvFile.ModelList.Any())
                throw new Exception("ERROR: unexpected not meshes in rmv2 file struct");

            if (rmvFile.ModelList[0].Length != partCount)
                throw new Exception("ERROR: unexpected rmv2 mesh count mismatch");
        }

        private static bool ValidateInput_BuildRmvModelMaterial(MeshPrimitive primitive)
        {
            if (primitive == null)
                throw new Exception("Invalid Mesh: primitive[0] can't be null ");

            var gltfMaterial = primitive.Material;

            if (gltfMaterial == null)
                return false;
            
            if (gltfMaterial.Channels == null || !gltfMaterial.Channels.Any())
                return false;

            return true;
        }
    }
}

