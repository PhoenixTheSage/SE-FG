# Graph Report - SE-FG  (2026-09-16)

## Corpus Check
- Corpus is ~37,113 words - fits in a single context window. You may not need a graph.

## Summary
- 802 nodes · 1483 edges · 41 communities (37 shown, 4 thin omitted)
- Extraction: 99% EXTRACTED · 1% INFERRED · 0% AMBIGUOUS · INFERRED: 18 edges (avg confidence: 0.86)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Gpu Support
- Assembly
- Config
- Buffer
- Post Pp Hud Space
- Button
- Read Root A G
- Layout
- Debug Log
- Velocity Acceptance
- Frame Gen Host
- Preloader Helpers
- Element
- Compute G P U
- Frame Gen Runtime
- Config Dialog Example Screenshot
- Debug Log 2
- Control Button Data
- Render Trace Bind
- Slider
- Checkbox
- Hashing
- Camera History
- Draw Scene Patch
- Dropdown
- Tools
- Assembly Definition
- Client Plugin
- Device
- Button 2
- Color
- Element 2
- Separator
- Textbox
- Binding
- Present Patch
- Control
- Microsoft. N E T.
- clean.sh
- Deploy.sh
- verify props.sh

## God Nodes (most connected - your core abstractions)
1. `AnomalyHook` - 48 edges
2. `FrameGenD3d` - 48 edges
3. `FrameGenRuntime` - 33 edges
4. `PostPpHudPass` - 29 edges
5. `ClientPlugin.FrameGen` - 22 edges
6. `VRage.Utils` - 18 edges
7. `FrameGenHost` - 17 edges
8. `SettingsGenerator` - 17 edges
9. `TranspilerHelpers` - 17 edges
10. `GpuSupport` - 16 edges

## Surprising Connections (you probably didn't know these)
- `HLSL Skill` --semantically_similar_to--> `HLSL Skill`  [INFERRED] [semantically similar]
  .agents/skills/a5c-ai-babysitter-hlsl/README.md → .cursor/skills/a5c-ai-babysitter-hlsl/README.md
- `HLSL Skill` --semantically_similar_to--> `HLSL Skill`  [INFERRED] [semantically similar]
  .agents/skills/a5c-ai-babysitter-hlsl/SKILL.md → .cursor/skills/a5c-ai-babysitter-hlsl/SKILL.md
- `Read Root AGENTS.md` --semantically_similar_to--> `Read Root AGENTS.md`  [INFERRED] [semantically similar]
  .github/copilot-instructions.md → .vscode/AGENTS.md
- `ShaderBytecode.cs` --semantically_similar_to--> `ShaderBytecode`  [INFERRED] [semantically similar]
  Assets/README.txt → README.md
- `Read Root AGENTS.md` --references--> `Space Engineers Plugin Developer Instructions`  [EXTRACTED]
  .github/copilot-instructions.md → AGENTS.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Mirrored HLSL Skill Copies** — _agents_skills_a5c_ai_babysitter_hlsl_readme_hlsl_skill, _agents_skills_a5c_ai_babysitter_hlsl_skill_hlsl_skill, _cursor_skills_a5c_ai_babysitter_hlsl_readme_hlsl_skill, _cursor_skills_a5c_ai_babysitter_hlsl_skill_hlsl_skill [INFERRED 0.95]
- **D3D11 Frame Interpolation Pipeline** — readme_framegend3d, readme_interpolate_hlsl, readme_mv_hlsl, readme_copy_hlsl, readme_shaderbytecode [EXTRACTED 1.00]
- **Anomaly Runtime Type Discovery** — readme_velocityregistry, readme_buffercatalog, readme_ownedpassregistry, readme_frametemporal, readme_hudoverlayregistry, readme_terminalconfigregistry [EXTRACTED 1.00]
- **Some Settings Widget Group** — docs_configdialogexample_toggle, docs_configdialogexample_integer, docs_configdialogexample_number, docs_configdialogexample_text, docs_configdialogexample_dropdown [EXTRACTED 1.00]
- **More Settings Widget Group** — docs_configdialogexample_color, docs_configdialogexample_color_with_alpha, docs_configdialogexample_keybind, docs_configdialogexample_button [EXTRACTED 1.00]

## Communities (41 total, 4 thin omitted)

### Community 0 - "Gpu Support"
Cohesion: 0.05
Nodes (28): GpuSupport, AdapterName, CanAttemptFrameGen, CanOfferFrameGen, FeatureLevel, IsAmd, IsNvidia, IsWarp (+20 more)

