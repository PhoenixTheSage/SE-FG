# Space Engineers FrameGen

Pulsar client plugin that adds **open-source frame generation** to Space Engineers 1 (DX11). It is not an upscaler. PluginHub compiles C# only — there is no custom native library.

Official AMD FSR 3 Frame Generation (optical flow + frame-interpolation swapchain) targets DX12 and Vulkan. This plugin runs an SM 5.0 bidirectional-warp interpolator through **SharpDX** on the game's D3D11 device (same pattern as SE-DLSS helper shaders and NGX: C# talks to D3D11; DXBC is baked in the plugin). Then it issues an extra DXGI Present of the interpolated color.

Settings live in the Pulsar plugin dialog. When [Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) and [Rich HUD Master](https://steamcommunity.com/workshop/filedetails/?id=1965654081) are in the world, the same options appear under **Anomaly Shaders → FrameGen → Settings**. This plugin does not vendor a Rich HUD client.

## Requirements

- Space Engineers with [Pulsar](https://github.com/SpaceGT/Pulsar) 2.4.0 or later, Windows, Direct3D feature level 11_0 (NVIDIA, AMD, or Intel — not WARP)
- **VSync off.** An extra Present with VSync on waits a full refresh, so generated frames do not raise displayed FPS.

## Settings

Plugin config:

- **Enabled** — insert one interpolated frame between presented frames
- **Show Status** — GPU, FrameGen context, generate counts, Anomaly velocity

MSAA is incompatible with FrameGen.

Motion vectors are camera-reprojected from depth unless [Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) is also loaded. Anomaly is optional: the plugin attaches or detaches at runtime from type detection, with no config or terminal toggle ([shader developer wiki](https://github.com/PhoenixTheSage/Anomaly/wiki)):

- **Velocity** — `VelocityRegistry.Active` (object motion). Camera-from-depth remains the fallback.
- **History** — `FrameTemporal.InvalidateHistory()` on camera cuts this plugin owns.
- FrameGen does **not** call `ClaimUpscale`. AfterUpscale Display tenants keep the slot.

No compile-time Anomaly reference. FrameGen does not require an NVIDIA GPU; Anomaly itself does not either.

Optional [Rich HUD Master](https://steamcommunity.com/workshop/filedetails/?id=1965654081) plus Anomaly mirrors the Pulsar dialog as **Anomaly Shaders → FrameGen → Settings** via Anomaly's `TerminalConfigRegistry`. Without Anomaly, use Pulsar MyGui. No PluginHub `DependencyIds`. See [Terminal config](https://github.com/PhoenixTheSage/Anomaly/wiki/Terminal-config).

## How it runs

`FrameGenD3d` creates a compute shader from baked DXBC (`FrameGen/Shaders/Interpolate.hlsl`, `fxc cs_5_0`) and dispatches it with SharpDX, the same way camera motion vectors use `Mv.hlsl`. Dilated motion, bidirectional warp to t=0.5, depth disocclusion, previous-color ping-pong. Pulsar / PluginHub compile the C# plugin only.

HUD and particles that lack motion vectors can ghost for one interpolated frame. Camera cuts reset history.

## Building

- .NET Framework 4.8.1 targeting pack and .NET 10 SDK
- Build `ClientPlugin` (deploys to Pulsar `Legacy\Local` or `Interim\Local`; close the game if the DLL is in use)
- To regenerate interpolator DXBC after editing HLSL: `fxc /T cs_5_0 /E CSMain /O3` on `ClientPlugin/FrameGen/Shaders/Interpolate.hlsl`, then paste bytes into `ShaderBytecode.InterpolateCs`

Debug with Pulsar `Legacy.exe` / `Interim.exe` and `-sources`.

## License

Interpolator HLSL is original to this plugin. The algorithm follows AMD FidelityFX Super Resolution 3 Frame Interpolation as documented in the FidelityFX SDK (MIT).

## Known interactions

These plugins patch the same render-thread surfaces. Prefer not enabling them together until a handshake exists.

### SE-DLSS / SE-FRS

Overlap: Present timing and motion-vector consumption. Do not run FrameGen with DLSS or FRS at the same time until a handshake exists. FrameGen is not an upscaler and will not claim Anomaly AfterUpscale.

### HdrRender

FrameGen interpolates the presented backbuffer (post-tonemap / swapchain color). It does not evaluate `hdrColor` or occupy AfterUpscale.

### SmoothFrames

Overlap: extra Present plus camera interpolation. Disable SmoothFrames camera interpolation while FrameGen is on.

[Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) is optional and complementary. It is discovered at runtime by type name (`VelocityRegistry`, `BufferCatalog`, `OwnedPassRegistry`, `FrameTemporal`). Packs that Harmony-patch `MyShader` or leave extra RT/SRV bound will fight Anomaly and can break Rich HUD.

## Bug reports

Open an issue with **Show Status** text, GPU, driver version, and `SpaceEngineers.log`.

Anomaly consumer verification: [Tests/ANOMALY-ACCEPTANCE.md](Tests/ANOMALY-ACCEPTANCE.md).
