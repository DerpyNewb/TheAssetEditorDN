namespace Editors.Ipc
{
    public class IpcRequest
    {
        public string Action { get; set; }
        public string Path { get; set; }
        public bool? BringToFront { get; set; }
        public bool? OpenInExistingKitbashTab { get; set; }
        // open: the editor to open the file in, by its EditorEnums name with or without "_Editor" ("SuperView",
        // "VariantMesh", "XML_VariantMesh_Editor"). Left out, a model opens in Kitbash and anything else as a double-click does.
        public string? OpenWith { get; set; }
        public string PackPathOnDisk { get; set; }
        public int? Limit { get; set; }

        // The editor actions (EditorRequests). Editor is a tab index from "editors"; the current tab when left out.
        public int? Editor { get; set; }
        public List<string>? Nodes { get; set; }
        public bool? Add { get; set; }
        public bool? Visible { get; set; }
        // close: drop the tab's unsaved changes instead of refusing.
        public bool? Discard { get; set; }
        public string? Name { get; set; }
        public float[]? Translate { get; set; }
        public float[]? Rotate { get; set; }
        public float[]? Scale { get; set; }
        public string? Pivot { get; set; }
        public float? Yaw { get; set; }
        public float? Pitch { get; set; }
        public float? Zoom { get; set; }
        public float[]? LookAt { get; set; }

        // keyframe: Name is the operation. Path and Animation are the mesh and clip "open" loads; Bones are bone names; Frame
        // is one frame; Rows and Frames are [first, last] of the Keyframes strip, both ends included.
        public string? Animation { get; set; }
        public List<string>? Bones { get; set; }
        public int? Frame { get; set; }
        public int[]? Rows { get; set; }
        public int[]? Frames { get; set; }

        // vm: Name is tree, get, set or invoke; Path the member path ("Tags[3].Variables[FieldName=StartTime].ValueAsString");
        // Value the text to set or the one argument to pass, "@path" for an object; Depth how many levels a tree opens.
        public string? Value { get; set; }
        public int? Depth { get; set; }
    }
}
