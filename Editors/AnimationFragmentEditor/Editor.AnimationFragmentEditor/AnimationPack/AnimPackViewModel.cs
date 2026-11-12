using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.BinGrid;
using Editors.AnimationFragmentEditor.AnimationPack.Commands;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinConverter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Common;
using Shared.Ui.Editors.TextEditor;

namespace CommonControls.Editors.AnimationPack
{
    public partial class AnimPackViewModel : NotifyPropertyChangedImpl, IEditorInterface, ISaveableEditor, IFileEditor
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private ITextConverter? _activeConverter;
        private readonly ApplicationSettingsService _appSettings;
        private readonly IFileSaveService _packFileSaveService;
        private readonly MetaDataFileParser _metaDataFileParser;

        public string DisplayName { get; set; } = "Not set";

        PackFile _packFile;

        public FilterCollection<IAnimationPackFile> AnimationPackItems { get; set; }

        SimpleTextEditorViewModel _selectedItemViewModel;
        public SimpleTextEditorViewModel SelectedItemViewModel { get => _selectedItemViewModel; set => SetAndNotify(ref _selectedItemViewModel, value); }

        // A WH3 battle table also gets the grid, shown instead of the text unless ShowAsXml. The text editor is still built for it, so
        // the XML view is one toggle away and shows the same bytes.
        BinGridViewModel? _binGrid;
        public BinGridViewModel? BinGrid { get => _binGrid; private set => SetAndNotify(ref _binGrid, value); }

        /// <summary>The grid's slot picker: the slot names the table has, in; the one picked, or null, out. The view sets the window.</summary>
        public Func<IReadOnlyCollection<string>, string?> PickSlot { get; set; } = _ => null;

        readonly IStandardDialogs _dialogs;

        public AnimPackViewModel(IUiCommandFactory uiCommandFactory,
            IPackFileService pfs,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            ApplicationSettingsService appSettings,
            IFileSaveService packFileSaveService,
            MetaDataFileParser metaDataFileParser,
            IStandardDialogs dialogs)
        {
            _uiCommandFactory = uiCommandFactory;
            _pfs = pfs;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _appSettings = appSettings;
            _packFileSaveService = packFileSaveService;
            _metaDataFileParser = metaDataFileParser;
            _dialogs = dialogs;
            AnimationPackItems = new FilterCollection<IAnimationPackFile>(new List<IAnimationPackFile>(), OnItemSelected, BeforeItemSelected)
            {
                SearchFilter = (value, rx) => { return rx.Match(value.FileName).Success; }
            };
        }

        [RelayCommand] private void RenameAction() => _uiCommandFactory.Create<RenameSelectedFileCommand>(x => x.Configure(this)).Execute();
        [RelayCommand] private void RemoveAction() => _uiCommandFactory.Create<RemoveSelectedFileCommand>(x => x.Configure(this)).Execute();
        [RelayCommand] private void CopyFullPathAction() => Clipboard.SetText(AnimationPackItems.SelectedItem.FileName);
        [RelayCommand] private void CreateEmptyWarhammer3AnimSetFileAction() => _uiCommandFactory.Create<CreateEmptyWarhammer3AnimSetFileCommand>(x => x.Configure(this)).Execute();
        [RelayCommand] private void ExportAnimationSlotsWh3Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer3();
        [RelayCommand] private void ExportAnimationSlotsWh2Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer2();

        [RelayCommand] private void SaveAction() => Save();
        [RelayCommand] public void UndoAction() => Step(_undo, _redo, x => x.Undo);
        [RelayCommand] public void RedoAction() => Step(_redo, _undo, x => x.Redo);

        // Each change to the pack's list or an entry, as its own inverse: the pack can hold thousands of tables, too many to copy
        // for every step. A change to the text not yet applied has the text editor's own undo.
        readonly Stack<(Action Undo, Action Redo)> _undo = new();
        readonly Stack<(Action Undo, Action Redo)> _redo = new();

        /// <summary>A change made to the pack, recorded for undo. The pack has unsaved changes from here.</summary>
        public void Changed(Action undo, Action redo)
        {
            _undo.Push((undo, redo));
            _redo.Clear();
            HasUnsavedChanges = true;
        }

