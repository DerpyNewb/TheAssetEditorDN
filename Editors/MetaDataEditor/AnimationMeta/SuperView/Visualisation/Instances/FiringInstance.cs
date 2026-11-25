using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // An event tag's marker, drawn in MetaDataBuilder.FiringColour while the tag fires (MetaDataBuilder.IsFiring). The viewport
    // picks and moves the marker through this wrapper, so it answers for the marker it wraps.
    public class FiringInstance(IMetaDataInstance marker, SimpleDrawableNode node, float startTime, float endTime) : IMetaDataInstance, IMetaDataMarkerPreview
    {
        readonly IMetaDataMarkerPreview? _preview = marker as IMetaDataMarkerPreview;

        public AnimationPlayer Player => null;

        public void Update(float currentTime)
        {
            marker.Update(currentTime);
            node.Tint = MetaDataBuilder.IsFiring(startTime, endTime, currentTime) ? MetaDataBuilder.FiringColour : null;
        }

        public void CleanUp() => marker.CleanUp();

        public ParsedMetadataAttribute Source => _preview?.Source!;
        public Vector3 FocusPosition => _preview?.FocusPosition ?? Vector3.Zero;
        public Matrix ReferenceWorldTransform => _preview?.ReferenceWorldTransform ?? Matrix.Identity;
        public bool FollowsTagLive => _preview?.FollowsTagLive ?? false;
        public bool IsHitTestVisible => _preview?.IsHitTestVisible ?? false;
        public float HitTestRadius => _preview?.HitTestRadius ?? 0;
        public IReadOnlyList<MetaDataMarkerHitTarget> HitTargets => _preview?.HitTargets ?? [];
    }
}
