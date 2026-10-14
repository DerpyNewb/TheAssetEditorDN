using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.RenderSettingsDialog;
using GameWorld.Core.WpfWindow;
using Shared.Core.Misc;
using Shared.Core.ToolCreation;

namespace Editors.Shared.Core.Common.BaseControl
{
    public abstract partial class EditorHostBase : ObservableObject, IEditorInterface, IEditorViewModelTypeProvider
    {
        private readonly FocusSelectableObjectService _focusSelectableObjectService;
        private readonly IEditorHostParameters _inputParams;
        protected readonly SceneObjectViewModelBuilder _sceneObjectViewModelBuilder;
        protected readonly SceneObjectEditor _sceneObjectEditor;

        protected FocusSelectableObjectService FocusService => _focusSelectableObjectService;
        protected SceneObjectEditor SceneObjectEditor => _sceneObjectEditor;

        public string DisplayName { get; set; } = "Not set";
        public abstract Type EditorViewModelType { get; }

        [ObservableProperty] IWpfGame? _gameWorld;
        [ObservableProperty] ObservableCollection<SceneObjectViewModel> _sceneObjects = new();
        [ObservableProperty] AnimationPlayerViewModel _player;
        [ObservableProperty] GridLength _leftColumnWidth = new(0.75, GridUnitType.Star);
        [ObservableProperty] GridLength _rightColumnWidth = new(0.25, GridUnitType.Star);

        // The right column's split between the scene content and the editor's own panel, as the splitter between them leaves it.
        // Collapsed, the scene content is only its header and the panel takes the rest; opened, it has the height it was left at.
        [ObservableProperty, NotifyPropertyChangedFor(nameof(SceneContentHeight))] bool _sceneContentExpanded = true;
        [ObservableProperty] GridLength _editorPanelHeight = new(3, GridUnitType.Star);
        GridLength _sceneContentOpenHeight = new(2, GridUnitType.Star);

        public GridLength SceneContentHeight
        {
            get => SceneContentExpanded ? _sceneContentOpenHeight : GridLength.Auto;
            set
            {
                _sceneContentOpenHeight = value;
                OnPropertyChanged();
            }
        }

        public EditorHostBase(IEditorHostParameters inputParams)
        {
            GameWorld = inputParams.GameWorld;
            Player = inputParams.AnimationPlayerViewModel;

            _focusSelectableObjectService = inputParams.FocusSelectableObjectService;
            _inputParams = inputParams;
            _sceneObjectViewModelBuilder = inputParams.SceneObjectViewModelBuilder;
            _sceneObjectEditor = inputParams.SceneObjectEditor;

            inputParams.ComponentInserter.Execute();
        }

        [RelayCommand] void ResetCamera() => _focusSelectableObjectService.ResetCamera();
        [RelayCommand] void FocusCamera() => _focusSelectableObjectService.FocusSelection();

        /// <summary>Where a screenshot goes, and whether that folder opens after.</summary>
        public string ScreenshotFolder { get; set; } = Path.Combine(DirectoryHelper.ApplicationDirectory, "PhotoStudio", "Screenshots");
        public bool OpenScreenshotFolder { get; set; } = true;

        /// <summary>A screenshot's name, before its number.</summary>
        protected virtual string ScreenshotName => DisplayName;

        /// <summary>The Kitbash editor's render settings: the light's intensity, colour and turn, which lights the models and lit
        /// smoke, the environment's turn, and the grid.</summary>
        [RelayCommand]
        void OpenRenderSettings() => new RenderSettingsWindow(_inputParams.RenderEngine, _inputParams.SceneParameters, _inputParams.Grid) { Owner = Application.Current.MainWindow }.ShowDialog();

        /// <summary>The next frame at twice the view's size, as PhotoStudio takes one.</summary>
        [RelayCommand]
        void TakeScreenshot() => _inputParams.RenderEngine.SaveNextFrame(new SaveRenderImageSettings(ScreenshotName, OpenScreenshotFolder, false, 2, ScreenshotFolder));

        public virtual void Close()
        {
            GameWorld = null;
        }

        /// <summary>The parts a model dropped on the 3D view can go into: those whose panel is shown.</summary>
        public IReadOnlyList<SceneObjectViewModel> ModelDropTargets => SceneObjects.Where(x => x.IsControlVisible && x.IsEnabled).ToList();

        public SceneObjectViewModel? GetSceneObjectFromId(string id)
        {
            var view = SceneObjects.FirstOrDefault(x => x.Data.Id == id);
            return view;
        }
    }
}
