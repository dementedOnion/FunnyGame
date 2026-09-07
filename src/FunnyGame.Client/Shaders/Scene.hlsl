cbuffer Camera : register(b0)
{
    float4x4 ViewProjection;
};

struct VertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
};

PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    output.Position = mul(float4(input.Position, 1.0f), ViewProjection);
    output.Color = input.Color;
    return output;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    return input.Color;
}
