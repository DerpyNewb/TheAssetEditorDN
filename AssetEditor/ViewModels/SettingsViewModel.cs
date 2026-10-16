using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace AssetEditor.ViewModels
{
    partial class SettingsViewModel : ObservableObject
    {
        private readonly ApplicationSettingsService _settingsService;
        private readonly LocalizationManager _localizationManager;

        public ObservableCollection<string> AvailableLangauges { get; set; } = [];
        public ObservableCollection<ThemeType> AvailableThemes { get; set; } = [];
        public ObservableCollection<BackgroundColour> RenderEngineBackgroundColours { get; set; } = [];
        public ObservableCollection<GameTypeEnum> Games { get; set; } = [];
        public ObservableCollection<GamePathItem> GameDirectores { get; set; } = [];
        public ObservableCollection<CameraControlMode> CameraModes { get; set; } = [];

        [ObservableProperty] private string _selectedLanguage;
        [ObservableProperty] private ThemeType _currentTheme;
        [ObservableProperty] private BackgroundColour _currentRenderEngineBackgroundColour;
        [ObservableProperty] private BackgroundColour _currentTextEditorBackgroundColour;
        [ObservableProperty] private int _visualEditorsGridSize;
        [ObservableProperty] private ColourPickerViewModel _vertexSelectionColour;
        [ObservableProperty] private bool _startMaximised;
        [ObservableProperty] private bool _showStartPanel;
        [ObservableProperty] private GameTypeEnum _currentGame;
        [ObservableProperty] private bool _loadCaPacksByDefault;
        [ObservableProperty] private bool _showCAWemFiles;
        [ObservableProperty] private bool _onlyLoadLod0ForReferenceMeshes;
        [ObservableProperty] private bool _previewTintCapability;
        [ObservableProperty] private CameraControlMode _selectedCameraMode;
        [ObservableProperty] private int _autosaveMinutes;
        [ObservableProperty] private bool _backupBeforeSave;
        [ObservableProperty] private int _backupsKept;
        [ObservableProperty] private string _backupFolder;

        public string DefaultBackupFolder => DirectoryHelper.BackupDirectory;

        public SettingsViewModel(ApplicationSettingsService settingsService, LocalizationManager localizationManager)
        {
            _settingsService = settingsService;
            _localizationManager = localizationManager;

            AvailableLangauges = new ObservableCollection<string>(_localizationManager.GetPossibleLanguages());
            SelectedLanguage = _localizationManager.SelectedLangauge;

            AvailableThemes = new ObservableCollection<ThemeType>((ThemeType[])Enum.GetValues(typeof(ThemeType)));
            CurrentTheme = _settingsService.CurrentSettings.Theme;
            RenderEngineBackgroundColours = new ObservableCollection<BackgroundColour>((BackgroundColour[])Enum.GetValues(typeof(BackgroundColour)));
            CurrentRenderEngineBackgroundColour = _settingsService.CurrentSettings.RenderEngineBackgroundColour;
            CurrentTextEditorBackgroundColour = _settingsService.CurrentSettings.TextEditorBackgroundColour;
            VisualEditorsGridSize = _settingsService.CurrentSettings.VisualEditorsGridSize;
            VertexSelectionColour = new ColourPickerViewModel(_settingsService.CurrentSettings.VertexSelectionColour);
            CameraModes = new ObservableCollection<CameraControlMode>((CameraControlMode[])Enum.GetValues(typeof(CameraControlMode)));

            StartMaximised = _settingsService.CurrentSettings.StartMaximised;
            ShowStartPanel = _settingsService.CurrentSettings.ShowStartPanel;
            Games = new ObservableCollection<GameTypeEnum>(GameInformationDatabase.Games.Values.OrderBy(game => game.DisplayName).Select(game => game.Type));
            CurrentGame = _settingsService.CurrentSettings.CurrentGame;
            LoadCaPacksByDefault = _settingsService.CurrentSettings.LoadCaPacksByDefault;
            ShowCAWemFiles = _settingsService.CurrentSettings.ShowCAWemFiles;
            OnlyLoadLod0ForReferenceMeshes = _settingsService.CurrentSettings.OnlyLoadLod0ForReferenceMeshes;
            PreviewTintCapability = _settingsService.CurrentSettings.PreviewTintCapability;
            SelectedCameraMode = _settingsService.CurrentSettings.CameraControlMode;
            AutosaveMinutes = _settingsService.CurrentSettings.AutosaveMinutes;
            BackupBeforeSave = _settingsService.CurrentSettings.BackupBeforeSave;
            BackupsKept = _settingsService.CurrentSettings.BackupsKept;
            BackupFolder = _settingsService.CurrentSettings.BackupFolder;

            foreach (var game in GameInformationDatabase.Games.Values.OrderBy(game => game.DisplayName))
            {
                GameDirectores.Add(
                    new GamePathItem()
                    {
                        GameName = $"{game.DisplayName}",
                        GameType = game.Type,
                        Path = _settingsService.CurrentSettings.GameDirectories.FirstOrDefault(x => x.Game == game.Type)?.Path
                    });
            }
        }


        [RelayCommand]
        private void Save()
        {
            // The game's packs and every window's text load once, at startup; the rest of these settings is read as it is used.
            var settings = _settingsService.CurrentSettings;
            var gamePath = settings.GameDirectories.FirstOrDefault(x => x.Game == CurrentGame)?.Path ?? "";
            var restart = settings.CurrentGame != CurrentGame
                || settings.SelectedLangauge != SelectedLanguage
                || gamePath != (GameDirectores.FirstOrDefault(x => x.GameType == CurrentGame)?.Path ?? "");
            if (settings.Theme != CurrentTheme)
                ThemesController.SetTheme(CurrentTheme);

            _settingsService.CurrentSettings.Theme = CurrentTheme;
            _settingsService.CurrentSettings.RenderEngineBackgroundColour = CurrentRenderEngineBackgroundColour;
            _settingsService.CurrentSettings.TextEditorBackgroundColour = CurrentTextEditorBackgroundColour;
            global::Shared.Ui.Editors.TextEditor.TextEditorTheme.Apply(CurrentTextEditorBackgroundColour);
            _settingsService.CurrentSettings.VisualEditorsGridSize = VisualEditorsGridSize;
            _settingsService.CurrentSettings.VertexSelectionColour = VertexSelectionColour.SelectedColour;
            _settingsService.CurrentSettings.StartMaximised = StartMaximised;
            _settingsService.CurrentSettings.ShowStartPanel = ShowStartPanel;
            _settingsService.CurrentSettings.CurrentGame = CurrentGame;
            _settingsService.CurrentSettings.LoadCaPacksByDefault = LoadCaPacksByDefault;
            _settingsService.CurrentSettings.ShowCAWemFiles = ShowCAWemFiles;
            _settingsService.CurrentSettings.SelectedLangauge = SelectedLanguage;
            _settingsService.CurrentSettings.OnlyLoadLod0ForReferenceMeshes = OnlyLoadLod0ForReferenceMeshes;
            _settingsService.CurrentSettings.PreviewTintCapability = PreviewTintCapability;
            _settingsService.CurrentSettings.GameDirectories.Clear();
            _settingsService.CurrentSettings.CameraControlMode = SelectedCameraMode;
            _settingsService.CurrentSettings.AutosaveMinutes = Math.Max(0, AutosaveMinutes);
            _settingsService.CurrentSettings.BackupBeforeSave = BackupBeforeSave;
            _settingsService.CurrentSettings.BackupsKept = Math.Max(1, BackupsKept);
            _settingsService.CurrentSettings.BackupFolder = BackupFolder?.Trim() ?? "";

            foreach (var item in GameDirectores)
                _settingsService.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair(item.GameType, item.Path));
           
            _localizationManager.LoadLanguage(SelectedLanguage);
            _settingsService.Save();
            if (restart)
                MessageBox.Show(_localizationManager.Get("SettingsWindow.RestartNeeded"));
        }

        [RelayCommand]
        private void BrowseBackupFolder()
        {
            var dialog = new FolderBrowserDialog() { SelectedPath = string.IsNullOrWhiteSpace(BackupFolder) ? DefaultBackupFolder : BackupFolder };
            if (dialog.ShowDialog() == DialogResult.OK)
                BackupFolder = dialog.SelectedPath;
        }
    }

    class GamePathItem : NotifyPropertyChangedImpl
    {
        public GameTypeEnum GameType { get; set; }

        string _gameName;
        public string GameName { get => _gameName; set => SetAndNotify(ref _gameName, value); }

        string _path;
        public string Path { get => _path; set => SetAndNotify(ref _path, value); }

        public ICommand BrowseCommand { get; set; }

        public GamePathItem()
        {
            BrowseCommand = new RelayCommand(OnBrowse);
        }

        void OnBrowse()
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Path = dialog.SelectedPath;
                var files = Directory.GetFiles(Path);
                var packFiles = files.Count(x => System.IO.Path.GetExtension(x) == ".pack");
                var manifest = files.Count(x => x.Contains("manifest.txt"));

                if (packFiles == 0 && manifest == 0)
                    System.Windows.MessageBox.Show($"The selected directory contains {packFiles} packfiles and {manifest} manifest files. It is probably not a game directory");
            }
        }
    }
}
