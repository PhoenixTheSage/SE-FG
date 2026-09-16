// Composite generated color with unwarped HUD sprites.
// t0 Interp is the compute UAV (UNORM, linear when the swapchain is sRGB).
// t1 Hud / t2 Scene are *_SRGB copies; Load is already linear. SrgbIn stays 0
// (a second SrgbToLinear crushes Keen HUD to black). Dest sRGB RTV encodes.
// Pulsar compiles C# only; bake with fxc /T ps_5_0 /E PSMain.

cbuffer Constants : register(b0)
{
    uint SrgbIn;
    uint Pad0;
    uint Pad1;
    uint Pad2;
};

Texture2D Interp : register(t0);
Texture2D Hud : register(t1);
Texture2D Scene : register(t2);
SamplerState PointClamp : register(s0);

float3 SrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(saturate((c + 0.055) / 1.055), 2.4);
    return lerp(lo, hi, step(0.04045, c));
}

float4 PSMain(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    int3 px = int3(pos.xy, 0);
    // Interp may be smaller than the swapchain when an upscaler SetDRS'd
    // ResolutionI; Sample by UV stretches. Hud/Scene copies are DXGI-sized.
    float3 interp = Interp.SampleLevel(PointClamp, uv, 0).rgb;
    float3 hud = Hud.Load(px).rgb;
    float3 scene = Scene.Load(px).rgb;
    if (SrgbIn != 0)
    {
        hud = SrgbToLinear(hud);
        scene = SrgbToLinear(scene);
    }

    float hudPx = dot(abs(Hud.Load(px).rgb - Scene.Load(px).rgb), 1.0) > 0.02;
    float3 color = hudPx ? hud : interp;
    return float4(color, 1.0);
}
