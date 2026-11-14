using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.ChildEditors.PinTool.UiCommands;
using Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.SaveDialog
{
    /// <summary>One problem in the list, with the button for its fix when it has one.</summary>
    public record SaveCheckRow(ErrorListDataItem Problem, string? FixLabel, string? FixToolTip, ICommand? Fix)
    {
        public bool HasFix => Fix != null;
    }

    /// <summary>
    /// The save check's problems, each with the fix it offers. A save shows them in a modal window that asks whether to save
    /// anyway; Check model shows them in a modeless one. Both check again whenever an edit lands on the undo stack, so a fix,
    /// or an edit made in the tool a fix opened, takes its problem off the list.
    /// </summary>
    public partial class SaveCheckViewModel : ObservableObject, IDisposable
    {
        readonly SaveValidatorService _validator;
        readonly GeometrySaveSettings _settings;
        readonly IUiCommandFactory _commandFactory;
        readonly IEventHub _eventHub;
        readonly LocalizationManager _localization;
        MainEditableNode? _mainNode;

        public ObservableCollection<SaveCheckRow> Problems { get; } = [];
        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsForCheck), nameof(Title))] public partial bool IsForSave { get; private set; }
        public bool IsForCheck => !IsForSave;
        public string Title => _localization.Get(IsForSave ? "KitbashTool.SaveCheck.SaveTitle" : "KitbashTool.SaveCheck.Title");
        [ObservableProperty] public partial bool NothingFound { get; private set; }

        /// <summary>Set by Save anyway. A save goes ahead only when this is true.</summary>
        public bool SaveAnyway { get; private set; }

        public event Action? CloseRequested;

        /// <summary>A tool a fix asked for during a save, to open once the modal window has closed and the main window answers again.</summary>
        public Action? AfterClose { get; private set; }

        // What the fixes for vertices with no weights and for a texture in no pack open. A test puts its own here, because the
        // tools' windows are AssetEditorWindows, which need the app's main window.
        public Action<IReadOnlyList<Rmv2MeshNode>> OpenPinToolToBind { get; set; }
        public Action<string> OpenReplaceTexturePaths { get; set; }

        public SaveCheckViewModel(SaveValidatorService validator, GeometrySaveSettings settings, IUiCommandFactory commandFactory, IEventHub eventHub, LocalizationManager localization)
        {
            _validator = validator;
            _settings = settings;
            _commandFactory = commandFactory;
            _eventHub = eventHub;
            _localization = localization;
            OpenPinToolToBind = meshes => _commandFactory.Create<OpenPinToolCommand>().OpenToBind(meshes);
            OpenReplaceTexturePaths = path => _commandFactory.Create<OpenReplaceTexturePathsCommand>().Open(path);
        }

        public void Initialize(MainEditableNode mainNode, ErrorList problems, bool forSave)
        {
            _mainNode = mainNode;
            IsForSave = forSave;
            // A selection is a step on the undo stack too, but it changes nothing the check reads.
            _eventHub.Register<CommandStackChangedEvent>(this, x => { if (x.IsMutation) Refresh(); });
            _eventHub.Register<CommandStackUndoEvent>(this, _ => Refresh());
            Show(problems);
        }

        [RelayCommand]
        public void Refresh()
        {
            if (_mainNode != null)
                Show(_validator.Validate(_mainNode, _settings));
        }

        void Show(ErrorList problems)
        {
            Problems.Clear();
            foreach (var problem in problems.Errors.OrderByDescending(x => x.IsError))
                Problems.Add(CreateRow(problem));
            NothingFound = Problems.Count == 0;
        }

        SaveCheckRow CreateRow(ErrorListDataItem problem)
        {
            var fix = (problem as SaveProblem)?.Fix ?? SaveFix.None;
            var mesh = (problem as SaveProblem)?.Mesh;
            Action? action = fix switch
            {
                SaveFix.NormaliseWeights => () => NormaliseWeights(mesh!),
                SaveFix.BindToBone => () => OpenTool(() => OpenPinToolToBind([mesh!])),
                SaveFix.ReplaceTexturePath => () => OpenTool(() => OpenReplaceTexturePaths(problem.ItemName)),
                _ => null,
            };
            if (action == null)
                return new SaveCheckRow(problem, null, null, null);

            var key = $"KitbashTool.SaveCheck.Fix.{fix}";
            return new SaveCheckRow(problem, _localization.Get(key), _localization.Get(key + ".Tooltip"), new RelayCommand(action));
        }

        // Only the vertices the check flags. Normalising every vertex, as the save does, would also move CA's two-weight vertices
        // that store only their first weight, which the check passes.
        void NormaliseWeights(Rmv2MeshNode mesh) =>
            _commandFactory.CreateWithBuilder<EditMeshesCommand>()
                .Configure(x => x.Configure([mesh], "Normalise weights", edited =>
                {
                    var weightCount = edited.Geometry.WeightCount;
                    var vertices = edited.Geometry.VertexArray;
                    for (var i = 0; i < vertices.Length; i++)
                    {
                        if (SaveValidatorService.IsUnbalanced(vertices[i], weightCount))
                            vertices[i].BlendWeights = MeshBuilderService.NormalizedWeights(vertices[i].BlendWeights, weightCount);
                    }
                }))
                .BuildAndExecute();

        // During a save the window is modal, and the tool needs the main window to select meshes in, so the save is cancelled
        // and the tool opens once the window has closed.
        void OpenTool(Action open)
        {
            if (IsForSave == false)
            {
                open();
                return;
            }

            AfterClose = open;
            SaveAnyway = false;
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        void ConfirmSave()
        {
            SaveAnyway = true;
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        void Close() => CloseRequested?.Invoke();

        public void Dispose() => _eventHub.UnRegister(this);
    }
}
