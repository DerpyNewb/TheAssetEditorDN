#ifndef CONST_LAYOUT_HLSLI
#define CONST_LAYOUT_HLSLI



float4x4 World;
float4x4 View;
float3 CameraPos;
float4x4 Projection;

float4x4 EnvMapTransform;
float4x4 DirLightTransform;

float LightMult = 1;

bool debug = true;
float exposure = 1;
float3 Constant_LightColour = float3(1, 1, 1);

float light0_roughnessFactor = 1;
float light0_radiannce = 1;
float light0_ambientFactor = 1;

float3 TintColour = float3(1, 1, 1);

// A rigid particle's colour and alpha off its chain; white and opaque for everything else.
float4 ModelColour = float4(1, 1, 1, 1);

// The VFX preview's light particles (CommonShaderParameters.PointLights): position and reach (xyz, w), and colour.
float4 PointLights[16];
float4 PointLightColours[16];
int PointLightCount = 0;

// Light i's colour where it reaches position, falling off as its pool on the ground does, (1 - d^2/r^2)^2, and the way to it.
// Each shader lights with it as it lights with the sun, on the sun's scale; the colours arrive already at CA's share of the
// sun (VfxConventions.LightShare), so a particle of colour 1 and alpha 1 is a 24th of it.
float3 PointLightAt(int i, float3 position, out float3 direction)
{
    float3 to = PointLights[i].xyz - position;
    direction = normalize(to);
    float fall = saturate(1 - dot(to, to) / max(PointLights[i].w * PointLights[i].w, 0.0001));
    return PointLightColours[i].rgb * fall * fall;
}

// Textures
Texture2D<float4> DiffuseTexture;
Texture2D<float4> SpecularTexture;
Texture2D<float4> NormalTexture;
Texture2D<float4> GlossTexture;
Texture2D<float4> MaskTexture;

TextureCube<float4> tex_cube_diffuse;
TextureCube<float4> tex_cube_specular;
Texture2D<float4> specularBRDF_LUT;

bool UseDiffuse = true;
bool UseSpecular = true;
bool UseNormal = true;
bool UseGloss = true;
bool UseAlpha = false;
bool UseMask = true;



#endif // CONST_LAYOUT_HLSLI