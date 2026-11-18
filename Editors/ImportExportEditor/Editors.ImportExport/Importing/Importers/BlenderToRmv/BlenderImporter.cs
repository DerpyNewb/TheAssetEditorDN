using System.IO;
using Editors.ImportExport.Blender;
using GameWorld.Core.Services.SceneSaving.Material;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;

namespace Editors.ImportExport.Importing.Importers.BlenderToRmv
{
    /// <param name="RoundTripPackPath">The first file's ae_pack_path: where the .blend was exported from, if AssetEditor exported it.</param>
    public record BlenderImportOutcome(BlenderResult Result, IReadOnlyList<string> AddedPackPaths, string? RoundTripPackPath);

    /// <summary>.blend / .obj -> rmv2 through Blender and io_scene_rmv2, one file per RMV2 root collection.</summary>
    public class BlenderImporter
    {
        readonly IPackFileService _packFileService;
        readonly BlenderRunner _runner;

        public BlenderImporter(IPackFileService packFileService, BlenderRunner runner)
        {
            _packFileService = packFileService;
            _runner = runner;
        }

        /// <param name="version">RMV2 version to write; 0 keeps each model's own.</param>
        /// <param name="replaceOriginal">Asked once per model AssetEditor exported, with the pack path it came from;
        /// true writes it back there instead of into <paramref name="packFolder"/>.</param>
        /// <param name="wsModelGame">Writes a .wsmodel and its materials for this game beside each model that has none in any loaded
        /// pack; null writes none. A model that already has one, as a round trip to CA's path does, keeps it.</param>
        public BlenderImportOutcome Import(string diskFile, IPackFileContainer container, string packFolder, string? skeletonName, int version, bool autoLods,
            Func<string, bool>? replaceOriginal = null, GameTypeEnum? wsModelGame = null)
        {
            var jobFolder = BlenderRunner.NewJobFolder(Path.GetFileNameWithoutExtension(diskFile));
            var job = new BlenderJob("to_rmv2", Path.GetFullPath(diskFile), Path.Combine(jobFolder, "out"),
                SkeletonName: skeletonName, Version: version, AutoLods: autoLods);
            var result = _runner.Run(job, jobFolder);
            if (!result.Ok)
                return new(BlenderRunner.Finish(result, jobFolder), [], null);

            // Parse everything before adding anything, so a bad file never reaches the pack.
            var files = new List<(string Folder, string Name, byte[] Bytes)>();
            var models = new List<RmvFile>();
            foreach (var output in result.Files)
            {
                var bytes = File.ReadAllBytes(output.Path);
                try
                {
                    models.Add(ModelFactory.Create().Load(bytes));
                }
                catch (Exception e)
                {
                    return new(BlenderRunner.Finish(result with { Ok = false, Error = $"{output.Name} is not a valid rigid_model_v2: {e.Message}" }, jobFolder), [], null);
                }
                if (!string.IsNullOrEmpty(output.PackPath) && replaceOriginal != null && replaceOriginal(output.PackPath))
                    files.Add((Path.GetDirectoryName(output.PackPath)!.ToLowerInvariant(), Path.GetFileName(output.PackPath).ToLowerInvariant(), bytes));
                else
                    files.Add((packFolder, output.Name.ToLowerInvariant(), bytes));
            }

            BlenderRunner.Finish(result, jobFolder); // the bytes are read; the outputs are no longer needed
            if (wsModelGame is { } game && RmvWsModelWriter.Supports(game))
            {
                for (var i = 0; i < models.Count; i++)
                {
                    var modelPath = string.IsNullOrEmpty(files[i].Folder) ? files[i].Name : $"{files[i].Folder}/{files[i].Name}";
                    if (_packFileService.FindFile(Path.ChangeExtension(modelPath, ".wsmodel")) != null)
                        continue;
                    foreach (var (packPath, bytes) in RmvWsModelWriter.Create(models[i], modelPath, game))
                        files.Add((Path.GetDirectoryName(packPath)!, Path.GetFileName(packPath), bytes));
                }
            }
            var entries = files.Select(f => new NewPackFileEntry(f.Folder, PackFile.CreateFromBytes(f.Name, f.Bytes))).ToList();
            _packFileService.AddFilesToPack(container, entries);
            var added = files.Select(f => string.IsNullOrEmpty(f.Folder) ? f.Name : $@"{f.Folder}\{f.Name}").ToList();
            return new(result, added, result.Files.Select(f => f.PackPath).FirstOrDefault(p => !string.IsNullOrEmpty(p)));
        }
    }
}