        void Step(Stack<(Action Undo, Action Redo)> from, Stack<(Action Undo, Action Redo)> to, Func<(Action Undo, Action Redo), Action> pick)
        {
            if (!from.TryPop(out var step))
                return;
            pick(step)();
            to.Push(step);
            HasUnsavedChanges = true;
            AnimationPackItems.RefreshFilter();
        }

        // The entry again, when it is the one shown, after an undo or redo changed it under the editor. A grid is reloaded rather
        // than built anew, so it keeps its place.
        void ShowAgain(IAnimationPackFile file)
        {
            if (AnimationPackItems.SelectedItem != file)
                return;
            if (BinGrid?.Table == file)
            {
                ShowText(file);
                BinGrid.Reload();
            }
            else
            {
                OnItemSelected(file);
            }
        }

        void ShowText(IAnimationPackFile file)
        {
            SelectedItemViewModel.Text = _activeConverter!.GetText(file.ToByteArray());
            SelectedItemViewModel.ResetChangeLog();
        }

        /// <summary>One edit to a table, from the grid: one undo step, and none at all when the table's bytes did not change.</summary>
        public void EditTable(AnimationBinWh3 table, Action<AnimationBinWh3> change)
        {
            var before = table.ToByteArray();
            change(table);
            var after = table.ToByteArray();
            if (before.AsSpan().SequenceEqual(after))
                return;
            table.IsChanged.Value = true;
            Changed(() => { table.CreateFromBytes(before); ShowAgain(table); }, () => { table.CreateFromBytes(after); ShowAgain(table); });
        }

        bool _showAsXml;
        /// <summary>A WH3 table shown as its XML instead of the grid. Back to the grid with text edits not yet applied asks to apply
        /// them; a refused apply stays in the text.</summary>
        public bool ShowAsXml
        {
            get => _showAsXml;
            set
            {
                if (value == _showAsXml)
                    return;
                if (BinGrid != null)
                {
                    if (value)
                    {
                        ShowText(BinGrid.Table);
                    }
                    else if (SelectedItemViewModel.HasUnsavedChanges())
                    {
                        if (_dialogs.ShowYesNoBox("Apply the text edits to the table first?", "Show grid") != ShowMessageBoxResult.OK)
                            ShowText(BinGrid.Table);
                        else if (!SaveActiveFile())
                        {
                            NotifyPropertyChanged();
                            return;
                        }
                    }
                }
                SetAndNotify(ref _showAsXml, value);
                if (!value)
                    BinGrid?.Reload();
            }
        }

