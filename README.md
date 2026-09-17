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

`FrameGenD3d` creates a compute shader from baked DXBC (`FrameGen/Shaders/Interpolate.hlsl`, `fxc cs_5_0`) and dispatches it with SharpDX, the same way camera motion vectors use `Mv.hlsl`. Bidirectional warp to t=0.5 with depth disocclusion; near-zero motion copies the current scene. The interpolator reads the **scene** snapshot taken after `DrawScene` / CopyToRT (before `ConsumeMainSprites`) at the **DXGI swapchain** size. Anomaly velocity is RG16F pixel delta at **internal / DRS** size; FrameGen UV-samples it and converts `mvUv = mvPx / velSize` (`MvScale = output/internal`). Camera-from-depth at swapchain size is the fallback. DLSS/FRS `SetDRS` makes `Backbuffer.Size` follow internal `ResolutionI`; FrameGen does not use that for UAV or Present size. Depth is sampled in UV so gbuffer (internal) still matches the upscaled color. After Keen `Present`, `Copy.hlsl` blits that interpolant through an sRGB RTV (sample-by-UV so a leftover internal UAV cannot leave a black pillar) and restores HUD pixels that differ from the scene copy. Do not `CopyResource` UNORM UAV color onto an `*_SRGB` backbuffer — that double-encodes and crushes midtones. The render thread does not spin-wait a half frame (that path halves game FPS and flashes the interpolant before the real frame). When the game already fills the monitor (native FPS approaching the display Hz), the extra Present is skipped — a second flip in that refresh queues the interpolant on top of the real frame and ghosts. Pulsar / PluginHub compile the C# plugin only.

HUD sprites are not warped on the displayed interpolant: Keen draws PostPP once (FrameGen does not skip+redraw — that stacked UiBkOpacity). SceneCopy is taken from the LDR target *before* Keen PostPP so it stays HUD-free; after Keen Present, Copy.hlsl restores HUD pixels that differ from that clean scene. If the game already fills the monitor so extra Present is skipped, there is no interpolant. Particles drawn in the scene pass still follow scene motion vectors. Camera cuts reset history. Near-zero motion (measured in the velocity buffer's own texels, before DRS→output scale) copies the current scene.

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
