using System.IO;
using CommonControls.SelectionListDialog;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
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
            return AnimationFile.ConvertToBytes(animFile);
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
