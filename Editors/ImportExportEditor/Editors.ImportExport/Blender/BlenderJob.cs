namespace Editors.ImportExport.Blender
{
    /// <summary>
    /// One conversion for ae_convert.py, written to job.json. Mode is "rmv2_to_blend", "rmv2_to_obj" or "to_rmv2".
    /// </summary>
    public record BlenderJob(
        string Mode,
        string Input,
        string Output,
        string? SkeletonAnim = null,
        string? SkeletonName = null,
        string? TextureRoot = null,
        string? PackPath = null,
        int Version = 7,
        bool AutoLods = false);

    /// <summary>One file the script wrote. PackPath is the root collection's ae_pack_path, when it has one.</summary>
    public record BlenderOutput(string Path, string Name, int Lods, string? PackPath, int Bones = 0);

    public record BlenderResult(bool Ok, string? Error, IReadOnlyList<BlenderOutput> Files, IReadOnlyList<string> Warnings)
    {
        public static BlenderResult Fail(string error) => new(false, error, [], []);
    }
}
