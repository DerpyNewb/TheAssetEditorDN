using Microsoft.Xna.Framework;

namespace GameWorld.Core.Rendering
{
    public record CommonShaderParameters(
        Matrix View,
        Matrix Projection,
        Vector3 CameraPosition,
        Vector3 CameraLookAt,
        float EnvLightRotationsRadians_Y,
        float DirLightRotationRadians_X,
        float DirLightRotationRadians_Y,
        float LightIntensityMult,
        Vector3[] FactionColours,
        Vector3 LightColour,
        float ViewportHeight = 0,
        float ViewportWidth = 0
        )
    {
        /// <summary>Multiplies the model's colour and is its alpha: a rigid particle's colour off its chain. White and opaque by default.</summary>
        public Vector4 ModelColour { get; init; } = Vector4.One;
    }

}
