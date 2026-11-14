using GameWorld.Core.Rendering.Materials.Capabilities.Utility;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    /// <summary>
    /// Find and replace in the texture paths of meshes, as one undo step. The shader material's paths are what the 3D view draws
    /// and what the save writes: the .rigid_model_v2 (MaterialToRmvSerializer) and the .wsmodel are both built from them. The
    /// mesh's loaded RMV2 material is changed as well, because Copy textures to pack reads it.
    /// </summary>
    public class ReplaceTexturePathsCommand : IAeUndoCommandCommand
    {
        public string HintText => "Replace in texture paths";
        public bool IsMutation => true;

        readonly List<(TextureInput Input, string Old, string New)> _shaderChanges = [];
        readonly List<(IRmvMaterial Material, TextureType Type, string Old, string New)> _rmvChanges = [];

        /// <summary>The new paths the shader materials will draw and save, once each.</summary>
        public IEnumerable<string> NewPaths => _shaderChanges.Select(x => x.New).Distinct(StringComparer.OrdinalIgnoreCase);

        public static List<(TextureInput Input, string Old, string New)> ShaderChanges(IEnumerable<Rmv2MeshNode> meshes, string find, string replace, bool matchCase) =>
            meshes.SelectMany(x => TextureInputs(x.Material))
                .Distinct()
                .Select(x => (Input: x, Old: x.TexturePath, New: Replaced(x.TexturePath, find, replace, matchCase)))
                .Where(x => x.New != null)
                .Select(x => (x.Input, x.Old, x.New!))
                .ToList();

        public void Configure(IEnumerable<Rmv2MeshNode> meshes, string find, string replace, bool matchCase)
        {
            var meshList = meshes.ToList();
            _shaderChanges.AddRange(ShaderChanges(meshList, find, replace, matchCase));

            foreach (var material in meshList.Select(x => x.RmvMaterial).Distinct())
            {
                foreach (var texture in material.GetAllTextures().ToList())
                {
                    var newPath = Replaced(texture.Path, find, replace, matchCase);
                    if (newPath != null)
                        _rmvChanges.Add((material, texture.TexureType, texture.Path, newPath));
                }
            }
        }

        public void Execute()
        {
            foreach (var change in _shaderChanges)
                change.Input.TexturePath = change.New;
            foreach (var change in _rmvChanges)
                change.Material.SetTexture(change.Type, change.New);
        }

        public void Undo()
        {
            foreach (var change in _shaderChanges)
                change.Input.TexturePath = change.Old;
            foreach (var change in _rmvChanges)
                change.Material.SetTexture(change.Type, change.Old);
        }

        /// <summary>Every texture slot of every capability: base colour, normal, mask, emissive, blood, dirt and the rest.</summary>
        public static IEnumerable<TextureInput> TextureInputs(CapabilityMaterial material) =>
            material.Capabilities.SelectMany(capability => capability.GetType().GetProperties()
                .Where(property => property.PropertyType == typeof(TextureInput))
                .Select(property => property.GetValue(capability))
                .OfType<TextureInput>());

        /// <summary>The path with every match replaced, or null when nothing matches or nothing would change.</summary>
        static string? Replaced(string? path, string find, string replace, bool matchCase)
        {
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (string.IsNullOrEmpty(path) || find.Length == 0 || !path.Contains(find, comparison))
                return null;
            var result = path.Replace(find, replace, comparison);
            return result == path ? null : result;
        }
    }
}
