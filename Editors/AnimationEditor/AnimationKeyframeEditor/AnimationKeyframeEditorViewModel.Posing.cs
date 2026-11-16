using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Ui.Common;

namespace Editors.AnimationVisualEditors.AnimationKeyframeEditor
{
    /// <summary>One pinned bone in the panel's list, with the bone its chain runs up to.</summary>
    public partial class PinRow(string boneName, IReadOnlyList<string> endBoneOptions, string endBoneName, Action<PinRow> endBoneChanged) : ObservableObject
    {
        public string BoneName { get; } = boneName;
        public IReadOnlyList<string> EndBoneOptions { get; } = endBoneOptions;

        public string EndBoneName
        {
            get => _endBoneName;
            set
            {
                if (SetProperty(ref _endBoneName, value))
                    endBoneChanged(this);
            }
        }
        string _endBoneName = endBoneName;
    }

    // The posing kit: joint limits measured from CA's clips, the bend-direction slider, pins and mirror.
    public partial class AnimationKeyframeEditorViewModel
    {
        const string BendHint = "Bend direction";

        readonly List<Pin> _pins = new();
        JointLimits? _measuredLimits;
        CancellationTokenSource? _measuring;
        readonly EditCoalescer _bendEdits = new();
        ReplaceFramesCommand? _lastBendCommand;
        float _bendStepStart;
        float _lastBendValue;
        bool _resettingBend;
        int _bendFrame = -1;

        public NotifyAttr<bool> UseJointLimits { get; set; } = new(true);
        public NotifyAttr<string> JointLimitStatus { get; set; } = new("");
        public NotifyAttr<bool> IsMeasuringJointLimits { get; set; } = new(false);

        /// <summary>The load or measure of the current skeleton's limits; finished once the status line is final.</summary>
        public Task JointLimitsTask { get; private set; } = Task.CompletedTask;

        public ObservableCollection<PinRow> PinRows { get; } = new();

        public PinRow? SelectedPinRow { get => _selectedPinRow; set => SetProperty(ref _selectedPinRow, value); }
        PinRow? _selectedPinRow;

        /// <summary>How far the selected limb has been turned since the slider was last at 0.</summary>
        public float BendDegrees
        {
            get => _bendDegrees;
            set
            {
                if (SetProperty(ref _bendDegrees, value) && !_resettingBend)
                    ApplyBend();
            }
        }
        float _bendDegrees;

        JointLimits? ActiveJointLimits => UseJointLimits.Value ? _measuredLimits : null;

        /// <summary>The current skeleton's limits, before the checkbox; setting them hands them to the bone selection.</summary>
        internal JointLimits? MeasuredJointLimits
        {
            get => _measuredLimits;
            set
            {
                _measuredLimits = value;
                PushPosingState();
            }
        }

        void InitializePosing()
        {
            UseJointLimits.PropertyChanged += (_, _) => PushPosingState();
        }

        /// <summary>Hands the limits and pins to the bone selection, which the gizmo's drag command reads.</summary>
        internal void PushPosingState(BoneSelectionState state)
        {
            state.JointLimits = ActiveJointLimits;
            state.Pins = _pins;
        }

        void PushPosingState()
        {
            if (SelectionManager.GetState() is BoneSelectionState state)
                PushPosingState(state);
        }

        int SingleSelectedBone() =>
            SelectionManager.GetState() is BoneSelectionState { SelectedBones.Count: 1 } state ? state.SelectedBones[0] : -1;

        bool HasRange(out int a, out int b)
        {
            a = _interpolateBetweenPose.KeyFrameNrA;
            b = _interpolateBetweenPose.KeyFrameNrB;
            var count = _rider.AnimationClip?.DynamicFrames.Count ?? 0;
            return a >= 0 && b >= 0 && a < count && b < count && int.TryParse(SelectedFrameAInterpolation.Value, out _) && int.TryParse(SelectedFrameBInterpolation.Value, out _);
        }

        ReplaceFramesCommand ExecuteFrameEdit(AnimationClip clip, IReadOnlyList<int> frames, Action<AnimationClip> edit, string hint)
        {
            var command = new ReplaceFramesCommand();
            command.Configure(clip, frames, edit, hint);
            _commandExecutor.ExecuteCommand(command);
            IsDirty.Value = _commandExecutor.CanUndo();
            return command;
        }

        // ---- Joint limits ----

        void LoadJointLimits(GameSkeleton? skeleton, bool remeasure = false)
        {
            _measuring?.Cancel();
            _measuring = null;
            if (!remeasure)
            {
                MeasuredJointLimits = null;
            }
            if (skeleton == null)
            {
                JointLimitStatus.Value = "";
                JointLimitsTask = Task.CompletedTask;
                return;
            }

            var cancel = new CancellationTokenSource();
            _measuring = cancel;
            JointLimitsTask = LoadJointLimitsAsync(skeleton, cancel, remeasure);
        }

