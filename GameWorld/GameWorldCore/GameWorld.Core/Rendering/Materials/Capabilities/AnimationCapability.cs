using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.Materials.Capabilities
{
    public class AnimationCapability : ICapability
    {
        public bool ApplyAnimation { get; set; }
        public bool AnimationInformation { get; set; }
        public Matrix[]? AnimationTransforms { get; set; }
        public int AnimationWeightCount { get; set; }

        /// <summary>Kitbash's weight view: the bone whose weights the mesh draws instead of its textures, -1 for none.</summary>
        public int WeightViewBone { get; set; } = -1;

        public void Apply(Effect effect, IScopedResourceLibrary _)
        {
            effect.Parameters["CapabilityFlag_ApplyAnimation"].SetValue(ApplyAnimation);
            effect.Parameters["Animation_WeightCount"].SetValue(AnimationWeightCount);
            effect.Parameters["Animation_Tranforms"].SetValue(AnimationTransforms);
            effect.Parameters["Animation_WeightBone"]?.SetValue(WeightViewBone);
        }

        public ICapability Clone()
        {
            return new AnimationCapability()
            {
                ApplyAnimation = ApplyAnimation,
                AnimationInformation = AnimationInformation,
                AnimationTransforms = AnimationTransforms,
                AnimationWeightCount = AnimationWeightCount,
                WeightViewBone = WeightViewBone,
            };
        }

        public (bool Result, string Message) AreEqual(ICapability otherCap)
        {
            var typedCap = otherCap as AnimationCapability;
            if (typedCap == null)
                throw new System.Exception($"Comparing {GetType} against {otherCap?.GetType()}");
            return (true, "");
        }
    }
}