        bool BeforeItemSelected(IAnimationPackFile item)
        {
            if (SelectedItemViewModel != null && SelectedItemViewModel.HasUnsavedChanges())
            {
                if (MessageBox.Show("Editor has unsaved changes that will be lost.\nContinue?", "", MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return false;
            }

            return true;
        }

        void OnItemSelected(IAnimationPackFile seletedFile)
        {
            _activeConverter = null;
            if (seletedFile is AnimationFragmentFile typedFragment)
                _activeConverter = new AnimationFragmentFileToXmlConverter(_skeletonAnimationLookUpHelper, _appSettings.CurrentSettings.CurrentGame);
            else if (seletedFile is AnimationBin typedBin)
                _activeConverter = new AnimationBinFileToXmlConverter();
            else if (seletedFile is AnimationBinWh3 wh3Bin)
                _activeConverter = new AnimationBinWh3FileToXmlConverter(_skeletonAnimationLookUpHelper, _metaDataFileParser, CurrentFile);

            if (seletedFile == null || _activeConverter == null || seletedFile.IsUnknownFile)
            {
                SelectedItemViewModel = new SimpleTextEditorViewModel();
                SelectedItemViewModel.SaveCommand = null;
                SelectedItemViewModel.TextEditor?.ShowLineNumbers(true);
                SelectedItemViewModel.TextEditor?.SetSyntaxHighlighting("XML");
                SelectedItemViewModel.Text = "";
                SelectedItemViewModel.ResetChangeLog();
            }
            else
            {
                SelectedItemViewModel = new SimpleTextEditorViewModel();
                SelectedItemViewModel.SaveCommand = new RelayCommand(() => SaveActiveFile());
                SelectedItemViewModel.TextEditor?.ShowLineNumbers(true);
                SelectedItemViewModel.TextEditor?.SetSyntaxHighlighting(_activeConverter.GetSyntaxType());
                SelectedItemViewModel.Text = _activeConverter.GetText(seletedFile.ToByteArray());
                SelectedItemViewModel.ResetChangeLog();
            }

            BinGrid = seletedFile is AnimationBinWh3 { TableVersion: 4, IsUnknownFile: false } table ? CreateGrid(table) : null;
        }

        BinGridViewModel CreateGrid(AnimationBinWh3 table)
        {
            var otherBins = AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Where(x => x != table && x.TableVersion == 4).Select(x => x.Name).ToList();
            return new BinGridViewModel(table, EditTable, Checks(), _dialogs, _uiCommandFactory, _pfs, _skeletonAnimationLookUpHelper, otherBins, slots => PickSlot(slots));
        }

        BinTableChecks Checks() => new(_pfs, _skeletonAnimationLookUpHelper, _metaDataFileParser, _packFile);
        public void Close() { }
        public bool HasUnsavedChanges { get; set; }

        public PackFile CurrentFile => _packFile;

 
        public bool SaveActiveFile()
        {
            if (_packFile == null)
            {
                MessageBox.Show("Can not save in this mode - Open the file normally");
                return false;
            }
      
            var fileName = AnimationPackItems.SelectedItem.FileName;
            var bytes = _activeConverter.ToBytes(SelectedItemViewModel.Text, fileName, _pfs, out var error);

            if (bytes == null || error != null)
            {
                SelectedItemViewModel.TextEditor.HightLightText(error.ErrorLineNumber, error.ErrorPosition, error.ErrorLength);
                MessageBox.Show(error.Text, "Error");
                return false;
            }

            var seletedFile = AnimationPackItems.SelectedItem;
            var before = seletedFile.ToByteArray();
            seletedFile.CreateFromBytes(bytes);
            seletedFile.IsChanged.Value = true;

            SelectedItemViewModel.ResetChangeLog();
            Changed(() => { seletedFile.CreateFromBytes(before); ShowAgain(seletedFile); }, () => { seletedFile.CreateFromBytes(bytes); ShowAgain(seletedFile); });

            return true;
        }


        public bool Save()
        {
            if (_packFile == null)
            {
                MessageBox.Show("Can not save in this mode - Open the file normally");
                return false;
            }

            if (SelectedItemViewModel != null && SelectedItemViewModel.HasUnsavedChanges())
            {
                if (MessageBox.Show("Editor has unsaved changes.\nSave anyway?", "", MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return false;
            }

            // The grid writes every edit, so a hard error is caught here rather than on an Apply. Only the tables edited since the
            // last save are checked: one that already failed, a skeleton from a pack not loaded say, does not hold up the rest.
            var checks = Checks();
            var problems = AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Where(x => x.IsChanged.Value && x.TableVersion == 4)
                .SelectMany(table => checks.Header(table, table.FileName).Where(x => x.IsError).Select(x => $"{table.FileName}: {x.Message}"))
                .ToList();
            if (problems.Count != 0 && _dialogs.ShowYesNoBox(string.Join("\n", problems) + "\n\nSave anyway?", "Save") != ShowMessageBoxResult.OK)
                return false;

            var newAnimPack = new AnimationPackFileDatabase(_pfs.GetFullPath(_packFile));

            foreach (var file in AnimationPackItems.PossibleValues)
                newAnimPack.AddFile(file);

            var savePath = _pfs.GetFullPath(_packFile);

            var result = _packFileSaveService.Save(savePath, AnimationPackSerializer.ConvertToBytes(newAnimPack), false);
            if (result != null)
            {
                HasUnsavedChanges = false;
                foreach (var file in AnimationPackItems.PossibleValues)
                    file.IsChanged.Value = false;
            }

            return true;
        }


        public void LoadFile(PackFile file)
        {
            _packFile = file;
            var animPack = AnimationPackSerializer.Load(_packFile, _pfs);
            var itemNames = animPack.Files.ToList();
            AnimationPackItems.UpdatePossibleValues(itemNames);
            DisplayName = animPack.FileName;
            _undo.Clear();
            _redo.Clear();
        }
    }
}
