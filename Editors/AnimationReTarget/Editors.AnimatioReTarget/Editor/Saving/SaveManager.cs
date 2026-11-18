using System.Globalization;
using System.IO;
using CommonControls.SelectionListDialog;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling;
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
using Shared.Ui.BaseDialogs.SelectionListDialog;
using Shared.Ui.Common;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    /// <summary>What a batch export did: animations saved, skipped as already in the pack, and failed with why.</summary>
    public record BatchExportResult(int Saved, int Skipped, List<string> Failed);

    public partial class SaveManager : ObservableObject
    {
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IPackFileService _pfs;
        private readonly IFileSaveService _saveService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly IAbstractFormFactory<SaveWindow> _settingsWindowFactory;
        [ObservableProperty] SaveSettings _settings;

        SceneObject _generated;
        SceneObject _source;
        SceneObject _target;

        /// <summary>Retargets one source clip with the editor's current mapping and settings. Set by the editor.</summary>
        public Func<AnimationClip, AnimationClip>? Remap { get; set; }

        public SaveManager(BoneManager boneManager,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            IPackFileService pfs,
            SaveSettings saveSettings,
            IFileSaveService saveService,
            IStandardDialogs standardDialogs,
            IAbstractFormFactory<SaveWindow> settingsWindowFactory)
        {
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _pfs = pfs;
            _saveService = saveService;
            _standardDialogs = standardDialogs;
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

        public void SetSceneNodes(SceneObject source, SceneObject target, SceneObject generated)
        {
            _generated = generated;
            _source = source;
            _target = target;
        }

        public void SaveAnimation(bool prompOnConflict = true)
        {
            var outputPath = OutputPath(_source?.AnimationName.Value);
            if (_generated?.AnimationClip == null || outputPath == null)
            {
                _standardDialogs.ShowDialogBox("Generated skeleton not set, or animation not created");
                return;
            }

            _saveService.Save(outputPath, ToBytes(_generated.AnimationClip), prompOnConflict);
        }

        byte[] ToBytes(AnimationClip clip)
        {
            var animFile = clip.ConvertToFileFormat(_generated.Skeleton);
            if (Settings.AnimationFormat != 7)
            {
                var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(animFile.Header.SkeletonName);
                animFile.ConvertToVersion(Settings.AnimationFormat, skeleton, _pfs);
            }
            // Renamed after the conversion, which looks the skeleton up by the header's name.
            if (Settings.UseGeneratedSkeleton && !string.IsNullOrWhiteSpace(Settings.ScaledSkeletonName))
                animFile.Header.SkeletonName = Settings.ScaledSkeletonName.Trim();
            return AnimationFile.ConvertToBytes(animFile);
        }

        /// <summary>The skeleton scale the generated animation is made at. Set by the editor.</summary>
        public Func<float>? SkeletonScale { get; set; }

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
            var scale = SkeletonScale?.Invoke() ?? 1;
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

        /// <summary>Every animation made for the source skeleton, by path, skeleton files left out.</summary>
        public List<string> BatchCandidates()
        {
            if (_source?.Skeleton == null)
                return [];
            return _skeletonAnimationLookUpHelper.GetAnimationsForSkeleton(_source.Skeleton.SkeletonName)
                .Select(x => x.AnimationFile)
                .Where(x => !x.StartsWith(@"animations\skeletons", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
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

        /// <summary>
        /// Retargets each animation with the current mapping and settings and saves it where Save would save it. Asks once when any
        /// of them is already in the editable pack: overwrite them all, skip them all, or stop. Null when stopped. One animation
        /// that fails is listed and the rest still go.
        /// </summary>
        public BatchExportResult? BatchExport(IReadOnlyList<string> sourceAnimationPaths)
        {
            var editablePack = _pfs.GetEditablePack();
            if (editablePack == null || Remap == null || _source?.Skeleton == null || _target?.Skeleton == null)
            {
                _standardDialogs.ShowDialogBox("Pick a source and a target model, and have an editable pack, before a batch export.");
                return null;
            }

            var jobs = sourceAnimationPaths.Select(x => (Source: x, Output: OutputPath(x)!)).ToList();
            var existing = jobs.Count(x => _pfs.FindFile(x.Output, editablePack) != null);
            var overwrite = true;
            if (existing != 0)
            {
                var choice = _standardDialogs.ShowChoiceBox($"{existing} of the {jobs.Count} animations are already in your pack.", "Batch export", ["Overwrite them", "Skip them", "Cancel"]);
                if (choice != 0 && choice != 1)
                    return null;
                overwrite = choice == 0;
            }

            var saved = 0;
            var skipped = 0;
            var failed = new List<string>();
            foreach (var (source, output) in jobs)
            {
                if (!overwrite && _pfs.FindFile(output, editablePack) != null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var file = _pfs.FindFile(source) ?? throw new FileNotFoundException("not in any loaded pack");
                    var clip = new AnimationClip(AnimationFile.Create(file), _source.Skeleton);
                    _saveService.Save(output, ToBytes(Remap(clip)), false);
                    saved++;
                }
                catch (Exception e)
                {
                    failed.Add($"{source}: {e.Message}");
                }
            }
            return new BatchExportResult(saved, skipped, failed);
        }

        public void ShowBatchExport()
        {
            var items = BatchCandidates()
                .Select(x => new SelectionListViewModel<string>.Item { DisplayName = x, ItemValue = x })
                .ToList();
            if (items.Count == 0)
            {
                _standardDialogs.ShowDialogBox("Pick a source model first. Batch export lists the animations made for its skeleton.");
                return;
            }

            var window = SelectionListWindow.ShowDialog($"Animations to retarget onto {_target?.Skeleton?.SkeletonName} (filter, then Select all)", items);
            var picked = items.Where(x => x.IsChecked.Value).Select(x => x.ItemValue).ToList();
            if (window.Result == false || picked.Count == 0)
                return;
            RunBatch(picked);
        }

        /// <summary>Batch export by animation set: every animation the ticked bins play, e.g. all of a unit's.</summary>
        public void ShowBatchExportFromSets()
        {
            var items = BatchSets()
                .Select(x => (Set: x, Count: AnimationsIn([x]).Count))
                .Where(x => x.Count != 0)
                .Select(x => new SelectionListViewModel<IAnimationBinGenericFormat>.Item { DisplayName = $"{x.Set.FullPath} ({x.Count} animations)", ItemValue = x.Set })
                .ToList();
            if (items.Count == 0)
            {
                _standardDialogs.ShowDialogBox("No animation set (bin) in the loaded packs plays animations for the source skeleton. Pick a source model first, and load the packs that hold its bins.");
                return;
            }

            var window = SelectionListWindow.ShowDialog($"Animation sets to retarget onto {_target?.Skeleton?.SkeletonName}: every animation in the ticked bins", items);
            var picked = AnimationsIn(items.Where(x => x.IsChecked.Value).Select(x => x.ItemValue));
            if (window.Result == false || picked.Count == 0)
                return;
            RunBatch(picked);
        }

        void RunBatch(List<string> picked)
        {
            if (picked.Count > 50 && _standardDialogs.ShowChoiceBox($"Retarget {picked.Count} animations? AssetEditor won't respond until it finishes.", "Batch export", ["Retarget them", "Cancel"]) != 0)
                return;

            BatchExportResult? result;
            using (new WaitCursor())
                result = BatchExport(picked);
            if (result == null)
                return;

            var summary = $"Saved {result.Saved} animations.";
            if (result.Skipped != 0)
                summary += $" Skipped {result.Skipped} already in your pack.";
            if (result.Failed.Count != 0)
                summary += $"\n\n{result.Failed.Count} failed:\n" + string.Join("\n", result.Failed.Take(20));
            _standardDialogs.ShowDialogBox(summary, "Batch export");
        }
    }
}