        async Task LoadJointLimitsAsync(GameSkeleton skeleton, CancellationTokenSource cancel, bool remeasure)
        {
            try
            {
                var limits = remeasure ? null : await Task.Run(() => _jointLimitStore.TryLoad(skeleton), cancel.Token);
                if (limits == null)
                {
                    IsMeasuringJointLimits.Value = true;
                    JointLimitStatus.Value = "Measuring joint limits from CA's clips...";
                    var progress = new Progress<int>(read =>
                    {
                        if (!cancel.IsCancellationRequested)
                            JointLimitStatus.Value = $"Measuring joint limits: {read} clips read";
                    });
                    limits = await _jointLimitStore.MeasureAsync(skeleton, progress, cancel.Token);
                }
                if (cancel.IsCancellationRequested)
                    return;

                _measuredLimits = limits;
                JointLimitStatus.Value = limits == null
                    ? $"No CA clips for {skeleton.SkeletonName}, so IK is unlimited."
                    : $"Joint limits for {limits.Limits.Count} bones of {skeleton.SkeletonName}, measured from CA's clips.";
                PushPosingState();
            }
            catch (OperationCanceledException)
            {
                if (_measuring == cancel || _measuring == null)
                    JointLimitStatus.Value = "Measuring was cancelled, so IK is unlimited.";
            }
            catch (Exception e)
            {
                JointLimitStatus.Value = $"Joint limits could not be measured: {e.Message}";
            }
            finally
            {
                if (_measuring == cancel || _measuring == null)
                    IsMeasuringJointLimits.Value = false;
            }
        }

        public void RemeasureJointLimits() => LoadJointLimits(_skeleton, remeasure: true);

        public void CancelMeasuring()
        {
            _measuring?.Cancel();
            _measuring = null;
        }

