using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands.Bone.Clipboard;
using GameWorld.Core.Components.Selection;
using Newtonsoft.Json;

namespace Editors.AnimationVisualEditors.AnimationKeyframeEditor
{
    /// <summary>One row of the strip: every bone (<see cref="Bone"/> -1), or one bone.</summary>
    public sealed record SheetRow(string Name, int Bone);

    /// <summary>The selected rectangle of the strip, both ends included.</summary>
    public sealed record SheetSelection(int FirstRow, int LastRow, int FirstFrame, int LastFrame);

    /// <summary>Where the strip draws each frame and row, and which frame and row a point is over.</summary>
    public sealed record KeyframeSheetLayout(double FrameWidth)
    {
        public const double HeaderWidth = 140, RulerHeight = 22, RowHeight = 20, MinFrameWidth = 3;

        /// <summary><see cref="RowAt"/> for a point on the frame-number ruler.</summary>
        public const int Ruler = -1;

        /// <summary><see cref="FrameAt"/> for a point on the row names.</summary>
        public const int Header = -1;

        /// <summary>The clip across the width, but never narrower than <see cref="MinFrameWidth"/> a frame.</summary>
        public static KeyframeSheetLayout Fit(double width, int frameCount) =>
            new(frameCount <= 0 ? MinFrameWidth : Math.Max(MinFrameWidth, (width - HeaderWidth) / frameCount));

        public double FrameLeft(int frame) => HeaderWidth + frame * FrameWidth;
        public double FrameCentre(int frame) => FrameLeft(frame) + FrameWidth / 2;
        public double RowTop(int row) => RulerHeight + row * RowHeight;

        /// <summary>The frame under <paramref name="x"/>; past either end of the clip, its first or last frame.</summary>
        public int FrameAt(double x, int frameCount) =>
            x < HeaderWidth ? Header : Math.Clamp((int)Math.Floor((x - HeaderWidth) / FrameWidth), 0, Math.Max(0, frameCount - 1));

        public int RowAt(double y) => y < RulerHeight ? Ruler : (int)Math.Floor((y - RulerHeight) / RowHeight);
    }

    /// <summary>
    /// The Keyframes strip: an "All bones" row and a row for each bone selected in the 3D view, with the clip's frames across.
    /// Cells are selected as a rectangle; Copy puts the selected rows' bones over the selected frames on the clipboard, and
    /// Paste writes them from the current frame on. A selection over several frames is also frames A and B.
    /// </summary>
    public partial class KeyframeSheetViewModel : ObservableObject
    {
        const string NoFrames = "The clipboard has no frames to paste.";

        readonly AnimationKeyframeEditorViewModel _editor;
        (int Row, int Frame)? _anchor;
        (int Row, int Frame) _extent;

        public KeyframeSheetViewModel(AnimationKeyframeEditorViewModel editor) => _editor = editor;

        /// <summary>The Windows clipboard, so a copy pastes into another tab or another AssetEditor; a test swaps both.</summary>
        internal Func<string?> ReadClipboard { get; set; } = ReadWindowsClipboard;
        internal Func<string, bool> WriteClipboard { get; set; } = WriteWindowsClipboard;

        /// <summary>Raised whenever anything the strip draws may have changed.</summary>
        public event Action? Changed;

        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        string _status = "Select frames, then Ctrl+C. Move to a frame and Ctrl+V pastes there.";

        public IReadOnlyList<SheetRow> Rows
        {
            get
            {
                var rows = new List<SheetRow> { new("All bones", -1) };
                var skeleton = _editor.Skeleton;
                if (skeleton == null)
                    return rows;
                // A frame change empties the 3D view's selection, so the rows are the bones last picked there.
                var bones = _editor.SelectionManager?.GetState() is BoneSelectionState { SelectedBones.Count: > 0 } state
                    ? state.SelectedBones
                    : _editor.GetSelectedBones() ?? [];
                rows.AddRange(bones.Where(x => x >= 0 && x < skeleton.BoneCount).Distinct().Order()
                    .Select(x => new SheetRow(skeleton.BoneNames[x], x)));
                return rows;
            }
        }

        public int FrameCount => _editor.Rider?.AnimationClip?.DynamicFrames.Count ?? 0;

        public int CurrentFrame
        {
            get => _editor.Rider?.Player.CurrentFrame ?? 0;
            set
            {
                _editor.GoToFrame(value);
                Refresh();
            }
        }

        /// <summary>The selection, its rows cut to the rows there are now; null when nothing is selected.</summary>
        public SheetSelection? Selection
        {
            get
            {
                if (_anchor is not { } anchor || FrameCount == 0)
                    return null;
                var lastRow = Rows.Count - 1;
                var lastFrame = FrameCount - 1;
                return new SheetSelection(
                    Math.Min(Math.Min(anchor.Row, _extent.Row), lastRow), Math.Min(Math.Max(anchor.Row, _extent.Row), lastRow),
                    Math.Min(Math.Min(anchor.Frame, _extent.Frame), lastFrame), Math.Min(Math.Max(anchor.Frame, _extent.Frame), lastFrame));
            }
        }

        public bool IsSelected(int row, int frame) =>
            Selection is { } s && row >= s.FirstRow && row <= s.LastRow && frame >= s.FirstFrame && frame <= s.LastFrame;

        public void Refresh() => Changed?.Invoke();

        /// <summary>Selects one cell, or with <paramref name="extend"/> the rectangle from the first cell picked to this one.</summary>
        public void Select(int row, int frame, bool extend)
        {
            if (FrameCount == 0)
                return;
            var cell = (Math.Clamp(row, 0, Rows.Count - 1), Math.Clamp(frame, 0, FrameCount - 1));
            if (extend == false || _anchor == null)
                _anchor = cell;
            _extent = cell;
            SelectionChanged();
        }

