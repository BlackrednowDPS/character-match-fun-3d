# PILLAR 6 — UNITY EDITOR SCENE AUTO-BUILDER & BATCHMODE COMPILATION
### Character Match Fun 3D — Local CLI Execution Specification
Owning agent: Unity Editor Automation & Scene Builder. Audited by Harsh Critic, QA & System Auditor (build-log verification, zero-error gate).

---

## 6.1 Purpose

Two Editor-only scripts let the project scene be assembled and compiled **without manual GUI intervention**, so the pipeline can run end-to-end from Blender export straight through to a local executable:

1. `Assets/Scripts/Editor/SceneAutoBuilder.cs` — populates the main gameplay scene: camera, URP lighting, grid anchor, boss spawn point, stage environment, and material assignment by naming convention against the assets produced in Pillar 5.
2. `Assets/Scripts/Editor/BuildScript.cs` — exposes static `BuildStandalone()` / `BuildMobile()` entry points callable from CLI batchmode.

## 6.2 Menu-Driven Local Workflow (for watching it assemble live in-Editor)

`SceneAutoBuilder` registers a `Tools/Character Match Fun 3D/Auto-Build Scene` menu item so, with the Editor open, the user can watch the scene populate step-by-step. The same method is also invoked automatically by `BuildScript` before a headless batchmode build, so CLI builds are never stale relative to what's visible in-Editor.

## 6.3 Scene Assembly Steps

1. Create/clear a scene named `Main_Game` at `Assets/Scenes/Main_Game.unity`.
2. Add `Main Camera` — orthographic-adjacent perspective (FOV 32°), tagged `MainCamera`, positioned for a top-down-ish board view (`(0, 9, -6)` looking at origin).
3. Add a `Directional Light`, configured for URP (intensity 1.1, soft shadows, warm-neutral color `#FFF4E0` matching the "kitchen" tone).
4. Instantiate a `GridAnchor` empty at the origin — the runtime `GridManager3D` (Pillar 7) parents all pooled pieces beneath it.
5. Instantiate a `BossSpawnPoint` empty offset above the grid (`(0, 3.5, 2)`).
6. Load the active stage prefab (by current season default, per Pillar 1/4) under a `StageRoot` empty.
7. Auto-assign URP materials to any imported mesh whose name matches a known convention (`Egg_*`, `Cosmetic_*`, `Fracture_*`) from `Assets/Materials/`, so Blender-exported meshes never land in the pink "missing material" state.
8. Mark the scene dirty and save.

## 6.4 Batchmode Build Command

```bash
Unity.exe -batchmode -quit -projectPath "." -executeMethod BuildScript.BuildStandalone -logFile build_log.txt
```

For a mobile (Android) build:

```bash
Unity.exe -batchmode -quit -projectPath "." -executeMethod BuildScript.BuildMobile -logFile build_log.txt
```

`BuildScript` calls `SceneAutoBuilder.BuildScene()` first, then runs `BuildPipeline.BuildPlayer` against `Assets/Scenes/Main_Game.unity`, writing output to `/Builds/`. Non-zero exit codes are surfaced whenever `BuildReport.summary.result != BuildResult.Succeeded`, so CI/CLI callers can detect failure without parsing the log.

## 6.5 Harsh Critic / QA Gate

Per the audit agent's mandate, `BuildScript` writes a machine-readable `Builds/build_report.json` (total errors, warnings, output size, build time) alongside the human-readable Unity log, so pass/fail status can be checked programmatically after a CLI run — this is what "validates zero-error CLI build status" means concretely.

See `Assets/Scripts/Editor/SceneAutoBuilder.cs` and `Assets/Scripts/Editor/BuildScript.cs` for the complete implementations.
