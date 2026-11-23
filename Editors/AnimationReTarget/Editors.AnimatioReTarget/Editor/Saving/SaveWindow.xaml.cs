using System.ComponentModel;
using System.Windows;
using CommonControls.SelectionListDialog;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.BaseDialogs.SelectionListDialog;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    public partial class SaveWindow : Window
    {
        private readonly SaveSettings _settings;
        private readonly IStandardDialogs _standardDialogs;
        private readonly LocalizationManager _loc;
        private SaveManager _saveManager;
        private CancellationTokenSource? _batchCancellation;

        public SaveWindow(SaveSettings viewModel, IStandardDialogs standardDialogs, LocalizationManager localizationManager)
        {
            InitializeComponent();
            _settings = viewModel;
            _standardDialogs = standardDialogs;
            _loc = localizationManager;
            DataContext = viewModel;
        }

        public void Initialize(SaveManager saveManager)
        {
            _saveManager = saveManager;
        }

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            _saveManager.SaveAnimation();
            Close();
        }

        private void ExportScaledMeshClick(object sender, RoutedEventArgs e) => _saveManager.ExportScaledMesh();

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BrowseButtonClick(object sender, RoutedEventArgs e)
        {
            var result = _standardDialogs.DisplayBrowseFolderDialog();
            if (result.Result)
                _settings.BatchTargetFolder = result.Folder;
        }

        private void CancelBatchButtonClick(object sender, RoutedEventArgs e)
        {
            _batchCancellation?.Cancel();
            CancelBatchButton.IsEnabled = false;
        }

        private void BatchButtonClick(object sender, RoutedEventArgs e)
        {
            if (!CanStartBatch())
                return;

            var candidates = _saveManager.GetBatchCandidates();
            if (candidates.Count == 0)
            {
                _standardDialogs.ShowDialogBox(_loc.Get("AnimationReTarget.Batch.Error.NoCandidates"));
                return;
            }

            var items = candidates.Select(x => new SelectionListViewModel<AnimationReference>.Item()
            {
                IsChecked = new NotifyAttr<bool>(!SaveManager.IsTechAnimation(x)),
                DisplayName = x.ToString(),
                ItemValue = x
            }).ToList();

            var selectionWindow = SelectionListWindow.ShowDialog(_loc.Get("AnimationReTarget.Batch.SelectAnimations"), items);
            if (!selectionWindow.Result)
                return;

            RunBatch(items.Where(x => x.IsChecked.Value).Select(x => x.ItemValue).ToList());
        }

        // Every animation the ticked bins play, each once: all of a unit's, say
        private void BatchSetsButtonClick(object sender, RoutedEventArgs e)
        {
            if (!CanStartBatch())
                return;

            var items = _saveManager.BatchSets()
                .Select(x => (Set: x, Count: SaveManager.AnimationsIn([x]).Count))
                .Where(x => x.Count != 0)
                .Select(x => new SelectionListViewModel<IAnimationBinGenericFormat>.Item { DisplayName = $"{x.Set.FullPath} ({x.Count})", ItemValue = x.Set })
                .ToList();
            if (items.Count == 0)
            {
                _standardDialogs.ShowDialogBox(_loc.Get("AnimationReTarget.Batch.Error.NoSets"));
                return;
            }

            var selectionWindow = SelectionListWindow.ShowDialog(_loc.Get("AnimationReTarget.Batch.SelectSets"), items);
            if (!selectionWindow.Result)
                return;

            // A set names an animation by path alone, so no container: the batch reads whichever loaded pack wins
            RunBatch(SaveManager.AnimationsIn(items.Where(x => x.IsChecked.Value).Select(x => x.ItemValue))
                .Select(x => new AnimationReference(x, null!))
                .ToList());
        }

        bool CanStartBatch()
        {
            if (!_saveManager.CanBatchRetarget(out var validationError))
            {
                _standardDialogs.ShowDialogBox(validationError);
                return false;
            }

            if (_settings.BatchUseSelectedFolder && string.IsNullOrWhiteSpace(_settings.BatchTargetFolder))
            {
                _standardDialogs.ShowDialogBox(_loc.Get("AnimationReTarget.Batch.Error.TargetFolderRequired"));
                return false;
            }
            return true;
        }

        async void RunBatch(List<AnimationReference> selected)
        {
            if (selected.Count == 0)
            {
                _standardDialogs.ShowDialogBox(_loc.Get("AnimationReTarget.Batch.Error.NoSelection"));
                return;
            }

            var request = new BatchAnimationRetargetRequest(
                selected,
                _settings.BatchUseSelectedFolder ? BatchRetargetPathMode.SelectedFolder : BatchRetargetPathMode.SourcePath,
                _settings.BatchTargetFolder,
                _settings.BatchOverwriteExisting);

            BatchAnimationRetargetResult? result = null;
            _batchCancellation = new CancellationTokenSource();
            SetBatchRunning(true, selected.Count);
            try
            {
                var progress = new Progress<BatchProgress>(ApplyProgress);
                result = await _saveManager.ExecuteBatchAsync(request, progress, _batchCancellation.Token);
            }
            catch (Exception exception)
            {
                _standardDialogs.ShowExceptionWindow(exception, _loc.Get("AnimationReTarget.Batch.Error.Failed"));
            }
            finally
            {
                _batchCancellation.Dispose();
                _batchCancellation = null;
                SetBatchRunning(false, 0);
            }

            if (result != null)
                ShowBatchResult(result);
        }

        void SetBatchRunning(bool isRunning, int total)
        {
            OptionsPanel.IsEnabled = !isRunning;
            CloseButton.IsEnabled = !isRunning;
            ProgressPanel.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            CancelBatchButton.IsEnabled = isRunning;
            BatchProgressBar.Maximum = Math.Max(1, total);
            BatchProgressBar.Value = 0;
            ProgressText.Text = string.Empty;
        }

        void ApplyProgress(BatchProgress progress)
        {
            BatchProgressBar.Maximum = Math.Max(1, progress.Total);
            BatchProgressBar.Value = progress.Completed;
            ProgressText.Text = $"{progress.Completed} / {progress.Total}  {progress.CurrentFile}";
        }

        void ShowBatchResult(BatchAnimationRetargetResult result)
        {
            var items = new ErrorList();
            foreach (var item in result.Items)
            {
                var path = item.OutputPath == null ? item.SourcePath : $"{item.SourcePath} -> {item.OutputPath}";
                switch (item.Status)
                {
                    case BatchAnimationRetargetItemStatus.Success:
                        items.Ok(path, _loc.Get("AnimationReTarget.Batch.Result.Success"));
                        break;
                    case BatchAnimationRetargetItemStatus.Skipped:
                        items.Warning(path, _loc.Get("AnimationReTarget.Batch.Result.Skipped"));
                        break;
                    case BatchAnimationRetargetItemStatus.Failed:
                        items.Error(path, string.Format(_loc.Get("AnimationReTarget.Batch.Result.Failed"), item.ErrorMessage ?? string.Empty));
                        break;
                    case BatchAnimationRetargetItemStatus.NotProcessed:
                        items.Warning(path, _loc.Get("AnimationReTarget.Batch.Result.NotProcessed"));
                        break;
                }
            }

            var title = string.Format(_loc.Get("AnimationReTarget.Batch.Result.Title"),
                result.SuccessCount, result.SkippedCount, result.FailureCount, result.NotProcessedCount);
            _standardDialogs.ShowErrorViewDialog(title, items);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // The batch reads the bone settings in the background; closing mid-run would hide its result
            if (_batchCancellation != null)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
