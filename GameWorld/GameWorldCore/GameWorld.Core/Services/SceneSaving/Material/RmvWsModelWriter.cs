using System.IO;
using System.Text;
using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.Rendering.Materials.Shaders;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Services.SceneSaving.Material
{
    /// <summary>
    /// The .wsmodel and its .material files for a model that has none: what Kitbash writes on save, built from the rmv2 alone,
    /// so an imported mesh gets the game's shaders (faction colours, metal and roughness) without a trip through Kitbash.
    /// Paths are lower case with forward slashes, as CA's are.
    /// </summary>
    public static class RmvWsModelWriter
    {
        /// <summary>The material each game's imported meshes get. A game missing here has no wsmodel template.</summary>
        static CapabilityMaterialsEnum? MaterialFor(GameTypeEnum game) => game switch
        {
            GameTypeEnum.Warhammer3 or GameTypeEnum.ThreeKingdoms => CapabilityMaterialsEnum.MetalRoughPbr_Default,
            GameTypeEnum.Warhammer2 or GameTypeEnum.Troy or GameTypeEnum.Pharaoh => CapabilityMaterialsEnum.SpecGlossPbr_Default,
            _ => null,
        };

        public static bool Supports(GameTypeEnum game) => MaterialFor(game) != null;

        /// <returns>Each file to add, by pack path: the .wsmodel beside the model, its materials in "materials/" beside it.</returns>
        public static List<(string PackPath, byte[] Bytes)> Create(RmvFile rmv, string rmvPackPath, GameTypeEnum game)
        {
            var type = MaterialFor(game) ?? throw new Exception($"No wsmodel template for {game}");
            var modelPath = Normalise(rmvPackPath);
            // Material files are named after the model as well as the mesh, so two models imported into one folder never write
            // different materials to one path.
            var stem = Path.GetFileNameWithoutExtension(modelPath);

            var meshes = new List<WsModelGeneratorInput>();
            for (var lod = 0; lod < rmv.ModelList.Length; lod++)
            {
                for (var part = 0; part < rmv.ModelList[lod].Length; part++)
                {
                    var rmvMaterial = rmv.ModelList[lod][part].Material;
                    var material = Create(type);
                    material.Initialize(null, rmvMaterial);
                    var mesh = string.IsNullOrWhiteSpace(rmvMaterial.ModelName) ? $"mesh_{part}" : rmvMaterial.ModelName.Trim().Replace(' ', '_').ToLowerInvariant();
                    var name = mesh.StartsWith(stem) ? mesh : $"{stem}_{mesh}";
                    meshes.Add(new WsModelGeneratorInput(lod, part, name, ModelMaterialEnumHelper.GetToolVertexFormat(rmvMaterial.BinaryVertexFormat), material));
                }
            }

            var materials = new InMemoryMaterialSerializer(game);
            var wsModel = WsModelGeneratorService.CreateWsModel(materials, modelPath, meshes);

            var output = new List<(string, byte[])> { (Path.ChangeExtension(modelPath, ".wsmodel"), Encoding.UTF8.GetBytes(wsModel)) };
            output.AddRange(materials.Files.Select(x => (x.Key, Encoding.UTF8.GetBytes(x.Value))));
            return output;
        }

        static CapabilityMaterial Create(CapabilityMaterialsEnum type) => type switch
        {
            // No resource library: these materials are only serialised, never drawn, and the library is read only to draw.
            CapabilityMaterialsEnum.MetalRoughPbr_Default => new Rendering.Materials.Shaders.MetalRough.DefaultMaterial(null!),
            CapabilityMaterialsEnum.SpecGlossPbr_Default => new Rendering.Materials.Shaders.SpecGloss.DefaultMaterial(null!),
            _ => throw new Exception($"{type} is not a material an imported mesh gets"),
        };

        static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        /// <summary>Kitbash's MaterialToWsMaterialSerializer without the save: one file per distinct material.</summary>
        class InMemoryMaterialSerializer(GameTypeEnum game) : IMaterialToWsMaterialSerializer
        {
            public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

            public string ProsessMaterial(string modelFilePath, string meshName, UiVertexFormat meshVertexFormat, CapabilityMaterial material)
            {
                var template = new WsMaterialTemplateEditor(material, game);
                var fileName = template.AddTemplateHeader(meshName, meshVertexFormat, material);
                foreach (var capability in material.Capabilities)
                    capability.SerializeToWsModel(template);
                var content = template.GetCompletedMaterialString();

                var existing = Files.FirstOrDefault(x => x.Value == content).Key;
                if (existing != null)
                    return existing;

                var path = Normalise($"{Path.GetDirectoryName(modelFilePath)}/materials/{fileName}");
                Files[path] = content;
                return path;
            }
        }
    }
}
