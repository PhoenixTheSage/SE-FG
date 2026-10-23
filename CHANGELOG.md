# SE-FG changelog

## 1.5.2 — 2026-10-23

- Retry Vulkan status discovery when the native module has not loaded yet instead of permanently latching it missing.
- Release the extra-present request on disposal.
- Remove the stale blanket reactive-mask absence line; renderer evidence already reports current volume-reactive availability.

## 1.5.1 — 2026-10-14

- Bind the DX11 companion to the live Pulsar core and release its assembly resolver on disposal.
- Guard device invocation by renderer route and preserve readable companion failures in the shared status report.
- Build/deploy matched companions with separate NuGet/intermediate state and valid game access grants; remove unnecessary shared Publicizer use.
- Move diagnostics to a dedicated Status page and add local/source companion regression coverage for FrameGen, HDR, SSGI, and SE-RT.

## 1.5.0 — previous release

Release 1.5.0 Vulkan frame generation

Version: 1.4.2 -> 1.5.0 (minor).

Changelog:
- Move the DX11 interpolant into SpaceEngineersFrameGen.Dx11.dll.
- On Vulkan, request the same bidirectional warp and its extra immediate present through Anomaly display services.
- Sample catalog velocity when history is valid, and seed when InvalidateHistory clears it.

Co-authored-by: Cursor <cursoragent@cursor.com>
