# Anomaly consumption acceptance

Contract checked against the adjacent Anomaly checkout:
`wiki/Velocity-contract.md`, `ClientPlugin/Velocity/IVelocityBuffer.cs`,
`VelocityConvention.cs`, and `Config.cs`.

The required convention is exactly 15: unjittered, pixel units, Y down,
current-to-previous. `previousPixel = currentPixel + motion`. FrameGen scale is
(+1,+1). Legacy 7 and unknown flags fall back to camera-from-depth. The catalog
cannot certify conventions and is no longer a velocity fallback. VelocityProbe
must be readable and Off. Missing probe metadata fails closed.

## Automated contract checks

Run `dotnet run --project Tests/AnomalyAcceptance.csproj`.
The harness links the production reflection consumer and reset policy. It checks
conventions, all four current probes, unknown probe metadata, resize rejection,
resource replacement, invalid history, unavailable/null buffers, cuts and source resets.
It does not exercise D3D, FrameGen, or certify rendered image quality.

## In-game acceptance (pending)

Use the same scene and exposure for every comparison. Disable unrelated
rendering plugins for the baseline. Record Anomaly revision, FrameGen revision,
GPU/driver, resolution, and the Show Status binding snapshot. Turn VSync off.

1. **Reprojection:** capture stationary geometry, fixed-camera grids moving right
   and down, camera translation/rotation over static voxels, and characters.
   Convention 15: `previousPixel = currentPixel + motion`. A point that moved
   +3 pixels right has motion `(-3, 0)`. Camera-only paths cannot reproduce
   independent object motion; use that as a control.
2. **Ghosting:** capture moving grid edges, characters, and HUD. HUD without
   motion vectors can ghost for one interpolated frame; annotate that separately.
3. **Camera cuts:** teleport and rotate abruptly. Require camera-cut or invalid-
   history reset on the first generated post-cut frame and no old-scene trail.
4. **Resolution:** change window resolution. Require rejection of old-size buffers
   and a history reset, then matching dimensions.
5. **Source/probe changes:** switch GBuffer/CameraOnly/local fallback. Require a
   source reset even when switching between Anomaly modes.

Binding evidence reports selected source, dimensions, reset causes and FrameGen
result. Show Status is available in Release; Debug builds also sample evidence
in `SpaceEngineersFrameGen.debug.log`.
