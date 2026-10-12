using System.Linq;
using Shared.GameFormats.AnimationPack;

namespace Editors.CampaignAnimationSetEditor.Services
{
    /// <summary>(Name, Value) pairs for ComboBox ItemsSource/SelectedValuePath binding, since
    /// PersistentMeta_Pose.PoseId is a plain int rather than this enum type.</summary>
    public static class KnownPoseIdOptions
    {
        public record Option(string Name, int Value);

        public static readonly Option[] All = System.Enum.GetValues<KnownPoseId>()
            .Select(v => new Option(v.ToString(), (int)v))
            .ToArray();
    }
}
