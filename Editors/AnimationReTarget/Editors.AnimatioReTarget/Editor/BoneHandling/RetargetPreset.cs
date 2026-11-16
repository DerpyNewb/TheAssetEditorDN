namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    /// <summary>
    /// The transfer tool's mapping and settings, saved as a .retarget.json file. Bones are kept by NAME, the target bone and the
    /// source bone that drives it, so a preset made for one pair of skeletons applies to any pair that shares the names.
    /// </summary>
    public record RetargetPreset(int Version, string SourceSkeleton, string TargetSkeleton, RetargetPresetSettings Settings, List<RetargetPresetBone> Bones);

    public record RetargetPresetSettings(float SkeletonScale, float AnimationSpeedMult, bool ApplyRelativeScale, bool ZeroUnmappedBones);

    public record RetargetPresetBone(string Name, string? MappedFrom, float BoneLengthMult, double[] RotationOffset, double[] TranslationOffset,
        bool IsLocalOffset, bool ForceSnapToWorld, bool FreezeTranslation, bool FreezeRotation, bool FreezeRotationZ, bool ApplyTranslation,
        bool ApplyRotation, string? RelativeTo);
}
