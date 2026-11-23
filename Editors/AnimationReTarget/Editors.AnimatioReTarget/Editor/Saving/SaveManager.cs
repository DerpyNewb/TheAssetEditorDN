using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.Settings;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.RigidModel;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    public partial class SaveManager : ObservableObject
    {
        private readonly BoneManager _boneManager;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IPackFileService _pfs;
        private readonly IFileSaveService _saveService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly LocalizationManager _loc;
        private readonly IAbstractFormFactory<SaveWindow> _settingsWindowFactory;
        [ObservableProperty] SaveSettings _settings;

        SceneObject _generated;
        SceneObject _source;
        SceneObject _target;
        AnimationGenerationSettings _generationSettings;

        public SaveManager(BoneManager boneManager,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            IPackFileService pfs,
            SaveSettings saveSettings,
            IFileSaveService saveService,
            IStandardDialogs standardDialogs,
            LocalizationManager localizationManager,
            IAbstractFormFactory<SaveWindow> settingsWindowFactory)
        {
            _boneManager = boneManager;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _pfs = pfs;
            _saveService = saveService;
            _standardDialogs = standardDialogs;
            _loc = localizationManager;
            _settingsWindowFactory = settingsWindowFactory;
            _settings = saveSettings;
            _settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SaveSettings.SavePrefix))
                    RefreshOutputPath();
            };
        }

        void RefreshOutputPath() => Settings.OutputPath = OutputPath(_source?.AnimationName.Value) ?? "";

        /// <summary>A source animation's path with the target skeleton's folder and the prefix, or null before both models are set.</summary>
        public string? OutputPath(string? sourceAnimationPath)
        {
            if (string.IsNullOrEmpty(sourceAnimationPath) || _source?.Skeleton == null || _target?.Skeleton == null)
                return null;

            var newPath = sourceAnimationPath.Replace(_source.Skeleton.SkeletonName, _target.Skeleton.SkeletonName);
            var currentFileName = Path.GetFileName(newPath);
            newPath = Path.Combine(Path.GetDirectoryName(newPath) ?? "", Settings.SavePrefix + currentFileName);
            return SaveUtility.EnsureEnding(newPath, ".anim");
        }

        public void SetSceneNodes(SceneObject source, SceneObject target, SceneObject generated, AnimationGenerationSettings generationSettings)
        {
            _generated = generated;
            _source = source;
            _target = target;
            _generationSettings = generationSettings;
        }

        public void SaveAnimation(bool prompOnConflict = true)
        {
            var outputPath = OutputPath(_source?.AnimationName.Value);
            if (_generated?.AnimationClip == null || outputPath == null)
            {
                _standardDialogs.ShowDialogBox("Generated skeleton not set, or animation not created");
                return;
            }

            var sourceFile = Settings.AnimationFormat == 8 ? _pfs.FindFile(_source!.AnimationName.Value) : null;
            var sourceHeader = sourceFile == null ? null : AnimationFile.Create(sourceFile).Header;   // the header alone can run past what GetAnimationHeader peeks
            _saveService.Save(outputPath, ToBytes(_generated.AnimationClip, _generated.Skeleton, sourceHeader), prompOnConflict);
        }

        // The single save and the batch both write through here, so they write the same bytes for the same clip
        byte[] ToBytes(AnimationClip clip, GameSkeleton skeleton, AnimationFile.AnimationHeader? sourceHeader)
        {
            var animFile = ToOutputFile(clip, skeleton, sourceHeader);
            // Renamed after the conversion, which looks the skeleton up by the header's name.
            if (Settings.UseGeneratedSkeleton && !string.IsNullOrWhiteSpace(Settings.ScaledSkeletonName))
                animFile.Header.SkeletonName = Settings.ScaledSkeletonName.Trim();
            return AnimationFile.ConvertToBytes(animFile);
        }

        // The retargeted clip is on another skeleton, so only the source's header values carry over to v8 (flags,
        // unknown_v8), never its packing
        AnimationFile ToOutputFile(AnimationClip clip, GameSkeleton skeleton, AnimationFile.AnimationHeader? sourceHeader)
        {
            if (Settings.AnimationFormat == 8)
            {
                var unknownValueV8 = sourceHeader?.Version == 8 ? sourceHeader.UnknownValue_v8 : (uint?)null;
                return clip.ConvertToFileFormat(skeleton, 8, unknownValueV8, sourceHeader?.FlagVariables);
            }

            var animFile = clip.ConvertToFileFormat(skeleton);
            if (Settings.AnimationFormat != 7)
            {
                var skeletonFile = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(animFile.Header.SkeletonName);
                animFile.ConvertToVersion(Settings.AnimationFormat, skeletonFile, _pfs);
            }
            return animFile;
        }

        string BuildSourcePathOutput(string sourcePath) =>
            OutputPath(sourcePath) ?? throw new InvalidOperationException(_loc.Get("AnimationReTarget.Batch.Error.SkeletonsRequired"));

        /// <summary>"_s" and the scale's digits: humanoid01_s15 is humanoid01 at 1.5.</summary>
        public static string ScaleSuffix(float scale) => "_s" + scale.ToString(CultureInfo.InvariantCulture).Replace(".", "");

        /// <summary>
        /// Saves the target skeleton and every model loaded for it, the hidden dismemberment stumps too, at the skeleton scale, so
        /// animations saved at that scale play on them in game. A saved animation keeps the scale only in its bone positions, so on the unscaled skeleton
        /// the model's parts drift apart instead of growing. The skeleton and its .bone_inv_trans_mats go to animations\skeletons,
        /// each model beside its original, all named with ScaleSuffix, and animations saved from then on name the new skeleton.
        /// Returns the paths saved, or null when nothing was.
        /// </summary>
        public List<string>? ExportScaledMesh()
        {
            const string title = "Export at scale";
            var scale = _generationSettings?.SkeletonScale ?? 1;
            var skeleton = _generated?.Skeleton;
            var root = _generated?.ModelNode;
            var models = root == null ? [] : SceneNodeHelper.GetChildrenOfType<Rmv2ModelNode>(root).Append(root as Rmv2ModelNode)
                .Select(x => x?.FilePath).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var editablePack = _pfs.GetEditablePack();
            var refusal = skeleton == null || models.Count == 0 ? "Pick a target model first."
                : scale <= 0 || scale == 1 ? "Set a skeleton scale other than 1 first. At 1 the target's own skeleton and models already fit."
                : editablePack == null ? "Select an editable pack first." : null;
            if (refusal != null)
            {
                _standardDialogs.ShowDialogBox(refusal, title);
                return null;
            }

            var suffix = ScaleSuffix(scale);
            var name = skeleton!.SkeletonName + suffix;
            var skeletonPath = $@"animations\skeletons\{name}.anim";
            var invMatsPath = Path.ChangeExtension(skeletonPath, ".bone_inv_trans_mats");
            var outputs = models.Select(x => (Source: x, Output: Path.Combine(Path.GetDirectoryName(x) ?? "", Path.GetFileNameWithoutExtension(x) + suffix + ".rigid_model_v2"))).ToList();
            var paths = outputs.Select(x => x.Output).Prepend(invMatsPath).Prepend(skeletonPath).ToList();

            var existing = paths.Count(x => _pfs.FindFile(x, editablePack) != null);
            if (existing != 0 && _standardDialogs.ShowChoiceBox($"{existing} of the {paths.Count} files are already in your pack.", title, ["Overwrite them", "Cancel"]) != 0)
                return null;

            // Every model is scaled before anything is written, so one that cannot be leaves the pack as it was.
            List<(string Output, byte[] Bytes)> scaled;
            try
            {
                scaled = outputs.Select(x => (x.Output, ScaleModel(_pfs.FindFile(x.Source)?.DataSource.ReadData() ?? throw new FileNotFoundException($"{x.Source} is in no loaded pack"), scale, name))).ToList();
            }
            catch (Exception e)
            {
                _standardDialogs.ShowDialogBox($"Nothing was saved. {e.Message}", title);
                return null;
            }

            var scaledSkeleton = skeleton.Clone();
            for (var i = 0; i < scaledSkeleton.BoneCount; i++)
                scaledSkeleton.Translation[i] *= scale;
            scaledSkeleton.SkeletonName = name;
            scaledSkeleton.RebuildSkeletonMatrix();

            _saveService.Save(skeletonPath, AnimationFile.ConvertToBytes(AnimationClip.CreateSkeletonAnimation(scaledSkeleton).ConvertToFileFormat(scaledSkeleton)), false);
            _saveService.Save(invMatsPath, scaledSkeleton.CreateInvMatrixFile().GetBytes(), false);
            foreach (var (output, bytes) in scaled)
                _saveService.Save(output, bytes, false);

            Settings.UseGeneratedSkeleton = true;
            Settings.ScaledSkeletonName = name;
            _standardDialogs.ShowDialogBox($"Saved at {scale.ToString(CultureInfo.InvariantCulture)}x:\n{string.Join("\n", paths)}\n\nAnimations you save from now on name {name}. " +
                $"Point the animation set (bin) at {name} and the variant mesh at the new models.", title);
            return paths;
        }

        /// <summary>A .rigid_model_v2 with every vertex, bounding box and pivot scaled about the origin, naming the skeleton given.</summary>
        public static byte[] ScaleModel(byte[] bytes, float scale, string skeletonName)
        {
            var file = ModelFactory.Create().Load(bytes);
            var header = file.Header;
            header.SkeletonName = skeletonName;
            file.Header = header;
            foreach (var model in file.ModelList.SelectMany(x => x))
            {
                if (model.Mesh.RawVertexData != null)
                    throw new InvalidDataException($"{model.Material.ModelName} has a vertex layout AssetEditor cannot read, so it cannot be scaled.");
                foreach (var vertex in model.Mesh.VertexList)
                    vertex.Position = new Vector4(vertex.GetPosistionAsVec3() * scale, vertex.Position.W);

                var common = model.CommonHeader;
                var box = common.BoundingBox;
                common.BoundingBox.UpdateBoundingBox(new BoundingBox(new Vector3(box.MinimumX, box.MinimumY, box.MinimumZ) * scale, new Vector3(box.MaximumX, box.MaximumY, box.MaximumZ) * scale));
                model.CommonHeader = common;
                model.Material.PivotPoint *= scale;
            }
            return ModelFactory.Create().Save(file);
        }

        [RelayCommand] void ShowSaveSettings()
        {
            RefreshOutputPath();
            var window = _settingsWindowFactory.Create();
            window.Initialize(this);
            window.ShowDialog();
        }

        // Batch retarget. Ported from Szy-Cathay/TheAssetEditor-CN 08d0ed5dc, writing to the editable pack instead
        // of the fork's folder project

        public bool CanBatchRetarget(out string errorMessage)
        {
            if (_source?.Skeleton == null || _target?.Skeleton == null)
                errorMessage = _loc.Get("AnimationReTarget.Batch.Error.SkeletonsRequired");
            else if (!float.IsFinite(_generationSettings.AnimationSpeedMult) || _generationSettings.AnimationSpeedMult <= 0)
                errorMessage = _loc.Get("AnimationReTarget.Batch.Error.InvalidSpeed");
            else if (!_boneManager.FlatBones.Any(x => x.HasMapping && x.MappedIndex >= 0))
                errorMessage = _loc.Get("AnimationReTarget.Batch.Error.MappingRequired");
            else if (_pfs.GetEditablePack() == null)
                errorMessage = _loc.Get("AnimationReTarget.Batch.Error.EditablePackRequired");
            else
                errorMessage = string.Empty;

            return errorMessage.Length == 0;
        }

        /// <summary>The source skeleton's animations, without the skeleton files under animations\skeletons\.</summary>
        public IReadOnlyList<AnimationReference> GetBatchCandidates()
        {
            if (_source?.Skeleton == null)
                return [];

            // The copy, not the live list: a pack load refreshes the live one on a worker thread mid-walk
            return _skeletonAnimationLookUpHelper
                .CopyAnimationsForSkeleton(_source.Skeleton.SkeletonName)
                .Where(x => !IsSkeletonAnimationPath(x.AnimationFile))
                .OrderBy(x => x.AnimationFile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Container?.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The source skeleton's animation sets (bins) in the loaded packs, each with the animations it plays.</summary>
        public List<IAnimationBinGenericFormat> BatchSets() =>
            _source?.Skeleton == null ? [] : AnimationSets.ForSkeleton(_pfs, _source.Skeleton.SkeletonName).OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Every animation the sets play, each once, in the backslashed form the rest of the editor uses: a set names an
        /// animation once per slot that plays it, often in forward slashes, and leaves some slots empty.
        /// </summary>
        public static List<string> AnimationsIn(IEnumerable<IAnimationBinGenericFormat> sets) => sets
            .SelectMany(x => x.Entries)
            .Select(x => x.AnimationFile)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().Replace('/', '\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        public static bool IsTechAnimation(AnimationReference animation) =>
            GetPathSegments(animation.AnimationFile).Contains("tech", StringComparer.OrdinalIgnoreCase);

        public static bool IsSkeletonAnimationPath(string path)
        {
            if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                return false;

            var segments = GetPathSegments(path);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("animations", StringComparison.OrdinalIgnoreCase) &&
                    segments[i + 1].Equals("skeletons", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string[] GetPathSegments(string path) => path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

        public Task<BatchAnimationRetargetResult> ExecuteBatchAsync(BatchAnimationRetargetRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
        {
            // Checked before the background task starts, so a bad setup throws to the caller and writes nothing
            if (!CanBatchRetarget(out var validationError))
                throw new InvalidOperationException(validationError);
            if (request.PathMode == BatchRetargetPathMode.SelectedFolder && string.IsNullOrWhiteSpace(request.TargetFolder))
                throw new InvalidOperationException(_loc.Get("AnimationReTarget.Batch.Error.TargetFolderRequired"));

            // The bone settings are read on the background task, so the window keeps them disabled until it ends
            return Task.Run(() => ExecuteBatch(request, progress, cancellationToken));
        }

        BatchAnimationRetargetResult ExecuteBatch(BatchAnimationRetargetRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
        {
            var outputPack = _pfs.GetEditablePack()!;

            // Plan every output path first. A path that collides with an earlier item, or that already exists when
            // not overwriting, is skipped
            var planned = request.Animations.Select(x => PlanItem(request, x)).ToList();
            var seenOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new HashSet<int>();
            for (var i = 0; i < planned.Count; i++)
            {
                var outputPath = planned[i].OutputPath;
                if (outputPath == null)
                    continue;

                if (!seenOutputs.Add(outputPath) || (!request.OverwriteExisting && _pfs.FindFile(outputPath, outputPack) != null))
                    skipped.Add(i);
            }

            var results = new List<BatchAnimationRetargetItemResult>(planned.Count);
            for (var i = 0; i < planned.Count; i++)
            {
                var item = planned[i];
                var sourcePath = item.Animation.AnimationFile;

                if (cancellationToken.IsCancellationRequested)
                {
                    results.AddRange(planned.Skip(i).Select(x => new BatchAnimationRetargetItemResult(x.Animation.AnimationFile, x.OutputPath, BatchAnimationRetargetItemStatus.NotProcessed)));
                    break;
                }

                if (item.ErrorMessage != null)
                {
                    results.Add(new(sourcePath, null, BatchAnimationRetargetItemStatus.Failed, item.ErrorMessage));
                }
                else if (skipped.Contains(i))
                {
                    results.Add(new(sourcePath, item.OutputPath, BatchAnimationRetargetItemStatus.Skipped));
                }
                else
                {
                    try
                    {
                        var content = RetargetAnimation(item.Animation);
                        RunOnUiThread(() => _saveService.Save(item.OutputPath!, content, false));
                        results.Add(new(sourcePath, item.OutputPath, BatchAnimationRetargetItemStatus.Success));
                    }
                    catch (Exception e)
                    {
                        results.Add(new(sourcePath, item.OutputPath, BatchAnimationRetargetItemStatus.Failed, e.Message));
                    }
                }

                progress?.Report(new BatchProgress(i + 1, planned.Count, sourcePath));
            }

            return new BatchAnimationRetargetResult(results);
        }

        record BatchPlanItem(AnimationReference Animation, string? OutputPath, string? ErrorMessage);

        BatchPlanItem PlanItem(BatchAnimationRetargetRequest request, AnimationReference animation)
        {
            try
            {
                return new BatchPlanItem(animation, BuildBatchOutputPath(request, animation.AnimationFile), null);
            }
            catch (Exception e)
            {
                return new BatchPlanItem(animation, null, e.Message);
            }
        }

        string BuildBatchOutputPath(BatchAnimationRetargetRequest request, string sourcePath)
        {
            if (request.PathMode == BatchRetargetPathMode.SourcePath)
                return BuildSourcePathOutput(sourcePath);

            // Only the folder that holds the .anim is kept, so stand\idle.anim lands in <chosen>\stand\idle.anim
            var containingFolder = Path.GetFileName(Path.GetDirectoryName(sourcePath));
            if (string.IsNullOrWhiteSpace(containingFolder))
                throw new InvalidDataException(string.Format(_loc.Get("AnimationReTarget.Batch.Error.SourceFolderRequired"), sourcePath));

            var fileName = Settings.SavePrefix + Path.GetFileName(sourcePath);
            var targetFolder = request.TargetFolder!.Replace('/', '\\').Trim('\\');
            return SaveUtility.EnsureEnding(Path.Combine(targetFolder, containingFolder, fileName), ".anim");
        }

        byte[] RetargetAnimation(AnimationReference animation)
        {
            var packFile = _pfs.FindFile(animation.AnimationFile, animation.Container)
                ?? throw new FileNotFoundException(string.Format(_loc.Get("AnimationReTarget.Batch.Error.AnimationNotFound"), animation.AnimationFile));

            var sourceFile = AnimationFile.Create(packFile);
            var sourceClip = new AnimationClip(sourceFile, _source.Skeleton);
            var remapper = new AnimationRemapperService(_generationSettings, _boneManager.Bones);
            var remappedClip = remapper.ReMapAnimation(_source.Skeleton, _target.Skeleton, sourceClip);

            return ToBytes(remappedClip, _target.Skeleton, sourceFile.Header);
        }

        // Adding to the pack raises events the pack tree handles, so writes go through the UI thread. Tests have no
        // Application and write directly
        static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
    }
}
