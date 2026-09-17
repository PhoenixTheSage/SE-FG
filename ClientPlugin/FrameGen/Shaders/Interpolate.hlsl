// FSR3-style bidirectional warp interpolator for DX11 SM 5.0.
// Motion vectors are current-to-previous pixel space (Anomaly convention 15).
// previousPixel = currentPixel + motion.
// Baked to DXBC in ShaderBytecode.InterpolateCs (fxc cs_5_0). Pulsar compiles C# only.

cbuffer Constants : register(b0)
{
    uint Width;
    uint Height;
    float InvWidth;
    float InvHeight;
    // MvScale converts Anomaly internal pixel delta to output pixels so
    // mvUv = mvInternal / velSize. Camera MVs generated at output size use 1,1.
    float MvScaleX;
    float MvScaleY;
    uint InvertedDepth;
    uint Pad;
};

Texture2D PrevColor : register(t0);
Texture2D CurrColor : register(t1);
Texture2D DepthTex : register(t2);
Texture2D MotionTex : register(t3);
SamplerState LinearClamp : register(s0);
RWTexture2D<float4> Output : register(u0);

float DepthAtUv(float2 uv)
{
    return DepthTex.SampleLevel(LinearClamp, saturate(uv), 0).r;
}

float DepthDisocclude(float sampleDepth, float centerDepth)
{
    float diff = abs(sampleDepth - centerDepth);
    float scale = InvertedDepth ? max(centerDepth, 1e-4) : max(1.0 - centerDepth, 1e-4);
    return saturate(1.0 - diff / (scale * 0.08 + 1e-4));
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    int2 pixel = int2(id.xy);
    float4 currPx = CurrColor.Load(int3(pixel, 0));
    float2 uv = (float2(pixel) + 0.5) * float2(InvWidth, InvHeight);
    // Center MV only. Passthrough uses the motion-buffer texel magnitude so
    // MvScale (DRS→output) cannot turn sub-pixel noise into a warp.
    float2 mvNative = MotionTex.SampleLevel(LinearClamp, uv, 0).xy;
    float2 mvPx = mvNative * float2(MvScaleX, MvScaleY);
    float magNative = length(mvNative);
    float magOut = length(mvPx);
    if (magNative < 1.25 || magOut > 96.0 || any(isnan(mvPx)))
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float2 mvUv = mvPx * float2(InvWidth, InvHeight);
    float2 uvPrev = uv + 0.5 * mvUv;
    float2 uvCurr = uv - 0.5 * mvUv;

    float4 prev = PrevColor.SampleLevel(LinearClamp, saturate(uvPrev), 0);
    float4 curr = CurrColor.SampleLevel(LinearClamp, saturate(uvCurr), 0);
    float centerDepth = DepthAtUv(uv);

    float wPrev = DepthDisocclude(DepthAtUv(uvPrev), centerDepth);
    float wCurr = DepthDisocclude(DepthAtUv(uvCurr), centerDepth);
    wPrev *= all(uvPrev >= 0.0) && all(uvPrev <= 1.0) ? 1.0 : 0.0;
    wCurr *= all(uvCurr >= 0.0) && all(uvCurr <= 1.0) ? 1.0 : 0.0;
    wCurr = max(wCurr, 0.35);

    float sum = wPrev + wCurr;
    float4 mixed = sum < 1e-3 ? currPx : (prev * wPrev + curr * wCurr) / sum;
    Output[pixel] = float4(mixed.rgb, 1.0);
}
