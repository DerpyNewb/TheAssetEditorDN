using GameWorld.Core.Rendering.Materials.Capabilities.Utility;
using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Rendering.Materials.Capabilities
{
    public class MetalRoughCapability : MaterialBaseCapability
    {
        public TextureInput BaseColour { get; set; } = new TextureInput(TextureType.BaseColour);
        public TextureInput MaterialMap { get; set; } = new TextureInput(TextureType.MaterialMap);
        public TextureInput NormalMap { get; set; } = new TextureInput(TextureType.Normal);
        public TextureInput Mask { get; set; } = new TextureInput(TextureType.Mask);
        public TextureInput Distortion { get; set; } = new TextureInput(TextureType.Distortion);
        public TextureInput DistortionNoise { get; set; } = new TextureInput(TextureType.DistortionNoise);

        public override void Apply(Effect effect, IScopedResourceLibrary resourceLibrary)
        {
            BaseColour.Apply(effect, resourceLibrary);
            MaterialMap.Apply(effect, resourceLibrary);
            NormalMap.Apply(effect, resourceLibrary);
            Mask.Apply(effect, resourceLibrary);

            base.Apply(effect, resourceLibrary);
        }

        public override ICapability Clone()
        {
            return new MetalRoughCapability()
            {
                ScaleMult = ScaleMult,
                UseAlpha = UseAlpha,
                BaseColour = BaseColour.Clone(),
                MaterialMap = MaterialMap.Clone(),
                NormalMap = NormalMap.Clone(),
                Mask = Mask.Clone(),
                Distortion = Distortion.Clone(),
                DistortionNoise = DistortionNoise.Clone(),
            };
        }

        public override void Initialize(WsModelMaterialFile? wsModelMaterial, IRmvMaterial rmvMaterial) => MetalRoughCapabilitySerializer.Initialize(this, wsModelMaterial, rmvMaterial);

        public override void SerializeToWsModel(WsMaterialTemplateEditor templateHandler) => MetalRoughCapabilitySerializer.SerializeToWsModel(this, templateHandler);

        public override void SerializeToRmvMaterial(IRmvMaterial rmvMaterial) => MetalRoughCapabilitySerializer.SerializeToRmvMaterial(this, rmvMaterial);

        public override (bool Result, string Message) AreEqual(ICapability otherCap)
        {
            if (otherCap is not MetalRoughCapability typedCap)
                throw new System.Exception($"Comparing {GetType} against {otherCap?.GetType()}");

            if (!CompareHelper.Compare(BaseColour, typedCap.BaseColour, nameof(BaseColour), out var res0))
                return res0;
            if (!CompareHelper.Compare(MaterialMap, typedCap.MaterialMap, nameof(MaterialMap), out var res1))
                return res1;
            if (!CompareHelper.Compare(NormalMap, typedCap.NormalMap, nameof(NormalMap), out var res2))
                return res2;
            if (!CompareHelper.Compare(Mask, typedCap.Mask, nameof(Mask), out var res3))
                return res3;

            return base.AreEqual(otherCap);
        }
    }

    public static class MetalRoughCapabilitySerializer
    {
        public static void Initialize(MetalRoughCapability output, WsModelMaterialFile? wsModelMaterial, IRmvMaterial rmvMaterial)
        {

            if (wsModelMaterial != null)
            {
                output.UseAlpha = wsModelMaterial.Alpha;
            }
            else
            {
                if (rmvMaterial is WeightedMaterial weightedMaterial == false)
                    throw new Exception($"Unable to convert material of type {rmvMaterial?.MaterialId} into {nameof(WeightedMaterial)}");

                weightedMaterial.IntParams.TryGet(WeightedParamterIds.IntParams_Alpha_index, out var useAlpha);
                output.UseAlpha = useAlpha == 1;
            }

            CapabilityHelper.SetTextureFromModel(rmvMaterial, wsModelMaterial, output.BaseColour);
            CapabilityHelper.SetTextureFromModel(rmvMaterial, wsModelMaterial, output.MaterialMap);
            CapabilityHelper.SetTextureFromModel(rmvMaterial, wsModelMaterial, output.NormalMap);
            CapabilityHelper.SetTextureFromModel(rmvMaterial, wsModelMaterial, output.Mask);
            CapabilityHelper.SetTextureFromModel(rmvMaterial, wsModelMaterial, output.Distortion, "commontextures/winds_of_magic_specular.dds");
            CapabilityHelper.SetTextureFromModel(rmvMaterial, wsModelMaterial, output.DistortionNoise, "commontextures/winds_of_magic_noise.dds");

       
        }

        // An empty slot names the texture CA's own WH3 materials use for "none" in it: of 10,909, 873 name default_black for the
        // faction-colour mask, 69 for the material map, 93 default_normal and 36 default_grey for the base colour, and none names
        // Kitbash's bare test_mask.dds, which is in no pack: CA's is commontextures/test_mask.dds (counted 2026-11-22). All three
        // stand-ins are in the game's boot.pack and commontextures.pack.
        public const string NoMask = "commontextures/default_black.dds", NoMaterialMap = "commontextures/default_black.dds",
            NoNormal = "commontextures/default_normal.dds", NoBaseColour = "commontextures/default_grey.dds";

        public static void SerializeToWsModel(MetalRoughCapability typedCap, WsMaterialTemplateEditor templateHandler)
        {
            templateHandler.AddAttribute(WsModelParamters.Texture_BaseColour.TemplateName, typedCap.BaseColour, NoBaseColour);
            templateHandler.AddAttribute(WsModelParamters.Texture_Mask.TemplateName, typedCap.Mask, NoMask);
            templateHandler.AddAttribute(WsModelParamters.Texture_MaterialMap.TemplateName, typedCap.MaterialMap, NoMaterialMap);
            templateHandler.AddAttribute(WsModelParamters.Texture_Normal.TemplateName, typedCap.NormalMap, NoNormal);
            templateHandler.AddAttribute(WsModelParamters.Texture_Distortion.TemplateName, typedCap.Distortion);
            templateHandler.AddAttribute(WsModelParamters.Texture_DistortionNoise.TemplateName, typedCap.DistortionNoise);
        }

        public static void SerializeToRmvMaterial(MetalRoughCapability typedCap, IRmvMaterial rmvMaterial)
        {
            if (rmvMaterial is not WeightedMaterial weightedMateial)
                throw new Exception($"Input material '{rmvMaterial.GetType()}' is not {nameof(WeightedMaterial)} - Unable to serialize to rmv");

            rmvMaterial.SetTexture(typedCap.BaseColour.Type, typedCap.BaseColour.TexturePath);
            rmvMaterial.SetTexture(typedCap.MaterialMap.Type, typedCap.MaterialMap.TexturePath);
            rmvMaterial.SetTexture(typedCap.NormalMap.Type, typedCap.NormalMap.TexturePath);
            rmvMaterial.SetTexture(typedCap.Mask.Type, typedCap.Mask.TexturePath);

            weightedMateial.IntParams.Set(WeightedParamterIds.IntParams_Alpha_index, typedCap.UseAlpha ? 1 : 0);
        }
    }
}
