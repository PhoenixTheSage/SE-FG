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
    uint HasVolumeReactive;
};

Texture2D PrevColor : register(t0);
Texture2D CurrColor : register(t1);
Texture2D DepthTex : register(t2);
Texture2D MotionTex : register(t3);
Texture2D VolumeReactive : register(t4);
SamplerState LinearClamp : register(s0);
RWTexture2D<float4> Output : register(u0);

float DepthAtUv(float2 uv)
{
    uint width, height;
    DepthTex.GetDimensions(width, height);
    int2 pixel = min(int2(saturate(uv) * float2(width, height)), int2(width - 1, height - 1));
    return DepthTex.Load(int3(pixel, 0)).r;
}

float DepthFootprintConfidence(float2 uv, float centerDepth)
{
    // Color is bilinear. Reject a footprint that mixes foreground/background
    // rather than letting filtered depth invent a surface between them.
    uint width, height;
    DepthTex.GetDimensions(width, height);
    float2 pos = uv * float2(width, height) - 0.5;
    float2 weight = frac(pos);
    // Gather order: lower-left, lower-right, upper-right, upper-left.
    // Fetch the full footprint together instead of four dependent loads.
    float4 samples = DepthTex.GatherRed(LinearClamp, uv);
    if (!all(isfinite(samples)) || !isfinite(centerDepth))
        return 0.0;
    // Complementary depth approaches zero for distant voxels. A 1e-4
    // absolute tolerance accepts their entire silhouette, including sky.
    float centerSignal = InvertedDepth ? centerDepth : 1.0 - centerDepth;
    float4 sampleSignal = InvertedDepth ? samples : 1.0 - samples;
    float scale = max(centerSignal, 1e-7);
    float4 confidence = saturate(1.0 - abs(samples - centerDepth) / (scale * 0.08 + 1e-8));
    confidence *= float4((sampleSignal > 0.0) == (centerSignal > 0.0));
    float4 active = float4(weight.y > 1e-3, all(weight > 1e-3), weight.x > 1e-3, 1.0);
    confidence = lerp(1.0, confidence, active);
    return min(min(confidence.x, confidence.y), min(confidence.z, confidence.w));
}

float ColorHistoryConfidence(float3 previous, float3 current)
{
    if (!all(isfinite(previous)) || !all(isfinite(current)))
        return 0.0;
    float3 difference = abs(previous - current);
    float mismatch = max(difference.r, max(difference.g, difference.b));
    float3 magnitude = max(abs(previous), abs(current));
    float level = max(magnitude.r, max(magnitude.g, magnitude.b));
    // Linear-light tolerance permits small shading/filter differences. A
    // large mismatch is not proof of corresponding streamed/LOD geometry.
    float tolerance = 0.01 + 0.25 * level;
    return 1.0 - smoothstep(tolerance, tolerance * 2.0, mismatch);
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
    // A velocity texel describes one surface. Linear filtering at DRS size
    // creates false motion on stationary pixels next to moving geometry.
    uint motionWidth, motionHeight;
    MotionTex.GetDimensions(motionWidth, motionHeight);
    int2 motionPixel = min(int2(uv * float2(motionWidth, motionHeight)), int2(motionWidth - 1, motionHeight - 1));
    float2 mvNative = MotionTex.Load(int3(motionPixel, 0)).xy;
    float2 mvPx = mvNative * float2(MvScaleX, MvScaleY);
    float magNative = length(mvNative);
    float magOut = length(mvPx);
    if (magNative < 1.25 || magOut > 96.0 || !all(isfinite(mvPx)))
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float2 mvUv = mvPx * float2(InvWidth, InvHeight);
    float2 uvPrev = uv + 0.5 * mvUv;
    float2 uvCurr = uv - 0.5 * mvUv;

    // Keep the complete color-filter footprint inside the image. Clamp is
    // not valid scene coverage and must not smear the edge into a fake frame.
    float2 firstUv = 0.5 * float2(InvWidth, InvHeight);
    float2 lastUv = 1.0 - firstUv;
    if (any(uvPrev < firstUv) || any(uvPrev > lastUv) ||
        any(uvCurr < firstUv) || any(uvCurr > lastUv))
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float centerDepth = DepthAtUv(uv);
    float wPrev = DepthFootprintConfidence(uvPrev, centerDepth);
    float wCurr = DepthFootprintConfidence(uvCurr, centerDepth);
    // There is no previous-depth history to prove disoccluded coverage.
    // Preserve the real pixel when either warp cannot stay on its surface.
    if (min(wPrev, wCurr) < 0.5)
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float historyConfidence = 1.0;
    if ((InvertedDepth ? centerDepth : 1.0 - centerDepth) > 0.0)
    {
        // Validate the previous appearance at the full backward motion,
        // not the two midpoint samples (which need not match each other).
        // This also rejects false persistent motion on stationary detail.
        float2 previousSurfaceUv = uv + mvUv;
        if (any(previousSurfaceUv < firstUv) || any(previousSurfaceUv > lastUv))
        {
            Output[pixel] = float4(currPx.rgb, 1.0);
            return;
        }
        float3 previousSurface = PrevColor.SampleLevel(LinearClamp, previousSurfaceUv, 0).rgb;
        historyConfidence = ColorHistoryConfidence(previousSurface, currPx.rgb);
        // Repeated detail can accidentally match at a wrong full-motion
        // offset. If it already agrees without motion, prefer that evidence
        // instead of making a stationary surface oscillate at the midpoint.
        float3 previousUnwarped = PrevColor.Load(int3(pixel, 0)).rgb;
        if (!all(isfinite(previousUnwarped)))
        {
            Output[pixel] = float4(currPx.rgb, 1.0);
            return;
        }
        float3 stillDifference = abs(previousUnwarped - currPx.rgb);
        float stillMismatch = max(stillDifference.r, max(stillDifference.g, stillDifference.b));
        float3 stillMagnitude = max(abs(previousUnwarped), abs(currPx.rgb));
        float stillTolerance = 0.001 + 0.01 * max(stillMagnitude.r, max(stillMagnitude.g, stillMagnitude.b));
        historyConfidence *= smoothstep(stillTolerance, stillTolerance * 2.0, stillMismatch);
        if (historyConfidence <= 0.0)
        {
            Output[pixel] = float4(currPx.rgb, 1.0);
            return;
        }
    }

    float4 prev = PrevColor.SampleLevel(LinearClamp, uvPrev, 0);
    float4 curr = CurrColor.SampleLevel(LinearClamp, uvCurr, 0);

    float sum = wPrev + wCurr;
    float4 mixed = (prev * wPrev + curr * wCurr) / sum;
    mixed = lerp(currPx, mixed, historyConfidence);
    if (HasVolumeReactive != 0)
    {
        float reject=max(VolumeReactive.SampleLevel(LinearClamp,uv,0).r,
            max(VolumeReactive.SampleLevel(LinearClamp,saturate(uvPrev),0).r,
                VolumeReactive.SampleLevel(LinearClamp,saturate(uvCurr),0).r));
        // A changing participating volume has no single surface motion. Reduce
        // interpolation confidence instead of warping it with the terrain behind it.
        mixed=lerp(mixed,currPx,saturate(reject));
    }
    Output[pixel] = float4(mixed.rgb, 1.0);
}
