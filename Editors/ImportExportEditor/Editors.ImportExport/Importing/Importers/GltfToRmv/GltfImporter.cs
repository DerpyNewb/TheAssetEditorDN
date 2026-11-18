using System.IO;
using CommonControls.BaseDialogs.ErrorListDialog;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using GameWorld.Core.Services.SceneSaving.Material;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using SharpGLTF.Schema2;


namespace Editors.ImportExport.Importing.Importers.GltfToRmv
{
    public class GltfImporter
    {
        private const string Value = "//skeleton//";
        private readonly IPackFileService _packFileService;
        private readonly IStandardDialogs _exceptionService;
        private readonly ILogger _logger = Logging.Create<GltfImporter>();
        private readonly ISkeletonAnimationLookUpHelper _skeletonLookUpHelper;
        private readonly RmvMaterialBuilder _materialBuilder;

        public GltfImporter(IPackFileService packFileSerivce, IStandardDialogs exceptionService, ISkeletonAnimationLookUpHelper skeletonLookUpHelper, RmvMaterialBuilder materialBuilder)
        {
            _packFileService = packFileSerivce;
            _exceptionService = exceptionService;
            _skeletonLookUpHelper = skeletonLookUpHelper;
            _materialBuilder = materialBuilder;
        }

        public ImportSupportEnum CanImportFile(PackFile file)
        {
            if (FileExtensionHelper.IsGltfFile(file.Name))
                return ImportSupportEnum.HighPriority;

            return ImportSupportEnum.NotSupported;
        }

        private RmvFile? ImportMeshes(GltfImporterSettings settings, ModelRoot modelRoot, AnimationFile? skeletonAnimFile, string skeletonName)
        {
            var importedFileName = GetImportedPackFileName(settings);

            var rmv2File = RmvMeshBuilder.Build(settings, modelRoot, skeletonAnimFile, skeletonName, _partSources = []);
            if (rmv2File == null)
                return null;

            return rmv2File;
        }

        private void SaveRmvFileToPack(GltfImporterSettings settings, string importedFileName, RmvFile rmv2File)
        {
            var bytesRmv2 = ModelFactory.Create().Save(rmv2File);

            var packFileImported = new PackFile(importedFileName, new MemorySource(bytesRmv2));
            var newFile = new NewPackFileEntry(settings.DestinationPackPath, packFileImported);

            _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, [newFile]);
        }

