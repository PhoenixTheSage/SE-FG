# Pulsar companion initialization

Run the harness against an actual built plugin directory and game Bin64:

```powershell
dotnet run --project Tests/SatelliteRegression.csproj -f net10.0 -- <plugin-bin-directory> <game-Bin64> SpaceEngineersFrameGen sources
```

Use net48 for Legacy outputs and net10.0 for Interim outputs. The assembly name
argument also accepts HdrOutput, Prism.SSGI, and SERT. Run each with both sources
(a generated Pulsar assembly identity) and local (the normal built identity).
The harness lives in SE-FG and can inspect the adjacent projects' output folders.

Checks inspect the built access attributes, refuse device loading on pending and
Vulkan routes, bind a companion to the live core, and exercise safe callbacks
without a GPU device. FrameGen also checks that an initialization error produces
readable status text. SERT verifies its live backend interface and constructor.

The October 14 failure was an empty IgnoresAccessChecksTo assembly name emitted
by Publicizer 2.3. A nonempty, declared game-assembly grant remains necessary
where code accesses internal game members. Directory.Build.targets derives
those grants from Publicize items, including when the input was already publicized.
The shared FrameGen assembly no longer needs Publicizer.

Companions are built for the matching runtime with separate obj/NuGet state.
Source-loaded companions bind to Pulsar's live core rather than loading a
second disk-built Config. Legacy needs an exact assembly resolver for that
in-memory reference. Device calls remain guarded by Anomaly's operational route.

These tests do not run the game's UI, GPU frame insertion, or Vulkan rendering.
After restarting Pulsar, check FrameGen's Show Status and checkbox from both
Pulsar and Rich HUD on DX11, then check guest status in a Vulkan session.
