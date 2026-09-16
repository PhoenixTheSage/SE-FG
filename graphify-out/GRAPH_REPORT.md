# Graph Report - SE-FG  (2026-09-16)

## Corpus Check
- 69 files · ~37,279 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 814 nodes · 1503 edges · 46 communities (38 shown, 8 thin omitted)
- Extraction: 99% EXTRACTED · 1% INFERRED · 0% AMBIGUOUS · INFERRED: 18 edges (avg confidence: 0.86)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `9c357eff`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .Write
- AnomalyHook
- ClientPlugin.FrameGen
- FrameGenD3d
- PostPpHudPass
- SettingsScreen
- Space Engineers FrameGen
- Layout
- TranspilerHelpers
- Buffer
- FrameGenHost
- PreloaderHelpers
- setup.py
- HLSL Skill
- FrameGenRuntime
- Config Demo Dialog
- SettingsGenerator
- KeybindAttribute
- RenderTraceBind
- SliderAttribute
- Attribute
- Hashing
- CameraHistory
- .Prefix
- DropdownAttribute
- Tools
- Preloader
- ClientPlugin
- .CaptureScene
- ButtonAttribute
- ColorAttribute
- IElement
- SeparatorAttribute
- TextboxAttribute
- Binding
- .OnPresent
- Control
- AnomalyAcceptance.csproj
- clean.sh
- Deploy.sh
- verify_props.sh
- ClientPlugin.Settings.Elements
- FrameGenStatus
- .RefreshFromDesktop
- .ProbeDisplayRefreshHz
- .Postfix

## God Nodes (most connected - your core abstractions)
1. `AnomalyHook` - 48 edges
2. `FrameGenD3d` - 48 edges
3. `FrameGenRuntime` - 41 edges
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
- `ShaderBytecode.cs` --semantically_similar_to--> `ShaderBytecode`  [INFERRED] [semantically similar]
  Assets/README.txt → README.md
- `Read Root AGENTS.md` --semantically_similar_to--> `Read Root AGENTS.md`  [INFERRED] [semantically similar]
  .github/copilot-instructions.md → .vscode/AGENTS.md
- `Camera Cut Acceptance` --conceptually_related_to--> `FrameTemporal`  [INFERRED]
  Tests/ANOMALY-ACCEPTANCE.md → README.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Anomaly Runtime Type Discovery** — readme_velocityregistry, readme_buffercatalog, readme_ownedpassregistry, readme_frametemporal, readme_hudoverlayregistry, readme_terminalconfigregistry [EXTRACTED 1.00]
- **More Settings Widget Group** — docs_configdialogexample_color, docs_configdialogexample_color_with_alpha, docs_configdialogexample_keybind, docs_configdialogexample_button [EXTRACTED 1.00]
- **Some Settings Widget Group** — docs_configdialogexample_toggle, docs_configdialogexample_integer, docs_configdialogexample_number, docs_configdialogexample_text, docs_configdialogexample_dropdown [EXTRACTED 1.00]
- **D3D11 Frame Interpolation Pipeline** — readme_framegend3d, readme_interpolate_hlsl, readme_mv_hlsl, readme_copy_hlsl, readme_shaderbytecode [EXTRACTED 1.00]
- **Mirrored HLSL Skill Copies** — _agents_skills_a5c_ai_babysitter_hlsl_readme_hlsl_skill, _agents_skills_a5c_ai_babysitter_hlsl_skill_hlsl_skill, _cursor_skills_a5c_ai_babysitter_hlsl_readme_hlsl_skill, _cursor_skills_a5c_ai_babysitter_hlsl_skill_hlsl_skill [INFERRED 0.95]

## Communities (46 total, 8 thin omitted)

### Community 0 - ".Write"
Cohesion: 0.06
Nodes (25): Config, Enabled, ShowOverlay, DebugLog, FilePath, FrameSite, Plugin, Instance (+17 more)

### Community 1 - "AnomalyHook"
Cohesion: 0.07
Nodes (20): Assembly, AssemblyLoadEventArgs, AnomalyHook, CanNotifyUpscale, CatalogFound, ClaimedUpscale, HasDisplayTenant, RegistryFound (+12 more)

### Community 2 - "ClientPlugin.FrameGen"
Cohesion: 0.07
Nodes (17): ShaderBytecode, DrawPatch, DrawScenePatch, GetDeviceVSyncModePatch, HarmonyPrefix, PresentPatch, HudOverlayBind, Installed (+9 more)