        /// <summary>Every frame of one row, or with <paramref name="extend"/> of every row from the first picked to this one.</summary>
        public void SelectRow(int row, bool extend)
        {
            if (FrameCount == 0)
                return;
            row = Math.Clamp(row, 0, Rows.Count - 1);
            _anchor = (extend && _anchor is { } anchor ? anchor.Row : row, 0);
            _extent = (row, FrameCount - 1);
            SelectionChanged();
        }

        public void SelectAll()
        {
            if (FrameCount == 0)
                return;
            _anchor = (0, 0);
            _extent = (Rows.Count - 1, FrameCount - 1);
            SelectionChanged();
        }

        public void ClearSelection()
        {
            _anchor = null;
            SelectionChanged();
        }

        void SelectionChanged()
        {
            var selection = Selection;
            if (selection != null && selection.LastFrame > selection.FirstFrame)
                _editor.SetFrameRange(selection.FirstFrame, selection.LastFrame);
            else
                _editor.SetFrameRange(null, null);
            Refresh();
        }

        public void Copy()
        {
            var clip = _editor.Rider?.AnimationClip;
            var skeleton = _editor.Skeleton;
            if (clip == null || skeleton == null || Selection is not { } selection)
            {
                Status = "Select the frames to copy first.";
                return;
            }

            var rows = Rows.Skip(selection.FirstRow).Take(selection.LastRow - selection.FirstRow + 1).ToList();
            var bones = rows.Any(x => x.Bone < 0) ? Enumerable.Range(0, skeleton.BoneCount).ToList() : rows.Select(x => x.Bone).ToList();
            var data = FrameClipboard.Copy(clip, skeleton, bones, selection.FirstFrame, selection.LastFrame);
            if (WriteClipboard(JsonConvert.SerializeObject(data)) == false)
            {
                Status = "Another program is holding the Windows clipboard. Copy again in a moment.";
                return;
            }
            var frames = selection.LastFrame - selection.FirstFrame + 1;
            var what = bones.Count == skeleton.BoneCount ? "every bone" : bones.Count == 1 ? skeleton.BoneNames[bones[0]] : $"{bones.Count} bones";
            Status = $"Copied {Plural(frames, "frame")} of {what}.";
        }

        public void Paste() => Paste(flipped: false);

        /// <summary>Pastes each copied bone onto its left/right partner, mirrored, as Blender's Paste Flipped.</summary>
        public void PasteFlipped() => Paste(flipped: true);

        void Paste(bool flipped)
        {
            var clip = _editor.Rider?.AnimationClip;
            var skeleton = _editor.Skeleton;
            if (clip == null || skeleton == null)
            {
                Status = "Load an animation first.";
                return;
            }
            if (Parse(ReadClipboard()) is not { } data)
            {
                Status = NoFrames;
                return;
            }
            if (flipped && PoseMirror.MirrorAxis(skeleton) == null)
            {
                Status = $"{skeleton.SkeletonName} has no bones named left and right, so there is nothing to flip.";
                return;
            }

            _editor.Pause();
            var at = CurrentFrame;
            var frames = Enumerable.Range(at, data.Frames.Count).Where(x => x < clip.DynamicFrames.Count).ToList();
            var pins = _editor.Pins.ToList();
            var limits = _editor.ActiveJointLimits;
            var channels = new FrameClipboard.Channels(_editor.PastePosition.Value, _editor.PasteRotation.Value, _editor.PasteScale.Value);
            FrameClipboard.PasteResult? result = null;
            _editor.ExecuteFrameEdit(clip, frames, edited =>
            {
                var held = frames.ToDictionary(x => x, x => PinSolver.WhereTheyAre(edited.DynamicFrames[x], skeleton, pins));
                result = FrameClipboard.Paste(edited, skeleton, data, at, channels, flipped);
                // Pins leave the pasted bones alone, as they leave a dragged bone alone; a paste of every bone protects none,
                // or the pins would hold nothing after it.
                IReadOnlyCollection<int> pasted = result.Bones.Count >= skeleton.BoneCount ? [] : result.Bones;
                foreach (var frame in frames)
                    PinSolver.Resolve(edited.DynamicFrames[frame], skeleton, held[frame], pasted, limits);
            }, flipped ? "Paste frames flipped" : "Paste frames");

            var status = $"Pasted {Plural(result!.Frames, "frame")}{(flipped ? " flipped" : "")} at frame {at}.";
            if (result.Skipped > 0)
                status += $" {Plural(result.Skipped, "frame")} didn't fit past the end of the clip.";
            if (result.MissingBones.Count > 0)
                status += $" {skeleton.SkeletonName} has no {string.Join(", ", result.MissingBones.Take(3))}{(result.MissingBones.Count > 3 ? $" or {result.MissingBones.Count - 3} more" : "")}.";
            Status = status;
            Refresh();
        }

        static BoneTransformClipboardData? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                var data = JsonConvert.DeserializeObject<BoneTransformClipboardData>(text);
                return data?.Frames is { Count: > 0 } ? data : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";

        // Another program can hold the clipboard open for a moment: a failed read is "nothing there", a failed write says so.
        static string? ReadWindowsClipboard()
        {
            try
            {
                return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or System.Threading.ThreadStateException)
            {
                return null;
            }
        }

        static bool WriteWindowsClipboard(string text)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or System.Threading.ThreadStateException)
            {
                return false;
            }
        }
    }
}
