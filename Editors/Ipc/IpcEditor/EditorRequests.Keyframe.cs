using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.Shared.Core.Common;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Selection;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;

namespace Editors.Ipc
{
    // The KeyFrame editor and its Keyframes strip, for checking the strip live without a mouse. Each operation goes through
    // what the strip's clicks and keys call, and answers with the strip as it now is.
    public partial class EditorRequests
    {
        static readonly string[] KeyframeOperations = ["open", "state", "bones", "frame", "select", "copy", "paste", "paste_flipped", "undo", "pose"];

        IpcResponse KeyframeOpen(IpcRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Animation))
                return IpcResponse.Failure("open needs \"path\", the mesh, and \"animation\", the clip, both in a loaded pack");
            try
            {
                var editor = (AnimationKeyframeEditorViewModel)_editorManager.Create(EditorEnums.AnimationKeyFrame_Editor);
                var mesh = Get<IPackFileService>(editor).FindFile(request.Path);
                if (mesh == null)
                    return IpcResponse.Failure($"No file \"{request.Path}\" in the loaded packs");
                var scene = Get<SceneObjectEditor>(editor);
                scene.SetMesh(editor.Rider, mesh);
                scene.SetAnimation(editor.Rider, request.Animation);
                if (editor.Rider.AnimationClip == null)
                    return IpcResponse.Failure($"\"{request.Animation}\" did not load onto the mesh");
                editor.EnterSelectMode();
                return Data(State(editor));
            }
            catch (Exception e)
            {
                Logging.Create<EditorRequests>().Here().Error(e, "IPC keyframe open failed");
                return IpcResponse.Failure($"keyframe open failed: {e.Message}");
            }
        }

        static IpcResponse Keyframe(IEditorInterface editorInterface, IpcRequest request)
        {
            if (editorInterface is not AnimationKeyframeEditorViewModel editor)
                return IpcResponse.Failure("keyframe works in the KeyFrame editor only; \"name\": \"open\" opens one");
            var sheet = editor.Sheet;
            switch (request.Name?.ToLowerInvariant())
            {
                case "state":
                    break;
                case "bones":
                    {
                        var names = request.Bones ?? [];
                        var bones = names.Select(editor.Skeleton.GetBoneIndexByName).ToList();
                        var missing = names.Where((_, i) => bones[i] < 0).ToList();
                        if (missing.Count > 0)
                            return IpcResponse.Failure($"{editor.Skeleton.SkeletonName} has no {string.Join(", ", missing)}");
                        if (editor.SelectionManager.GetState() is not BoneSelectionState)
                            editor.EnterSelectMode();
                        if (editor.SelectionManager.GetState() is BoneSelectionState state)
                            state.SelectedBones.Clear();
                        editor.CommandFactory.CreateWithBuilder<BoneSelectionCommand>().Configure(x => x.Configure(bones, true, false)).BuildAndExecute();
                        break;
                    }
                case "frame":
                    if (request.Frame is not int target)
                        return IpcResponse.Failure("frame needs \"frame\"");
                    sheet.CurrentFrame = target;
                    break;
                case "select":
                    if (request.Frames is not [var first, var last])
                    {
                        if (request.Frames != null || request.Rows != null)
                            return IpcResponse.Failure("select needs \"frames\": [first, last], and \"rows\": [first, last] or none for the All bones row");
                        sheet.ClearSelection();
                        break;
                    }
                    var rows = request.Rows is [var r0, var r1] ? (r0, r1) : (0, 0);
                    sheet.Select(rows.Item1, first, extend: false);
                    sheet.Select(rows.Item2, last, extend: true);
                    break;
                case "copy":
                    sheet.Copy();
                    break;
                case "paste":
                    sheet.Paste();
                    break;
                case "paste_flipped":
                    sheet.PasteFlipped();
                    break;
                case "undo":
                    editor.UndoPose();
                    break;
                case "pose":
                    {
                        var clip = editor.Rider.AnimationClip!;
                        var frame = request.Frame ?? sheet.CurrentFrame;
                        if (frame < 0 || frame >= clip.DynamicFrames.Count)
                            return IpcResponse.Failure($"frame {frame} is outside the clip, 0 to {clip.DynamicFrames.Count - 1}");
                        var pose = new Dictionary<string, float[]>();
                        foreach (var name in request.Bones ?? [])
                        {
                            var bone = editor.Skeleton.GetBoneIndexByName(name);
                            if (bone < 0)
                                return IpcResponse.Failure($"{editor.Skeleton.SkeletonName} has no {name}");
                            var (p, r) = (clip.DynamicFrames[frame].Position[bone], clip.DynamicFrames[frame].Rotation[bone]);
                            pose[name] = [p.X, p.Y, p.Z, r.X, r.Y, r.Z, r.W];
                        }
                        return Data(pose);
                    }
                default:
                    return IpcResponse.Failure($"keyframe \"name\" is one of {string.Join(", ", KeyframeOperations)}");
            }
            return Data(State(editor));
        }

        static KeyframeState State(AnimationKeyframeEditorViewModel editor)
        {
            var sheet = editor.Sheet;
            var s = sheet.Selection;
            return new KeyframeState(sheet.CurrentFrame, sheet.FrameCount, sheet.Rows.Select(x => x.Name).ToList(),
                s == null ? null : [s.FirstRow, s.LastRow, s.FirstFrame, s.LastFrame], sheet.Status,
                editor.SelectedFrameAInterpolation.Value, editor.SelectedFrameBInterpolation.Value, editor.CommandManager.CanUndo());
        }

        /// <param name="Selection">First row, last row, first frame, last frame; null for none.</param>
        public record KeyframeState(int Frame, int Frames, List<string> Rows, int[]? Selection, string Status, string FrameA, string FrameB, bool CanUndo);
    }
}