### Community 3 - "FrameGenD3d"
Cohesion: 0.13
Nodes (20): Buffer, FrameGenD3d, CurrCopy, HudCopy, InterpCopy, SceneCopy, Device, DeviceContext (+12 more)

### Community 4 - "PostPpHudPass"
Cohesion: 0.08
Nodes (18): PostPpHudSpace, List, MyBillboard, BillboardAddPatch, BillboardAddRangePatch, BillboardFrameCompletePatch, BillboardPostPpPatch, PostPpHudPass (+10 more)

### Community 5 - "SettingsScreen"
Cohesion: 0.08
Nodes (16): Button, RichHudOptions, ConfigStorage, ConfigFilePath, SettingsScreen, Func, List, MyGuiControlBase (+8 more)

### Community 6 - "Space Engineers FrameGen"
Cohesion: 0.06
Nodes (45): Read Root AGENTS.md, Read Root AGENTS.md, Anomaly Owns Shared Rendering Gaps, IsolatedMix Energy, March LOD, Space Engineers Plugin Developer Instructions, Rich HUD Config Save Rule, se-dev Skill (+37 more)

### Community 7 - "Layout"
Cohesion: 0.10
Nodes (18): Layout, SettingsPanelSize, Func, List, MyGuiControlBase, Vector2, None, SettingsPanelSize (+10 more)

### Community 8 - "TranspilerHelpers"
Cohesion: 0.17
Nodes (13): CodeInstructionNotFound, TranspilerHelpers, CodeInstruction, CodeInstructionPredicate, FieldInfo, IEnumerable, List, MethodInfo (+5 more)

### Community 9 - "Buffer"
Cohesion: 0.09
Nodes (17): VelocityAcceptance, Buffer, Convention, Height, HistoryValid, IsAvailable, NativeResource, Width (+9 more)

### Community 10 - "FrameGenHost"
Cohesion: 0.05
Nodes (30): FrameGenHost, CurrentPresetHint, IsLoaded, IsReady, IsSupported, LastError, SupportKnown, Device (+22 more)

### Community 11 - "PreloaderHelpers"
Cohesion: 0.21
Nodes (9): PreloaderHelpers, CodeInstructionPredicate, Instruction, List, Collection, FieldReference, MethodDefinition, MethodReference (+1 more)

### Community 12 - "setup.py"
Cohesion: 0.20
Nodes (22): Element, Path, _detect_pulsar_dir(), _detect_space_engineers(), _generate_guid(), _get_install_locations(), _get_linux_steam_path(), _get_steam_path() (+14 more)

### Community 13 - "HLSL Skill"
Cohesion: 0.10
Nodes (22): Compute GPU Processing, DirectX Shaders, GLSL Skill, HLSL Skill, Shader Optimization, Compute Shaders, Constant Buffer Management, HLSL Documentation (+14 more)

### Community 14 - "FrameGenRuntime"
Cohesion: 0.11
Nodes (16): FrameGenRuntime, DisplayFps, DisplayRefreshHz, GameFps, GenerateCount, GeneratedThisFrame, Height, IsLive (+8 more)

### Community 15 - "Config Demo Dialog"
Cohesion: 0.14
Nodes (16): Config Dialog Example Screenshot, Button Control, Dialog Close Button, Color Picker, Color With Alpha, Config Demo Dialog, Dropdown Control, Integer Slider (+8 more)

### Community 16 - "SettingsGenerator"
Cohesion: 0.16
Nodes (11): AttributeInfo, SettingsGenerator, ActiveLayout, Dialog, Action, Func, List, MethodInfo (+3 more)

### Community 17 - "KeybindAttribute"
Cohesion: 0.25
Nodes (10): ControlButtonData, KeybindAttribute, SupportedTypes, Action, Func, List, Type, MyControl (+2 more)

### Community 18 - "RenderTraceBind"
Cohesion: 0.39
Nodes (3): RenderTraceBind, Exception, MethodInfo

### Community 19 - "SliderAttribute"
Cohesion: 0.18
Nodes (10): SliderAttribute, SupportedTypes, SliderType, Float, Integer, Action, Func, List (+2 more)

### Community 20 - "Attribute"
Cohesion: 0.18
Nodes (9): Attribute, CheckboxAttribute, SupportedTypes, Action, Func, List, Type, IgnoresAccessChecksToAttribute (+1 more)

