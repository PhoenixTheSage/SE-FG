# Space Engineers FrameGen

Pulsar client plugin that adds **open-source frame generation** to Space Engineers 1 (DX11). It is not an upscaler. PluginHub compiles C# only — there is no custom native library.

Official AMD FSR 3 Frame Generation (optical flow + frame-interpolation swapchain) targets DX12 and Vulkan. This plugin runs an SM 5.0 bidirectional-warp interpolator through **SharpDX** on the game's D3D11 device (same pattern as SE-DLSS helper shaders and NGX: C# talks to D3D11; DXBC is baked in the plugin). Then it issues an extra DXGI Present of the interpolated color.

Settings live in the Pulsar plugin dialog. When [Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) and [Rich HUD Master](https://steamcommunity.com/workshop/filedetails/?id=1965654081) are in the world, the same options appear under **Anomaly Shaders → FrameGen → Settings**, and a corner overlay shows game vs displayed FPS. This plugin does not vendor a Rich HUD client.

## Requirements

- Space Engineers with [Pulsar](https://github.com/SpaceGT/Pulsar) 2.4.0 or later, Windows, Direct3D feature level 11_0 (NVIDIA, AMD, or Intel — not WARP)
- **VSync off.** An extra Present with VSync on waits a full refresh, so generated frames do not raise displayed FPS.

## Settings

Plugin config:

- **Enabled** — insert one interpolated frame between presented frames
- **Show FPS overlay** — corner game / displayed FPS when Anomaly and Rich HUD Master are loaded
- **Show Status** — GPU, FrameGen context, generate counts, Anomaly velocity

MSAA is incompatible with FrameGen.

Motion vectors are camera-reprojected from depth at swapchain size, or Anomaly `VelocityRegistry.Active` when convention 15 and history is valid. [Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) is optional: the plugin attaches or detaches at runtime from type detection, with no config or terminal toggle ([shader developer wiki](https://github.com/PhoenixTheSage/Anomaly/wiki)):

- **Velocity** — Anomaly gbuffer velocity (camera + object) UV-sampled and scaled by buffer `Width`/`Height`. Camera-from-depth if Anomaly is missing, probing, or history is invalid.
- **History** — `FrameTemporal.InvalidateHistory()` on camera cuts this plugin owns.
- FrameGen does **not** call `ClaimUpscale` or `NotifyUpscaleComplete`. AfterUpscale Display tenants keep the slot. `hdrColor` / `reactiveMask` readers stay for a later upscaler handshake.
- **Overlay** — `HudOverlayRegistry` corner line (`se-framegen`) when Master is registered.

No compile-time Anomaly reference. FrameGen does not require an NVIDIA GPU; Anomaly itself does not either.

Optional [Rich HUD Master](https://steamcommunity.com/workshop/filedetails/?id=1965654081) plus Anomaly mirrors the Pulsar dialog as **Anomaly Shaders → FrameGen → Settings** via Anomaly's `TerminalConfigRegistry`. Without Anomaly, use Pulsar MyGui. No PluginHub `DependencyIds`. See [Terminal config](https://github.com/PhoenixTheSage/Anomaly/wiki/Terminal-config).

## How it runs

`FrameGenD3d` creates a compute shader from baked DXBC (`FrameGen/Shaders/Interpolate.hlsl`, `fxc cs_5_0`) and dispatches it with SharpDX, the same way camera motion vectors use `Mv.hlsl`. Bidirectional warp to t=0.5 with depth disocclusion; near-zero motion copies the current scene. The interpolator reads the **scene** snapshot taken from the LDR PostPP target *before* Keen blends Rich HUD, copied or stretched to the **DXGI swapchain** size. Anomaly velocity is RG16F pixel delta at **internal / DRS** size; FrameGen UV-samples it and converts `mvUv = mvPx / velSize` (`MvScale = output/internal`). Camera-from-depth at swapchain size is the fallback. DLSS/FRS `SetDRS` makes `Backbuffer.Size` follow internal `ResolutionI`; FrameGen does not use that for UAV or Present size. Depth is sampled in UV so gbuffer (internal) still matches the upscaled color. After Keen `Present`, `Copy.hlsl` blits that interpolant through an sRGB RTV (sample-by-UV so a leftover internal UAV cannot leave a black pillar). Bucket 4 persistents are drawn once onto that interpolant (`TryDrawOnOutput`, no DSV). Keen GUI sprites are then pixel-diff restored (`HudCopy` vs the post-CopyToRT copy). Doing pixel-diff *before* persistents baked Rich HUD into the interpolant and stacked `UiBkOpacity`. Matching sprite-diff pixels keep the persistents; differing pixels replace with `HudCopy` (still one layer). `SrgbIn` stays 0 — Hud/Scene `*_SRGB` Loads are already linear; a second `SrgbToLinear` crushes Keen HUD to black. SceneCopy is taken from the LDR target *before* Keen PostPP using the RTV's sRGB view (custom LDR is TYPELESS) so the interpolant stays HUD-free. Do not snapshot the backbuffer after CopyToRT as the interpolant fallback — native FXAA has already blended PostPP. The interpolant UAV is `R16G16B16A16_Float` (linear); do not `CopyResource` it onto an `*_SRGB` backbuffer and do not fall back to an 8-bit UNORM UAV — that quantizes dark glow to ~13/255 sRGB. The render thread does not spin-wait a half frame (that path halves game FPS and flashes the interpolant before the real frame). When the game already fills the monitor (native FPS approaching the display Hz), the extra Present is skipped — a second flip in that refresh queues the interpolant on top of the real frame and ghosts. Particles drawn in the scene pass still follow scene motion vectors. Camera cuts reset history. Near-zero motion (measured in the velocity buffer's own texels, before DRS→output scale) copies the current scene. Pulsar / PluginHub compile the C# plugin only.

## Building

- .NET Framework 4.8.1 targeting pack and .NET 10 SDK
- Build `ClientPlugin` (deploys to Pulsar `Legacy\Local` or `Interim\Local`; close the game if the DLL is in use)
- To regenerate interpolator DXBC after editing HLSL: `fxc /T cs_5_0 /E CSMain /O3` on `ClientPlugin/FrameGen/Shaders/Interpolate.hlsl`, then paste bytes into `ShaderBytecode.InterpolateCs`. Composite blit: `fxc /T ps_5_0 /E PSMain` on `Copy.hlsl` → `ShaderBytecode.CopyPs`.

Debug with Pulsar `Legacy.exe` / `Interim.exe` and `-sources`.

## License

Interpolator HLSL is original to this plugin. The algorithm follows AMD FidelityFX Super Resolution 3 Frame Interpolation as documented in the FidelityFX SDK (MIT).

## Known interactions

These plugins patch the same render-thread surfaces. Prefer not enabling them together until a handshake exists.

### SE-DLSS / SE-FRS

Overlap: Present timing and motion-vector consumption. Do not run FrameGen with DLSS or FRS at the same time until a handshake exists. FrameGen is not an upscaler and will not claim Anomaly AfterUpscale.

### HdrRender

FrameGen interpolates the post-tonemap scene snapshot (swapchain color after `DrawScene`). It does not evaluate `hdrColor` or occupy AfterUpscale.

### SmoothFrames

Overlap: extra Present plus camera interpolation. Disable SmoothFrames camera interpolation while FrameGen is on.

[Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) is optional and complementary. It is discovered at runtime by type name (`VelocityRegistry`, `BufferCatalog`, `OwnedPassRegistry`, `FrameTemporal`). Packs that Harmony-patch `MyShader` or leave extra RT/SRV bound will fight Anomaly and can break Rich HUD.

## Bug reports

Open an issue with **Show Status** text, GPU, driver version, and `SpaceEngineers.log`.

Anomaly consumer verification: [Tests/ANOMALY-ACCEPTANCE.md](Tests/ANOMALY-ACCEPTANCE.md).

## FG dark-color precision fix (2026-09-18)

The generated-frame UAV is always RGBA16F linear light, including SDR output.
Previously SDR sRGB inputs were decoded into an 8-bit linear UNORM UAV before
presentation re-encoded them to sRGB. The first nonzero linear code became
approximately 13/255 sRGB, erasing dim stars and making warm glare gradients
step abruptly. Motion changes the sampled values and hence the quantization
error. No lossy UNORM fallback is allowed if floating-point allocation fails.
The output RTV still performs the existing SDR encoding; HDR stays floating point.
Status binding evidence now records input and interpolation formats.

Run `dotnet run --project Tests/ColorRegression.csproj -c Release` for actual
D3D11 WARP capture/interpolation/presentation checks using production code and
baked shaders. Twelve cases cover faint stars and dark gradients in SDR/HDR,
stationary, integer and fractional motion. The old UAV format fails the test;
the float fix passes. These tests do not exercise DXGI display pacing or HUD.
The user isolated the reported sun ring and movement brightness to FG; confirm
both in-game after updating SE-FG. No FSR or celestial motion-vector change is
part of this fix. RGBA16F increases SDR interpolation-buffer storage by four
bytes per output pixel (about 8 MiB at 1080p).

## Review fixes and regression checks

Camera-from-depth fallback includes the camera-origin delta in the previous
projection; the baked motion shader matches the source's current-to-previous
direction. Raw D3D passes isolate and restore the host pipeline state, including
blend/write-mask and scissor behavior. Failed or missed generation requires a
fresh color-history seed. Insertion failures accumulate until a complete extra
Present succeeds; a failed Keen sprite restore suppresses extra Present.

Optional Anomaly discovery retries on assembly load or after two seconds,
including terminal/overlay registration. Owned color views and one external
motion view are reused and released on replacement/resize/shutdown. Swapchain
RTVs remain temporary so they cannot prevent `ResizeBuffers`.

Run the independent harnesses:

- `dotnet run --project Tests/ColorRegression.csproj -c Release`
- `dotnet run --project Tests/AnomalyAcceptance.csproj -c Release`
- `dotnet run --project Tests/RuntimeRegression.csproj -c Release`
- `dotnet run --project Tests/WarpRegression.csproj -c Release`

The runtime harness uses production history, failure policy, graphics code and
baked shaders with a stubbed game boundary and real D3D11 WARP. It checks camera
translation at large world coordinates, direction, cuts, inherited state and
restoration (context-state swaps and the legacy fallback), discovery retries,
failure recovery, and resizing/replaced velocity resources. These checks do not
certify live HUD composition, display pacing, or input latency. Existing Present
order, RGBA16F precision, sRGB behavior, and one-layer PostPP composition remain.

## Edge-warp safety (2026-10-01)

The interpolator preserves the real current pixel when either warped color
footprint leaves the image or crosses a rejected depth boundary. It no longer
forces a minimum current-sample weight after rejecting that sample. Depth
confidence checks the individual contributing filter taps, so a thin rail and
the wall behind it cannot produce an invented blended depth. Motion uses the
nearest producer texel to avoid creating false motion between surfaces at DRS
size; the existing pixel-to-UV scale and sub-pixel threshold remain.

The HLSL, shipped `ShaderBytecode.InterpolateCs`, and checked-in bake artifacts
are regenerated together. No new textures or shader bindings are required.
`WarpRegression` covers all four screen borders, fractional filter footprints,
horizontal/vertical/diagonal depth boundaries, stationary pixels next to moving
internal-resolution motion, odd dispatch sizes, and valid interior warping in
SDR/HDR. `RuntimeRegression` also verifies stationary and moving perspective
camera reprojection at the center and corners against CPU math.

These are automated reproductions of unsafe sampling conditions. Confirm the
reported scene with the updated plugin in-game; the tests do not certify live
Anomaly velocity output or establish that every cause of flicker is resolved.

## Distant surfaces and changed history (2026-10-01)

Complementary-depth rejection uses a relative tolerance with a small numerical
floor and explicitly distinguishes surface coverage from sky. The former
`1e-4` absolute floor admitted sky and different voxel surfaces at asteroid
distances even after the edge-warp fix.

On geometry, color history is checked at the full backward-motion position
before midpoint interpolation. Large appearance mismatches gradually reduce
history confidence; unchanged appearance at the same pixel also suppresses
unproven motion on repeating stationary detail. Uncertain pixels keep their
current real color. This is a conservative color-consistency heuristic, not a
voxel-LOD event detector. It can also reduce interpolation during changing
shading or aliased detail. Sky/star color behavior remains on its existing path.

Color history continues advancing during per-pixel rejection so coherent motion
can resume after an appearance replacement. The check uses existing textures;
it adds color reads but no GPU buffers or CPU readback. It does not alter Keen
streaming, voxel LOD, Anomaly motion production, or global reset/presentation
rules. The regression suite includes distant asteroid/sky and neighboring voxel
depths, replaced appearance, persistent false motion on stationary detail, and
successful recovery and coherent near/far motion. Live confirmation remains
necessary because the reported intermittent scene has not been captured here.

## Ship silhouettes and paired depth history (2026-10-01)

Orbiting the third-person camera around a stopped or piloted ship can expose
pixels that previously contained the ship. Checking both color images against
only current depth admitted the old silhouette into current sky, and admitted
sky history into newly exposed geometry. This reproduced in six SDR/HDR GPU
cases, including fractional filter footprints.

FrameGen now retains native depth alongside previous color. Each warped color
footprint is checked against depth from its own real frame; the existing current
silhouette guard remains. Rejected pixels keep their current real color, and
both histories continue advancing together. Internal-depth size or format
changes reseed both histories even when the output color size stays fixed.
Missing depth invalidates the pair. Cached history is released on resize or
shutdown, and the new CS SRV slot is restored with the host pipeline state.

This adds one GPU depth copy per captured frame and one depth gather on moving
pixels. Native R32G8X24 history costs about 15.8 MiB at 1080p or 28.1 MiB at
1440p; internal/DRS depth uses its own smaller dimensions. A standalone RTX 5070
Ti benchmark with alternating coherent patterns measured approximately
0.03–0.04 ms more interpolation time at 1080p and 0.06–0.07 ms at 1440p against
1.4.1. These timings exclude game contention, HUD, and DXGI presentation.

The relative depth comparison is conservative: changing camera/surface depth
can suppress otherwise valid interpolation. It does not reconstruct hidden
surfaces or solve motion at intermediate silhouettes. `WarpRegression` checks
ship/sky history rejection and recovery while coherent interior motion remains
active. `RuntimeRegression` checks native depth formats, internal-depth resize,
missing-depth recovery, and the new binding on both D3D state paths. Live
camera-orbit confirmation with Anomaly/GBuffer remains necessary.