        // The .wsmodel and its materials, as Kitbash writes them, unless the model already has one in a loaded pack.
        private void SaveWsModelToPack(GltfImporterSettings settings, RmvFile rmv2File)
        {
            if (!RmvWsModelWriter.Supports(settings.SelectedGame))
                return;
            var modelPath = $"{settings.DestinationPackPath}/{GetImportedPackFileName(settings)}";
            if (_packFileService.FindFile(Path.ChangeExtension(modelPath, ".wsmodel")) != null)
                return;

            var entries = RmvWsModelWriter.Create(rmv2File, modelPath, settings.SelectedGame)
                .Select(x => new NewPackFileEntry(Path.GetDirectoryName(x.PackPath)!, PackFile.CreateFromBytes(Path.GetFileName(x.PackPath), x.Bytes)))
                .ToList();
            _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, entries);
        }

        // Each rmv2 part's (mesh, primitive) in the glTF, filled by ImportMeshes.
        private List<(int Mesh, int Primitive)> _partSources = [];

        private void ImportMaterials(GltfImporterSettings settings, ModelRoot modelRoot, RmvFile rmv2File)
        {
            _materialBuilder.BuildRmvFileMaterials(settings, modelRoot, rmv2File, _partSources);
        }

        private void ImportAnimations(GltfImporterSettings settings, ModelRoot modelRoot, AnimationFile? skeletonAnimFile, string skeletonName)
        {
            // One .anim per clip. A single clip keeps the file's own name; several are named after the clips.
            var builderSettings = new AnimationBuilderSettings(modelRoot, skeletonName, settings.AnimationKeysPerSecond, settings.DestinationPackFileContainer, settings.DestinationPackPath);
            var names = AnimationFileNames(Path.GetFileNameWithoutExtension(settings.InputGltfFile), modelRoot.LogicalAnimations.Select(a => a.Name).ToList());
            var entries = new List<NewPackFileEntry>();
            for (var i = 0; i < names.Count; i++)
            {
                var animFile = AnimationBuilder.Build(builderSettings, skeletonAnimFile!, i);
                if (animFile != null)
                    entries.Add(new NewPackFileEntry(settings.DestinationPackPath, new PackFile(names[i], new MemorySource(AnimationFile.ConvertToBytes(animFile)))));
            }
            if (entries.Count != 0)
                _packFileService.AddFilesToPack(settings.DestinationPackFileContainer, entries);
        }

        /// <summary>The .anim file name for each clip: the input's name for one clip, else the input's name and the clip's,
        /// lower case with no spaces, numbered when a clip has no name or two clips share one.</summary>
        internal static List<string> AnimationFileNames(string inputName, IReadOnlyList<string?> clipNames)
        {
            static string Clean(string s) => new string(s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            var stem = Clean(inputName);
            if (clipNames.Count == 1)
                return [$"{stem}.anim"];
            var used = new HashSet<string>();
            var names = new List<string>();
            for (var i = 0; i < clipNames.Count; i++)
            {
                var clip = string.IsNullOrWhiteSpace(clipNames[i]) ? $"{i}" : Clean(clipNames[i]!);
                var name = $"{stem}_{clip}";
                for (var n = 2; !used.Add(name); n++)
                    name = $"{stem}_{clip}_{n}";
                names.Add($"{name}.anim");
            }
            return names;
        }

        public void Import(GltfImporterSettings settings)
        {
            if (!CreateModelRoot(settings, out var modelRoot))
                return;

            var skeletonData = GetSkeletonData(modelRoot);

            var rmv2File = ImportMeshes(settings, modelRoot, skeletonData.skeletonAnimFile, skeletonData.skeletonName ?? "");
            if (rmv2File == null)
                throw new Exception($"Failed to import mesh, rmv2File == {rmv2File}");

            if (settings.ImportMaterials)
                ImportMaterials(settings, modelRoot, rmv2File);

            if (settings.ImportAnimations)
                ImportAnimations(settings, modelRoot, skeletonData.skeletonAnimFile, skeletonData.skeletonName ?? "");

            if (settings.ImportMeshes)
            {
                SaveRmvFileToPack(settings, GetImportedPackFileName(settings), rmv2File);
                SaveWsModelToPack(settings, rmv2File);
            }

        }

        private static bool CreateModelRoot(GltfImporterSettings settings, out ModelRoot? outModelRoot)
        {
            ModelRoot? modelRoot = null;
            try // use GLTF api to verify that all needed files are present
            {
                var result = ModelRoot.Validate(settings.InputGltfFile);
                modelRoot = ModelRoot.Load(settings.InputGltfFile);
            }
            catch (Exception ex)
            {
                var errorList = new ErrorList();
                errorList.Error("GLTF Load Error, might not be valid GLTF file.", ex.Message);
                ErrorListWindow.ShowDialog("Advanced Import Error", errorList, false);
                outModelRoot = null;                
                return false;
            }

            outModelRoot = modelRoot;
            return true;
        }

        private (string? skeletonName, AnimationFile? skeletonAnimFile) GetSkeletonData(ModelRoot? modelRoot)
        {
            if (modelRoot == null)
                throw new ArgumentException($"Invalid Input: {nameof(modelRoot)} == {modelRoot}");

            var skeletonName = FetchSkeletonIdStringFromScene(modelRoot);

            if (skeletonName == null)
            {
                _logger.Information("Skeleton ID not found in scene, if the model loading is not rigged, then ignore this.");
                return ("", null);
            }

            AnimationFile? skeletonAnimFile = null;
            if (skeletonName != null && skeletonName.Any())
            {
                skeletonAnimFile = _skeletonLookUpHelper.GetSkeletonFileFromName(skeletonName);

                if (skeletonAnimFile == null)
                {
                    var errorList = new ErrorList();
                    errorList.Error("Skeleton Not Found", $"Skeleton named '{skeletonName}' could not be found\nHave you selected the correct game AND loaded all CA Pack Files?");

                    ErrorListWindow.ShowDialog("Skeleton Error", errorList);

                    return (skeletonName = null, skeletonAnimFile = null);
                }
            }

            return (skeletonName, skeletonAnimFile);
        }

        // Lower case: since 6.1 an upper-case letter in a pack path crashes the game.
        private static string GetImportedPackFileName(GltfImporterSettings settings) => Path.GetFileNameWithoutExtension(settings.InputGltfFile).Replace(' ', '_').ToLowerInvariant() + ".rigid_model_v2";

        private static string FetchSkeletonIdStringFromScene(ModelRoot modelRoot)
        {
            var nodeSearchResult = modelRoot.LogicalNodes.Where(node => node.Name.StartsWith(Value));

            if (nodeSearchResult == null || !nodeSearchResult.Any())
                return "";

            // Strip the prefix itself. TrimStart(chars) also ate leading name letters from the
            // set "/skeleton", turning e.g. "snake01" into "ake01".
            var skeletonName = nodeSearchResult.First().Name.Substring(Value.Length);

            return skeletonName.ToLower();
        }
    }
}