        /// <summary>Frees the selected bone from its limit, for this skeleton from now on.</summary>
        public void WidenSelectedBoneLimit()
        {
            var bone = SingleSelectedBone();
            if (bone < 0 || _skeleton == null)
            {
                MessageBox.Show("Select one bone first.", "Joint limits", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _jointLimitStore.SaveOverride(_skeleton, bone, null);
            MeasuredJointLimits = _measuredLimits?.WithOverride(bone, null);
        }

        /// <summary>Puts the selected bone's measured limit back.</summary>
        public void ResetSelectedBoneLimit()
        {
            var bone = SingleSelectedBone();
            if (bone < 0 || _skeleton == null)
            {
                MessageBox.Show("Select one bone first.", "Joint limits", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _jointLimitStore.ClearOverride(_skeleton, bone);
            MeasuredJointLimits = _jointLimitStore.TryLoad(_skeleton) ?? _measuredLimits;
        }

        // ---- Bend direction ----

        void ApplyBend()
        {
            var clip = _rider.AnimationClip;
            var bone = SingleSelectedBone();
            if (clip == null || _skeleton == null || bone < 0 || !EnableInverseKinematics.Value || !LimbSwivel.TryGetChain(_skeleton, bone, out _, out _))
                return;

            var frameNr = _rider.Player.CurrentFrame;
            if (_bendEdits.Continues((frameNr, bone)) && _lastBendCommand != null && ReferenceEquals(_commandExecutor.Peek(), _lastBendCommand))
                _commandExecutor.Undo(keepForRedo: false);
            else
                _bendStepStart = _lastBendValue;

            var angle = MathHelper.ToRadians(_bendDegrees - _bendStepStart);
            var skeleton = _skeleton;
            var limits = ActiveJointLimits;
            var applied = angle;
            _lastBendCommand = ExecuteFrameEdit(clip, [frameNr], c => applied = LimbSwivel.Apply(c.DynamicFrames[frameNr], skeleton, bone, angle, limits), BendHint);
            // A limit can stop the turn short; the next step must start from where the limb is, not where the slider is.
            _lastBendValue = _bendStepStart + MathHelper.ToDegrees(applied);
        }

        /// <summary>Back to 0 without turning anything: on a new frame, a new selection, or an undo the slider no longer describes.</summary>
        internal void ResetBend()
        {
            _resettingBend = true;
            BendDegrees = 0;
            _resettingBend = false;
            _lastBendValue = 0;
            _bendStepStart = 0;
            _lastBendCommand = null;
            _bendEdits.Break();
        }

        // ---- Pins ----

        public void PinSelectedBone()
        {
            var clip = _rider.AnimationClip;
            var bone = SingleSelectedBone();
            if (clip == null || _skeleton == null || bone < 0)
            {
                MessageBox.Show("Load an animation and select one bone first.", "Pins", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_pins.Any(x => x.Bone == bone))
                return;

            var pin = Pin.Create(clip.DynamicFrames[_rider.Player.CurrentFrame], _skeleton, bone);
            _pins.Add(pin);
            var skeleton = _skeleton;
            var ancestors = new List<string>();
            var firstParent = skeleton.GetParentBoneIndex(bone);
            for (var parent = firstParent == -1 ? -1 : skeleton.GetParentBoneIndex(firstParent); parent != -1 && skeleton.GetParentBoneIndex(parent) != -1; parent = skeleton.GetParentBoneIndex(parent))
                ancestors.Add(skeleton.BoneNames[parent]);
            if (ancestors.Count == 0 && pin.EndBone >= 0)
                ancestors.Add(skeleton.BoneNames[pin.EndBone]);
            PinRows.Add(new PinRow(skeleton.BoneNames[bone], ancestors, pin.EndBone >= 0 ? skeleton.BoneNames[pin.EndBone] : "", PinEndBoneChanged));
            PushPosingState();
        }

        void PinEndBoneChanged(PinRow row)
        {
            if (_skeleton == null)
                return;
            var bone = _skeleton.GetBoneIndexByName(row.BoneName);
            var endBone = _skeleton.GetBoneIndexByName(row.EndBoneName);
            var index = _pins.FindIndex(x => x.Bone == bone);
            if (index >= 0 && endBone >= 0)
                _pins[index] = _pins[index] with { EndBone = endBone };
        }

        public void RemoveSelectedPin()
        {
            if (SelectedPinRow is not { } row || _skeleton == null)
                return;
            var bone = _skeleton.GetBoneIndexByName(row.BoneName);
            _pins.RemoveAll(x => x.Bone == bone);
            PinRows.Remove(row);
        }

        public void ClearPins()
        {
            _pins.Clear();
            PinRows.Clear();
        }

        /// <summary>Holds every pinned bone, on every frame from A to B, where it is on frame A. One undo step.</summary>
        public void HoldPinsAcrossAB()
        {
            var clip = _rider.AnimationClip;
            if (clip == null || _skeleton == null || _pins.Count == 0)
            {
                MessageBox.Show("Pin at least one bone first.", "Pins", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!HasRange(out var a, out var b))
            {
                MessageBox.Show("Set frames A and B first, with the Frame A and Frame B buttons.", "Pins", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Pause();
            var skeleton = _skeleton;
            var pins = _pins.ToList();
            var limits = ActiveJointLimits;
            var frames = Enumerable.Range(Math.Min(a, b), Math.Abs(b - a) + 1).ToList();
            ExecuteFrameEdit(clip, frames, c => pins.ForEach(pin => PinSolver.HoldAcross(c, skeleton, pin, a, b, limits)), "Hold pins across frames");
        }

        /// <summary>Runs a paste, then puts every pinned bone back where it was on that frame before it, as a second undo step.</summary>
        void KeepPinsThrough(Action paste)
        {
            var clip = _rider.AnimationClip;
            if (clip == null || _skeleton == null || _pins.Count == 0)
            {
                paste();
                return;
            }

            var frameNr = _rider.Player.CurrentFrame;
            var before = clip.DynamicFrames[frameNr];
            var skeleton = _skeleton;
            var held = _pins.Where(x => x.Bone < before.Position.Count)
                .Select(x => x with { Target = TransformBoneCommand.WorldTransform(before, skeleton, x.Bone).Translation }).ToList();
            paste();
            var limits = ActiveJointLimits;
            ExecuteFrameEdit(clip, [frameNr], c => PinSolver.Resolve(c.DynamicFrames[frameNr], skeleton, held, [], limits), "Keep pinned bones");
        }

        // ---- Mirror ----

        public void MirrorPose() => Mirror(MirrorMode.Flip);
        public void MirrorLeftOntoRight() => Mirror(MirrorMode.LeftOntoRight);
        public void MirrorRightOntoLeft() => Mirror(MirrorMode.RightOntoLeft);

        void Mirror(MirrorMode mode)
        {
            var clip = _rider.AnimationClip;
            if (clip == null || _skeleton == null)
            {
                MessageBox.Show("Load an animation first.", "Mirror", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (PoseMirror.MirrorAxis(_skeleton) == null)
            {
                MessageBox.Show($"{_skeleton.SkeletonName} has no bones named left and right, so there is nothing to mirror.", "Mirror", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Pause();
            var frames = HasRange(out var a, out var b)
                ? Enumerable.Range(Math.Min(a, b), Math.Abs(b - a) + 1).ToList()
                : [_rider.Player.CurrentFrame];
            var skeleton = _skeleton;
            ExecuteFrameEdit(clip, frames, c => frames.ForEach(x => PoseMirror.Apply(c.DynamicFrames[x], skeleton, mode)), "Mirror pose");
        }
    }
}
