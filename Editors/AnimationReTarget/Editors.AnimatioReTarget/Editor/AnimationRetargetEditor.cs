using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.Saving;
using Editors.AnimatioReTarget.Editor.Settings;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.AnimatioReTarget.Editor
{
    // When applying proportion scling, should also rotation be scaled? 
    // Show scale factor in view for each bone 


    // Note!
    // When the test config AE-AnimTraser the followin values are applied:
    // Target (The one that gets the animation)  = Dwarf
    // Source (The one we take animation from)   = Human with animation hu1_swp_missile_attack_aim_to_shootready_01
    // Setting target(dwarf) updates generated 

    public static class AnimationRetargetIds
    {
        public static string Target => "Target";
        public static string Source => "Source";
        public static string Generated => "Generated";
    }

    public partial class AnimationRetargetEditor : EditorHostBase, IDisposable
    {
        // private readonly ILogger _logger = Logging.Create<AnimationTransferToolViewModel>();

        private readonly IPackFileService _pfs;
        private readonly AnimationPlayerViewModel _player;
        private readonly IStandardDialogs _standardDialogs;
        private readonly IEventHub _eventHub;

        private SceneObject _target;
        private SceneObject _source;
        private SceneObject _generated;

        [ObservableProperty] BoneManager _boneManager;
        [ObservableProperty] AnimationReTargetRenderingComponent _rendering;
        [ObservableProperty] BoneOffsetHandle _offsetHandle;
        [ObservableProperty] AnimationGenerationSettings _settings;
        [ObservableProperty] SaveManager _saveManager;


        public override Type EditorViewModelType => typeof(EditorView);
        public override global::Shared.Core.ToolCreation.EditorEnums LayoutKey => global::Shared.Core.ToolCreation.EditorEnums.AnimationRetarget_Editor;

        public AnimationRetargetEditor(
            IEditorHostParameters editorHostParameters,
            AnimationPlayerViewModel player,
            SceneObjectViewModelBuilder referenceModelSelectionViewModelBuilder,
            IStandardDialogs standardDialogs,

            IEventHub eventHub,
            SaveManager saveManager,
            BoneManager boneManager,
            AnimationReTargetRenderingComponent renderingComponent,
            BoneOffsetHandle offsetHandle,
            IPackFileService pfs) : base(editorHostParameters)
        {
            DisplayName = "Animation transfer tool";

            _settings = new AnimationGenerationSettings();
            _boneManager = boneManager;
            _boneManager.MappingApplied += () =>
            {
                if (CanUpdateAnimation(out _))
                    UpdateAnimation();
            };
            _history = new(TakeSnapshot, (a, b) => a.Preset == b.Preset, Restore);
            _boneManager.MappingApplied += Checkpoint;
            // Live: every setting regenerates the clip, with no Apply.
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _boneManager.SettingsChanged += ScheduleUpdate;
            _settings.PropertyChanged += (_, _) => ScheduleUpdate();
            _rendering = renderingComponent;
          
            _pfs = pfs;
            _player = player;
            _standardDialogs = standardDialogs;
            _eventHub = eventHub;
            _saveManager = saveManager;

            GameWorld.AddComponent(renderingComponent);
            _offsetHandle = offsetHandle;
            GameWorld.AddComponent(offsetHandle);
            offsetHandle.DragEnded += Checkpoint;

            _eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);

            Initialize();
        }

        void Initialize()
        {
            var target = _sceneObjectViewModelBuilder.CreateAsset(AnimationRetargetIds.Target, true, "Target", Color.Black, null);
            var source = _sceneObjectViewModelBuilder.CreateAsset(AnimationRetargetIds.Source, true, "Source", Color.Black, null);
            var generated = _sceneObjectViewModelBuilder.CreateAsset(AnimationRetargetIds.Generated, true, "Generated", Color.Black, null);
            _target = target.Data;
            _source = source.Data;
            _generated = generated.Data;

            generated.IsExpanded = false;
            generated.IsEnabled = false;

            SceneObjects.Add(target);
            SceneObjects.Add(source);
            SceneObjects.Add(generated);

            BoneManager.SetSceneNodes(source.Data, target.Data, generated.Data);
            Rendering.SetSceneNodes(source.Data, target.Data, generated.Data);
            SaveManager.SetSceneNodes(source.Data, target.Data, generated.Data);
            SaveManager.Remap = UpdateAnimation;
            SaveManager.SkeletonScale = () => Settings.SkeletonScale;
            OffsetHandle.Follow(generated.Data, () => Settings.SkeletonScale);
            // Another source animation is retargeted with the mapping and settings already made.
            _source.AnimationChanged += _ => ScheduleUpdate();
        }

        private void OnSceneObjectUpdated(SceneObjectUpdateEvent e)
        {
           if (e.Owner == _target && e.MeshChanged)
               _sceneObjectEditor.CopyMeshFromOther(_generated, _target);

            if (e.Owner == _source && e.SkeletonChanged)
                BoneManager.UpdateSourceSkeleton(_source.SkeletonName.Value);
           
            if (e.Owner == _target && e.SkeletonChanged)
                BoneManager.UpdateTargetSkeleton(_target.SkeletonName.Value);

            // Another skeleton is another bone tree: the steps made on the last one do not apply to it.
            if (e.SkeletonChanged)
                _history.Reset();

            Rendering.ComputeOffsets();
        }

        public void LoadData(AnimationToolInput targetInput, AnimationToolInput sourceInput)
        {
            _sceneObjectEditor.SetMesh(_target, targetInput.Mesh);

            _sceneObjectEditor.SetMesh(_source, sourceInput.Mesh);
            _sceneObjectEditor.SetAnimation(_source, _pfs.GetFullPath(sourceInput.Animation));
        }

        readonly System.Windows.Threading.Dispatcher _dispatcher;
        bool _updatePending;

        /// <summary>
        /// One remap once the current change is in: "Clear selected bone settings" changes eleven values, and a typed number
        /// changes on every key, so changes made together are one regeneration rather than one each.
        /// </summary>
        void ScheduleUpdate()
        {
            if (_updatePending)
                return;
            _updatePending = true;
            _dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                _updatePending = false;
                if (CanUpdateAnimation(out _))
                    UpdateAnimation();
            });
        }

        RetargetPreset CurrentPreset() => new(1, _source.SkeletonName.Value, _target.SkeletonName.Value,
            new(Settings.SkeletonScale, Settings.AnimationSpeedMult, Settings.ApplyRelativeScale, Settings.ZeroUnmappedBones),
            BoneManager.PresetBones());

        /// <summary>Puts a preset's settings and bones on the editor. Returns the names it could not place.</summary>
        List<string> ApplyPreset(RetargetPreset preset)
        {
            Settings.SkeletonScale = preset.Settings.SkeletonScale;
            Settings.AnimationSpeedMult = preset.Settings.AnimationSpeedMult;
            Settings.ApplyRelativeScale = preset.Settings.ApplyRelativeScale;
            Settings.ZeroUnmappedBones = preset.Settings.ZeroUnmappedBones;
            return BoneManager.ApplyPresetBones(preset.Bones);
        }

        public void SavePreset(string path) =>
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(CurrentPreset(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        // Undo keeps the mapping and every setting as a preset, and the selected bone. A step is recorded when a mapping is
        // applied (the mapping window, a loaded preset) and when a handle drag ends, and, from the view, when a mouse button is
        // let go or a field is left; a checkpoint where nothing changed records nothing.
        record Snapshot(string Preset, string? SelectedBone);
        readonly SnapshotUndo<Snapshot> _history;

        Snapshot TakeSnapshot() => new(System.Text.Json.JsonSerializer.Serialize(CurrentPreset()), BoneManager.SelectedBone?.BoneName);

        // Selects the bone the step was made in; the snapshot an undo arrives at holds whatever was selected before it.
        void Restore(Snapshot snapshot, Snapshot madeIn)
        {
            ApplyPreset(System.Text.Json.JsonSerializer.Deserialize<RetargetPreset>(snapshot.Preset)!);
            if (madeIn.SelectedBone != null && SkeletonBoneNodeHelper.GetNodeFromName(madeIn.SelectedBone, BoneManager.Bones) is { } bone)
                BoneManager.SelectedBone = bone;
        }

        /// <summary>Records the mapping and the settings as one undo step, if they changed since the last.</summary>
        public void Checkpoint() => _history.Checkpoint();

        [RelayCommand] public void Undo() => _history.Undo();
        [RelayCommand] public void Redo() => _history.Redo();

        /// <summary>Loads a preset onto the current skeletons. Returns what it could not place, or null when everything was placed.</summary>
        public string? LoadPreset(string path)
        {
            var preset = System.Text.Json.JsonSerializer.Deserialize<RetargetPreset>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The file holds no preset.");
            if (preset.Settings == null || preset.Bones == null
                || preset.Bones.Any(x => x?.Name == null || x.RotationOffset?.Length != 3 || x.TranslationOffset?.Length != 3))
                throw new InvalidDataException("The file is not a complete retarget preset.");
            var missing = ApplyPreset(preset);
            if (missing.Count == 0)
                return null;
            const int Shown = 20;
            return $"{missing.Count} names in the preset are not on these skeletons and were skipped:\n\n"
                + string.Join("\n", missing.Take(Shown)) + (missing.Count > Shown ? $"\n...and {missing.Count - Shown} more" : "");
        }

        [RelayCommand] void SavePresetToFile()
        {
            if (BoneManager.Bones.Count == 0)
            {
                _standardDialogs.ShowDialogBox("Pick a target model first: a preset holds the settings of its bones.");
                return;
            }
            var name = $"{Path.GetFileNameWithoutExtension(_source.SkeletonName.Value)}_to_{Path.GetFileNameWithoutExtension(_target.SkeletonName.Value)}.retarget.json";
            var result = _standardDialogs.ShowSystemSaveFileDialog(name, "Retarget preset (*.retarget.json)|*.retarget.json", ".json");
            if (result.Result == false || string.IsNullOrWhiteSpace(result.FilePath))
                return;
            try
            {
                SavePreset(result.FilePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _standardDialogs.ShowDialogBox($"Could not save {result.FilePath}: {e.Message}");
            }
        }

        [RelayCommand] void LoadPresetFromFile()
        {
            if (BoneManager.Bones.Count == 0)
            {
                _standardDialogs.ShowDialogBox("Pick the source and target models first: a preset is put on their bones by name.");
                return;
            }
            var result = _standardDialogs.ShowSystemOpenFileDialog(false, "Retarget preset (*.retarget.json)|*.retarget.json|All files|*.*");
            if (result.Result == false || result.FilePaths.Count == 0)
                return;
            var path = result.FilePaths[0];
            try
            {
                var report = LoadPreset(path);
                if (report != null)
                    _standardDialogs.ShowDialogBox(report, "Preset loaded");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                _standardDialogs.ShowDialogBox($"Could not read {path} as a retarget preset: {e.Message}");
            }
        }

        [RelayCommand]public void UpdateAnimation()
        {
            var canUpdate = CanUpdateAnimation(out var errorText);
            if (canUpdate == false)
            {
               _standardDialogs.ShowDialogBox(errorText);
                return;
            }

            var newAnimationClip = UpdateAnimation(_source.AnimationClip);
            _sceneObjectEditor.SetAnimationClip(_generated, newAnimationClip, "Generated");
            _player.SelectedMainAnimation = _player.PlayerItems.First(x => x.Asset == _generated);
            
        }

        AnimationClip UpdateAnimation(AnimationClip animationToCopy)
        { 
            var service = new AnimationRemapperService(Settings, BoneManager.Bones);
            var newClip = service.ReMapAnimation(_source.Skeleton, _target.Skeleton, animationToCopy);
            return newClip;
        }

        public bool CanUpdateAnimation(out string errorText)
        {
            if (_target.Skeleton == null || _source.Skeleton == null)
            {
                errorText = "Missing a skeleton?";
                return false;
            }

            if (_source.AnimationClip == null)
            {
                errorText = "No animation to copy selected";
                return false;
            }

            errorText = string.Empty;
            return true;
        }

        public void Dispose()
        {
            _eventHub?.UnRegister(this);
        }
    }
}
