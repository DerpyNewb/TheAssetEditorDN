#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0
	#define PS_SHADERMODEL ps_4_0
#endif

// The VFX preview's particles, drawn by GameWorld.Core/Vfx/ParticleRenderItem.cs. Every reading of a CA fragment here
// is a guess with a C# twin in VfxConventions, which the tests hold this file to.

float4x4 WorldViewProjection;
// A vertex colour stops at 1: an HDR emitter's colours arrive divided by Scale and are multiplied back here.
float Scale = 1;

Texture2D Texture;
sampler2D TextureSampler = sampler_state
{
	Texture = <Texture>;
};

struct VertexShaderInput
{
	float4 Position : POSITION0;
	float4 Colour : COLOR0;
	float2 Uv : TEXCOORD0;
};

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Colour : COLOR0;
	float2 Uv : TEXCOORD0;
};

VertexShaderOutput MainVS(in VertexShaderInput input)
{
	VertexShaderOutput output;
	output.Position = mul(input.Position, WorldViewProjection);
	output.Colour = input.Colour * float4(Scale, Scale, Scale, 1);
	output.Uv = input.Uv;
	return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
	return tex2D(TextureSampler, input.Uv) * input.Colour;
}

technique ParticleDrawing
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
