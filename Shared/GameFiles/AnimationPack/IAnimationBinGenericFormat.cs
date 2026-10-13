namespace Shared.GameFormats.AnimationPack
{
    public interface IAnimationBinGenericFormat
    {
        /// <summary>The animpack holding a battle set; null for a campaign set, which is a file of its own.</summary>
        public AnimationPackFileDatabase? PackFileReference { get; }
        public string Name { get; }
        public string FullPath { get; }
        public string SkeletonName { get; }
        /// <summary>Name of the mount's fragment. Only set on rider fragments; empty otherwise.</summary>
        public string MountBin { get; }
        public List<AnimationBinEntryGenericFormat> Entries { get; }
    }

    public class AnimationBinEntryGenericFormat
    {
        public int Index { get; set; } = -1;
        public int SlotIndex { get; set; }
        public string SlotName { get; set; }
        public string DisplayName { get => GetDisplayName(); }
        public string AnimationFile { get; set; }
        public string MetaFile { get; set; }
        public string SoundFile { get; set; }
        public float BlendInTime { get; set; }
        public float SelectionWeight { get; set; }
        // Which weapon bones the slot shows, bit N for be_prop_N. A set that carries none (a campaign set) shows them all.
        public int WeaponBools { get; set; } = 0b111111;

        string GetDisplayName()
        {
            if (Index == -1)
                return SlotName;
            return $"[{Index}] {SlotName}";
        }
    }
}
