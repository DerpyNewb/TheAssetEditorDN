namespace Editors.Ipc
{
    public class IpcRequest
    {
        public string Action { get; set; }
        public string Path { get; set; }
        public bool? BringToFront { get; set; }
        public bool? OpenInExistingKitbashTab { get; set; }
        public string PackPathOnDisk { get; set; }
        public int? Limit { get; set; }

        // The editor actions (EditorRequests). Editor is a tab index from "editors"; the current tab when left out.
        public int? Editor { get; set; }
        public List<string>? Nodes { get; set; }
        public bool? Add { get; set; }
        public bool? Visible { get; set; }
        public string? Name { get; set; }
        public float[]? Translate { get; set; }
        public float[]? Rotate { get; set; }
        public float[]? Scale { get; set; }
        public string? Pivot { get; set; }
        public float? Yaw { get; set; }
        public float? Pitch { get; set; }
        public float? Zoom { get; set; }
        public float[]? LookAt { get; set; }
    }
}
