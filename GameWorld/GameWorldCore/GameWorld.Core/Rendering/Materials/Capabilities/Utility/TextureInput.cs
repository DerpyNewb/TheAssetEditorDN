using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel.Types;

namespace GameWorld.Core.Rendering.Materials.Capabilities.Utility
{
    public class TextureInput
    {
        public string TexturePath { get; set; }
        public bool UseTexture { get; set; }
        public TextureType Type { get; set; }

        public TextureInput(TextureType type)
        {
            Type = type;
            UseTexture = false;
            TexturePath = string.Empty;
        }

        public TextureInput Clone()
        {
            return new TextureInput(Type)
            {
                UseTexture = UseTexture,
                TexturePath = TexturePath
            };
        }

        public void Apply(Effect effect, IScopedResourceLibrary resourceLibrary)
        {
            var useTextureParam = ShaderParameterHelper.UseTextureTypeToParamName[Type];
            var textureParam = ShaderParameterHelper.TextureTypeToParamName[Type];
            effect.Parameters[useTextureParam].SetValue(UseTexture);

            Texture2D? texture = null;
            if (UseTexture)
            {
                texture = resourceLibrary.LoadTexture(TexturePath);
                effect.Parameters[textureParam].SetValue(texture);
            }

            // The game reads a base colour with no sRGB flag as linear, so it draws lighter there; the shader decodes the rest from sRGB.
            if (Type == TextureType.BaseColour)
            {
                var linear = texture?.Tag is ImageInformation { IsDdsImage: true, HasSrgbFlag: false };
                effect.Parameters["BaseColourIsLinear"]?.SetValue(linear);
                if (linear && s_warnedPaths.Add(TexturePath))
                    s_logger.Here().Warning($"{TexturePath} is a base colour in {((ImageInformation)texture!.Tag).FormatName}, which has no sRGB flag. The game reads it as linear and draws it lighter, and so does this view. Save it as BC1_UNORM_SRGB, BC3_UNORM_SRGB or BC7_UNORM_SRGB.");
            }
        }

        static readonly ILogger s_logger = Logging.CreateStatic(typeof(TextureInput));
        static readonly HashSet<string> s_warnedPaths = new(StringComparer.OrdinalIgnoreCase);
    }

}