### Community 21 - "Hashing"
Cohesion: 0.27
Nodes (7): Hashing, CodeInstruction, IEnumerable, Instruction, MethodImpl, MethodInfo, ConstructorInfo

### Community 22 - "CameraHistory"
Cohesion: 0.25
Nodes (8): CameraHistory, HasPrevious, InvViewProjection, PreviousViewProjection, ViewProjection, Matrix, Vector3, Vector3D

### Community 24 - "DropdownAttribute"
Cohesion: 0.28
Nodes (6): DropdownAttribute, SupportedTypes, Action, Func, List, Type

### Community 25 - "Tools"
Cohesion: 0.31
Nodes (4): Tools, Color, Match, Regex

### Community 26 - "Preloader"
Cohesion: 0.25
Nodes (4): AssemblyDefinition, IEnumerable, Preloader, TargetDLLs

### Community 27 - "ClientPlugin"
Cohesion: 0.25
Nodes (6): ClientPlugin, net10.0, net48, Krafs.Publicizer (2.3.0), Lib.Harmony (2.4.2), Mono.Cecil (0.11.6)

### Community 28 - ".CaptureScene"
Cohesion: 0.33
Nodes (3): Resource, Texture2D, HarmonyPostfix

### Community 29 - "ButtonAttribute"
Cohesion: 0.29
Nodes (6): ButtonAttribute, SupportedTypes, Action, Func, List, Type

### Community 30 - "ColorAttribute"
Cohesion: 0.29
Nodes (7): ColorAttribute, SupportedTypes, Action, Color, Func, List, Type

### Community 31 - "IElement"
Cohesion: 0.29
Nodes (6): IElement, SupportedTypes, Action, Func, List, Type

### Community 32 - "SeparatorAttribute"
Cohesion: 0.29
Nodes (6): SeparatorAttribute, SupportedTypes, Action, Func, List, Type

### Community 33 - "TextboxAttribute"
Cohesion: 0.29
Nodes (6): TextboxAttribute, SupportedTypes, Action, Func, List, Type

### Community 34 - "Binding"
Cohesion: 0.48
Nodes (3): Binding, IMyInput, MyKeys

### Community 36 - "Control"
Cohesion: 0.40
Nodes (4): Control, MyGuiControlBase, Vector2, MyGuiDrawAlignEnum

### Community 41 - "ClientPlugin.Settings.Elements"
Cohesion: 0.25
Nodes (3): ClientPlugin.Settings.Tools, ClientPlugin.Settings.Elements, ClientPlugin.Settings.Layouts

### Community 42 - "FrameGenStatus"
Cohesion: 0.39
Nodes (3): FrameGenStatus, CurrentText, StringBuilder

### Community 43 - ".RefreshFromDesktop"
Cohesion: 0.50
Nodes (3): DeviceMode, DeviceMode, DllImport

## Knowledge Gaps
- **134 isolated node(s):** `net10.0`, `net48`, `Lib.Harmony (2.4.2)`, `Mono.Cecil (0.11.6)`, `Krafs.Publicizer (2.3.0)` (+129 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 271 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **8 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `VRage.Utils` connect `ClientPlugin.FrameGen` to `Control`, `SettingsScreen`, `ClientPlugin.Settings.Elements`, `Buffer`, `RenderTraceBind`, `SliderAttribute`, `ButtonAttribute`?**
  _High betweenness centrality (0.146) - this node is a cross-community bridge._
- **Why does `ClientPlugin.FrameGen` connect `ClientPlugin.FrameGen` to `Buffer`, `FrameGenStatus`, `RenderTraceBind`, `PostPpHudPass`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **Why does `System.Runtime.CompilerServices` connect `ClientPlugin.FrameGen` to `TranspilerHelpers`, `Attribute`?**
  _High betweenness centrality (0.134) - this node is a cross-community bridge._
- **What connects `net10.0`, `net48`, `Lib.Harmony (2.4.2)` to the rest of the system?**
  _134 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.Write` be split into smaller, more focused modules?**
  _Cohesion score 0.05817028027498678 - nodes in this community are weakly interconnected._
- **Should `AnomalyHook` be split into smaller, more focused modules?**
  _Cohesion score 0.07272727272727272 - nodes in this community are weakly interconnected._
- **Should `ClientPlugin.FrameGen` be split into smaller, more focused modules?**
  _Cohesion score 0.07087486157253599 - nodes in this community are weakly interconnected._