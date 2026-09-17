// Stretch any color texture to the destination RT by UV sample.
Texture2D Source : register(t0);
SamplerState LinearClamp : register(s0);

float4 PSMain(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    return float4(Source.SampleLevel(LinearClamp, uv, 0).rgb, 1.0);
}
