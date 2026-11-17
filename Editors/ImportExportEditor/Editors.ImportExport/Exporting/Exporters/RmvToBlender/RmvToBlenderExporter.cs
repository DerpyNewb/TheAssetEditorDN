using System.IO;
using Editors.ImportExport.Blender;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.RigidModel;

namespace Editors.ImportExport.Exporting.Exporters.RmvToBlender
{
    /// <summary>
    /// rmv2 -> .blend through Blender and io_scene_rmv2: every LOD, the skeleton attached when the
    /// model names one, and the textures written next to the .blend so it opens with them.
    /// </summary>
    public class RmvToBlenderExporter
    {
        readonly IPackFileService _packFileService;
        readonly BlenderRunner _runner;

        public RmvToBlenderExporter(IPackFileService packFileService, BlenderRunner runner)
        {
            _packFileService = packFileService;
            _runner = runner;
        }

        public BlenderResult Export(PackFile rmv2, string outputBlendPath)
        {
            var outputFolder = Path.GetDirectoryName(Path.GetFullPath(outputBlendPath))!;
            var textureRoot = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(outputBlendPath) + "_textures");
            return Run("rmv2_to_blend", rmv2, outputBlendPath, textureRoot);
        }

        BlenderResult Run(string mode, PackFile rmv2, string outputPath, string textureRoot)
        {
            var warnings = new List<string>();
            var jobFolder = BlenderRunner.NewJobFolder(Path.GetFileNameWithoutExtension(rmv2.Name));
            var bytes = rmv2.DataSource.ReadData();
            var modelPath = Path.Combine(jobFolder, rmv2.Name);
            File.WriteAllBytes(modelPath, bytes);

            var skeletonName = ModelFactory.Create().LoadOnlyHeaders(bytes).Header.SkeletonName;
            string? skeletonAnim = null;
            if (!string.IsNullOrWhiteSpace(skeletonName))
            {
                var skeletonPath = $@"animations\skeletons\{skeletonName}.anim";
                var skeleton = _packFileService.FindFile(skeletonPath);
                if (skeleton == null)
                {
                    warnings.Add($"The skeleton {skeletonPath} is not in any loaded pack, so the model is exported without its rig.");
                }
                else
                {
                    skeletonAnim = Path.Combine(jobFolder, skeletonName + ".anim");
                    File.WriteAllBytes(skeletonAnim, skeleton.DataSource.ReadData());
                }
            }

            WriteTextures(rmv2, textureRoot, warnings);

            var job = new BlenderJob(mode, modelPath, Path.GetFullPath(outputPath), skeletonAnim, skeletonName,
                textureRoot, _packFileService.GetFullPath(rmv2), (int)ModelFactory.Create().LoadOnlyHeaders(bytes).Header.Version);
            var result = _runner.Run(job, jobFolder);
            return result with { Warnings = [.. warnings, .. result.Warnings] };
        }

        void WriteTextures(PackFile rmv2, string textureRoot, List<string> warnings)
        {
            ResolvedModelAsset asset;
            try
            {
                asset = new ModelAssetResolver(_packFileService).Resolve(rmv2);
            }
            catch (Exception e)
            {
                warnings.Add($"Could not read the model's materials, so no textures were exported: {e.Message}");
                return;
            }

            var paths = asset.MaterialsByLod.SelectMany(lod => lod).SelectMany(m => m.Textures.Values)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var texture = _packFileService.FindFile(path);
                if (texture == null)
                {
                    warnings.Add($"Texture not found in any loaded pack: {path}");
                    continue;
                }
                var target = Path.Combine(textureRoot, path.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, texture.DataSource.ReadData());
            }
        }
    }
}
