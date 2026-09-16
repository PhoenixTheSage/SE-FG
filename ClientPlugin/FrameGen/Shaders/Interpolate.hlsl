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

static const int2 kOff[8] =
{
    int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1),
    int2(1, 1), int2(-1, 1), int2(1, -1), int2(-1, -1)
};

float DepthAtUv(float2 uv)
{
    return DepthTex.SampleLevel(LinearClamp, saturate(uv), 0).r;
}

float2 DilateMotion(float2 uv)
{
    float best = DepthAtUv(uv);
    float2 bestUv = uv;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float2 p = uv + float2(kOff[i]) * float2(InvWidth, InvHeight);
        float d = DepthAtUv(p);
        bool nearer = InvertedDepth ? (d > best) : (d < best);
        if (nearer)
        {
            best = d;
            bestUv = p;
        }
    }
    return MotionTex.SampleLevel(LinearClamp, saturate(bestUv), 0).xy;
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
    float2 uv = (float2(pixel) + 0.5) * float2(InvWidth, InvHeight);
    float2 mvPx = DilateMotion(uv) * float2(MvScaleX, MvScaleY);
    float2 mvUv = mvPx * float2(InvWidth, InvHeight);

    float2 uvPrev = uv + 0.5 * mvUv;
    float2 uvCurr = uv - 0.5 * mvUv;

    float4 prev = PrevColor.SampleLevel(LinearClamp, uvPrev, 0);
    float4 curr = CurrColor.SampleLevel(LinearClamp, uvCurr, 0);
    float centerDepth = DepthAtUv(uv);

    float wPrev = DepthDisocclude(DepthAtUv(uvPrev), centerDepth);
    float wCurr = DepthDisocclude(DepthAtUv(uvCurr), centerDepth);

    bool prevIn = all(uvPrev >= 0.0) && all(uvPrev <= 1.0);
    bool currIn = all(uvCurr >= 0.0) && all(uvCurr <= 1.0);
    wPrev *= prevIn ? 1.0 : 0.0;
    wCurr *= currIn ? 1.0 : 0.0;

    float sum = wPrev + wCurr;
    float4 mixed;
    if (sum < 1e-3)
        mixed = CurrColor.Load(int3(pixel, 0));
    else
        mixed = (prev * wPrev + curr * wCurr) / sum;

    Output[pixel] = float4(mixed.rgb, 1.0);
}
