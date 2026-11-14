#ifndef GPUSKINNING_HLSLI
#define GPUSKINNING_HLSLI

#include "../InputLayouts.hlsli"

// Input parameters
bool        CapabilityFlag_ApplyAnimation = false;
float4x4    Animation_Tranforms[256];
int         Animation_WeightCount = 0;
int         Animation_WeightBone = -1;     // Kitbash's weight view: the bone whose weights are drawn, -1 = off

// How much of the vertex Animation_WeightBone moves, 0 to 1. A static mesh has no weights, so 0.
float BoneWeight(in VertexInputType input)
{
    float weight = 0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (i < Animation_WeightCount && (int) (input.BoneIndices[i] + 0.5f) == Animation_WeightBone)
            weight += input.Weights[i];
    }
    return weight;
}

// The weight view's colour: blue at 0 through green to red at 1, darker where the bone has less hold, shaded towards the
// camera so the shape still reads.
float3 WeightColour(float weight, float3 normal, float3 toCamera)
{
    weight = saturate(weight);
    float hue = (1 - weight) * 2.0f / 3.0f;
    float3 ramp = saturate(float3(abs(hue * 6 - 3) - 1, 2 - abs(hue * 6 - 2), 2 - abs(hue * 6 - 4)));
    float shade = 0.35f + 0.65f * abs(dot(normalize(normal), normalize(toCamera)));
    return ramp * (0.4f + 0.6f * weight) * shade;
}


void DoSkinning(
	in VertexInputType input,
	out float4 position,
	out float3 normal,
	out float3 tangent,
	out float3 binormal)
{
    
    if (CapabilityFlag_ApplyAnimation == false)
    {
        position = input.position;
        normal = input.normal;
        tangent = input.tangent;
        binormal = input.binormal;
    }
    else
    {
        position = 0;
        normal = 0;
        tangent = 0;
        binormal = 0;
	
	    [unroll]
        for (int i = 0; i < Animation_WeightCount; i++)
        {
            float4x4 currentTransform4x4 = Animation_Tranforms[input.BoneIndices[i]];
            float3x3 currentTransform3x3 = currentTransform4x4;
            // TODO: sawp "FramePoseMatrices out with "tranforms[input.BoneIndices[i]]"
            // ---------------------------------------------------------------------------------
		    // transform vertex position
            // ---------------------------------------------------------------------------------
            position += input.Weights[i] * mul(float4(input.position.xyz, 1), currentTransform4x4);

		    // ---------------------------------------------------------------------------------
            // tranform lighting vectors, using only rotational part (3x3) of the matrix
            // ---------------------------------------------------------------------------------
            normal.xyz += input.Weights[i] * mul(input.normal.xyz, currentTransform3x3);
            tangent.xyz += input.Weights[i] * mul(input.tangent.xyz,  currentTransform3x3);
            binormal.xyz += input.Weights[i] * mul(input.binormal.xyz, currentTransform3x3);
        }
    }
}

#endif // GPUSKINNING_HLSLI