### Community 1 - "Assembly"
Cohesion: 0.08
Nodes (16): Assembly, AssemblyLoadEventArgs, AnomalyHook, CanNotifyUpscale, CatalogFound, ClaimedUpscale, HasDisplayTenant, RegistryFound (+8 more)

### Community 2 - "Config"
Cohesion: 0.06
Nodes (21): FrameGenStatus, CurrentText, StringBuilder, ShaderBytecode, DeviceDisposePatch, Harmony, MethodInfo, GetDeviceVSyncModePatch (+13 more)

### Community 3 - "Buffer"
Cohesion: 0.13
Nodes (20): Buffer, FrameGenD3d, CurrCopy, HudCopy, InterpCopy, SceneCopy, Device, DeviceContext (+12 more)

### Community 4 - "Post Pp Hud Space"
Cohesion: 0.09
Nodes (18): PostPpHudSpace, List, MyBillboard, BillboardAddPatch, BillboardAddRangePatch, BillboardFrameCompletePatch, BillboardPostPpPatch, PostPpHudPass (+10 more)

### Community 5 - "Button"
Cohesion: 0.06
Nodes (20): Button, Config, Enabled, ShowOverlay, RichHudOptions, ConfigStorage, ConfigFilePath, SettingsScreen (+12 more)

### Community 6 - "Read Root A G"
Cohesion: 0.06
Nodes (45): Read Root AGENTS.md, Read Root AGENTS.md, Anomaly Owns Shared Rendering Gaps, IsolatedMix Energy, March LOD, Space Engineers Plugin Developer Instructions, Rich HUD Config Save Rule, se-dev Skill (+37 more)

### Community 7 - "Layout"
Cohesion: 0.06
Nodes (29): Layout, SettingsPanelSize, Func, List, MyGuiControlBase, Vector2, None, SettingsPanelSize (+21 more)

### Community 8 - "Debug Log"
Cohesion: 0.12
Nodes (15): CodeInstructionNotFound, TranspilerHelpers, CodeInstruction, CodeInstructionPredicate, FieldInfo, IEnumerable, List, MethodInfo (+7 more)

### Community 9 - "Velocity Acceptance"
Cohesion: 0.08
Nodes (18): VelocityAcceptance, Buffer, Convention, Height, HistoryValid, IsAvailable, NativeResource, Width (+10 more)

### Community 10 - "Frame Gen Host"
Cohesion: 0.11
Nodes (14): FrameGenHost, CurrentPresetHint, IsLoaded, IsReady, IsSupported, LastError, SupportKnown, Device (+6 more)

### Community 11 - "Preloader Helpers"
Cohesion: 0.21
Nodes (9): PreloaderHelpers, CodeInstructionPredicate, Instruction, List, Collection, FieldReference, MethodDefinition, MethodReference (+1 more)

### Community 12 - "Element"
Cohesion: 0.20
Nodes (22): Element, Path, _detect_pulsar_dir(), _detect_space_engineers(), _generate_guid(), _get_install_locations(), _get_linux_steam_path(), _get_steam_path() (+14 more)

### Community 13 - "Compute G P U"
Cohesion: 0.10
Nodes (22): Compute GPU Processing, DirectX Shaders, GLSL Skill, HLSL Skill, Shader Optimization, Compute Shaders, Constant Buffer Management, HLSL Documentation (+14 more)

### Community 14 - "Frame Gen Runtime"
Cohesion: 0.11
Nodes (15): FrameGenRuntime, DisplayFps, GameFps, GenerateCount, GeneratedThisFrame, Height, IsLive, LastBindingEvidence (+7 more)

### Community 15 - "Config Dialog Example Screenshot"
Cohesion: 0.14
Nodes (16): Config Dialog Example Screenshot, Button Control, Dialog Close Button, Color Picker, Color With Alpha, Config Demo Dialog, Dropdown Control, Integer Slider (+8 more)

### Community 16 - "Debug Log 2"
Cohesion: 0.25
Nodes (7): DebugLog, FilePath, FrameSite, Conditional, Dictionary, FrameSite, StreamWriter

### Community 17 - "Control Button Data"
Cohesion: 0.25
Nodes (10): ControlButtonData, KeybindAttribute, SupportedTypes, Action, Func, List, Type, MyControl (+2 more)

### Community 18 - "Render Trace Bind"
Cohesion: 0.29
Nodes (3): RenderTraceBind, Exception, MethodInfo

