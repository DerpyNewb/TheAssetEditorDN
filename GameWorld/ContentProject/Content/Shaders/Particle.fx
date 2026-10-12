#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0
	#define PS_SHADERMODEL ps_4_0
#endif

// The VFX preview's particles, drawn by GameWorld.Core/Vfx/ParticleRenderItem.cs. Every reading of a CA fragment here
// is a guess, listed in docs/VFX.md beside VfxConventions' own; ParticleRenderItemTests draw each one on the GPU.

float4x4 WorldViewProjection;
// A vertex colour stops at 1: an HDR emitter's colours arrive divided by Scale and are multiplied back here.
float Scale = 1;
float Premultiplied = 1;
// ps_alpha_crush sharp_transition.
float CrushSharp = 0;
// ps_gradient: whether the chain has one, its alpha_mapping, and its strip at 16 points from 0 to 1.
float UseGradient = 0;
float GradientMapsAlpha = 0;
float4 GradientRamp[16];

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
	// The next flipbook cell's texture coordinate (xy), how far to blend into it (z), the alpha crush level (w).
	float4 Extra : TEXCOORD1;
};

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Colour : COLOR0;
	float2 Uv : TEXCOORD0;
	float4 Extra : TEXCOORD1;
};

float4 Ramp(float t)
{
	float x = saturate(t) * 15;
	float i = min(floor(x), 14);
	return lerp(GradientRamp[(int)i], GradientRamp[(int)i + 1], x - i);
}

VertexShaderOutput MainVS(in VertexShaderInput input)
{
	VertexShaderOutput output;
	output.Position = mul(input.Position, WorldViewProjection);
	output.Colour = input.Colour * float4(Scale, Scale, Scale, 1);
	output.Uv = input.Uv;
	output.Extra = input.Extra;
	return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
	// vs_animated_texture cell_blend: this cell mixed into the next (VfxConventions.FlipbookFrame).
	float4 texel = lerp(tex2D(TextureSampler, input.Uv), tex2D(TextureSampler, input.Extra.xy), input.Extra.z);

	// The rest works on the straight colour: a premultiplied texel is divided out first and multiplied back after.
	float a = texel.a;
	float3 rgb = Premultiplied > 0 && a > 0 ? texel.rgb / a : texel.rgb;

	// ps_gradient, read as a gradient map: the texel's red picks a colour off the strip. Its alpha is multiplied by the
	// strip's there, or with alpha_mapping is put through the strip's alpha itself.
	if (UseGradient > 0)
	{
		float4 picked = Ramp(rgb.r);
		rgb = picked.rgb;
		a = GradientMapsAlpha > 0 ? Ramp(a).a : a * picked.a;
	}

	// ps_alpha_crush: alpha under the particle's crush level goes. A sharp crush cuts there and keeps what is over; a soft
	// one stretches what is left back over 0..1, so the texture thins from its faint edges in.
	float crush = input.Extra.w;
	a = CrushSharp > 0 ? (a > crush ? a : 0) : saturate((a - crush) / max(1 - crush, 0.0001));

	return float4(Premultiplied > 0 ? rgb * a : rgb, a) * input.Colour;
}

technique ParticleDrawing
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