### Community 19 - "Slider"
Cohesion: 0.18
Nodes (10): SliderAttribute, SupportedTypes, SliderType, Float, Integer, Action, Func, List (+2 more)

### Community 20 - "Checkbox"
Cohesion: 0.20
Nodes (9): Attribute, CheckboxAttribute, SupportedTypes, Action, Func, List, Type, IgnoresAccessChecksToAttribute (+1 more)

### Community 21 - "Hashing"
Cohesion: 0.27
Nodes (7): Hashing, CodeInstruction, IEnumerable, Instruction, MethodImpl, MethodInfo, ConstructorInfo

### Community 22 - "Camera History"
Cohesion: 0.22
Nodes (8): CameraHistory, HasPrevious, InvViewProjection, PreviousViewProjection, ViewProjection, Matrix, Vector3, Vector3D

### Community 23 - "Draw Scene Patch"
Cohesion: 0.22
Nodes (3): DrawScenePatch, HarmonyPostfix, HarmonyPrefix

### Community 24 - "Dropdown"
Cohesion: 0.28
Nodes (6): DropdownAttribute, SupportedTypes, Action, Func, List, Type

### Community 25 - "Tools"
Cohesion: 0.31
Nodes (4): Tools, Color, Match, Regex

### Community 26 - "Assembly Definition"
Cohesion: 0.25
Nodes (4): AssemblyDefinition, IEnumerable, Preloader, TargetDLLs

### Community 27 - "Client Plugin"
Cohesion: 0.25
Nodes (6): ClientPlugin, net10.0, net48, Krafs.Publicizer (2.3.0), Lib.Harmony (2.4.2), Mono.Cecil (0.11.6)

### Community 28 - "Device"
Cohesion: 0.29
Nodes (5): Device, DeviceContext, IntPtr, Resource, Texture2D

### Community 29 - "Button 2"
Cohesion: 0.29
Nodes (6): ButtonAttribute, SupportedTypes, Action, Func, List, Type

### Community 30 - "Color"
Cohesion: 0.29
Nodes (7): ColorAttribute, SupportedTypes, Action, Color, Func, List, Type

### Community 31 - "Element 2"
Cohesion: 0.29
Nodes (6): IElement, SupportedTypes, Action, Func, List, Type

### Community 32 - "Separator"
Cohesion: 0.29
Nodes (6): SeparatorAttribute, SupportedTypes, Action, Func, List, Type

### Community 33 - "Textbox"
Cohesion: 0.29
Nodes (6): TextboxAttribute, SupportedTypes, Action, Func, List, Type

### Community 34 - "Binding"
Cohesion: 0.39
Nodes (3): Binding, IMyInput, MyKeys

### Community 35 - "Present Patch"
Cohesion: 0.29
Nodes (3): PresentPatch, HarmonyPostfix, HarmonyPrefix

### Community 36 - "Control"
Cohesion: 0.40
Nodes (4): Control, MyGuiControlBase, Vector2, MyGuiDrawAlignEnum

## Knowledge Gaps
- **133 isolated node(s):** `net10.0`, `net48`, `Lib.Harmony (2.4.2)`, `Mono.Cecil (0.11.6)`, `Krafs.Publicizer (2.3.0)` (+128 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 267 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **4 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `VRage.Utils` connect `Config` to `Control`, `Debug Log`, `Velocity Acceptance`, `Frame Gen Host`, `Slider`, `Button 2`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **Why does `ClientPlugin.FrameGen` connect `Config` to `Present Patch`, `Post Pp Hud Space`, `Debug Log`, `Velocity Acceptance`, `Frame Gen Host`, `Camera History`, `Draw Scene Patch`?**
  _High betweenness centrality (0.140) - this node is a cross-community bridge._
- **Why does `System.Runtime.CompilerServices` connect `Debug Log` to `Config`?**
  _High betweenness centrality (0.136) - this node is a cross-community bridge._
- **What connects `net10.0`, `net48`, `Lib.Harmony (2.4.2)` to the rest of the system?**
  _133 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Gpu Support` be split into smaller, more focused modules?**
  _Cohesion score 0.052597402597402594 - nodes in this community are weakly interconnected._
- **Should `Assembly` be split into smaller, more focused modules?**
  _Cohesion score 0.08385744234800839 - nodes in this community are weakly interconnected._
- **Should `Config` be split into smaller, more focused modules?**
  _Cohesion score 0.06009783368273934 - nodes in this community are weakly interconnected._