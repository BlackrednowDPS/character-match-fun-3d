# Codex Task Brief — Character Match Fun 3D (v4, WebGL vertical-slice)

You are a Senior Unity C# Developer. You are writing **runtime gameplay code only** for an existing, already-scaffolded Unity 6 project. Another agent (Claude) owns the asset pipeline, Editor tooling, build, and deployment. Stay strictly inside your lane, defined below.

**Project root:** `C:\Codex\CMF3D`
**Unity version:** `6000.5.6f1` (Unity 6) · **Render pipeline:** URP · **Delivery target:** **WebGL** → GitHub Pages
**Repo:** https://github.com/BlackrednowDPS/character-match-fun-3d

---

## 0. READ FIRST — binding specification

Before writing any code, read this file in the repo:

```
Docs/Pillar7_Codex_Runtime_Specs.md
```

It contains the **exact** namespaces, class names, field names, method signatures, enums, and events you must implement. It is a contract, not a suggestion. The Editor tooling and scene assembly already reference these names. If you rename anything, integration breaks and the build fails.

Supporting design detail (numeric tunables, behaviour rules) lives in:

```
Docs/Pillar1_Sensor_Weather_Gyro_Matrix.md
Docs/Pillar2_Boss_Battle_Combat.md
Docs/Pillar3_Culinary_Chemistry_MeshFracture.md
Docs/Pillar4_Psych_Narrative_MetaHumor.md
```

Pillar 7 deliberately does not duplicate every constant — pull them from the owning pillar.

---

## 1. FILE OWNERSHIP — do not cross these lines

### You own (create/edit freely)
```
Assets/Scripts/Runtime/**          ← all your work goes here
```

### DO NOT TOUCH — already written, tested, and committed
```
Blender/generate_assets.py                    ← VERIFIED WORKING on Blender 5.2. Do not regenerate.
Assets/Models/*.fbx                           ← 16 generated assets. Do not overwrite.
Assets/Models/cosmetic_manifest.json
Assets/Scripts/Editor/SceneAutoBuilder.cs     ← Claude owns
Assets/Scripts/Editor/BuildScript.cs          ← Claude owns
ProjectSettings/**, Packages/**               ← Claude owns
Docs/**, Origin Prompts/**, .gitignore
```

If you believe a file outside your lane needs to change, **do not edit it** — write a note in `Assets/Scripts/Runtime/INTEGRATION_NOTES.md` describing what you need and why.

### Do not run these
- Do **not** run `git commit` / `git push` (Claude manages version control).
- Do **not** attempt a Unity build or batchmode compile. **Unity is currently unlicensed on this machine**, so every build will fail for reasons unrelated to your code. Write the code, then stop. Claude runs the compile-and-fix loop once licensing is resolved.

---

## 2. HARD TECHNICAL CONSTRAINTS

### 2.1 WebGL is the delivery target — this changes real things
This is the most common source of broken code on this project. WebGL runs in a browser:

- **`Input.gyro` does not work in WebGL.** `SystemInfo.supportsGyroscope` returns false. `GyroGravityController` **must** detect this and fall back to a mouse/keyboard tilt driver (e.g. mouse position offset from screen centre drives the gravity vector; arrow keys as secondary). Gate hardware paths behind `#if !UNITY_WEBGL || UNITY_EDITOR` and provide a working non-gyro path — do **not** simply no-op it, or the tilt mechanic is dead on the only platform we ship.
- **`Input.location` (GPS) is unreliable/permission-gated in WebGL.** `WeatherSyncManager` must fall back to IP-based or timezone-based region resolution, and must never block gameplay waiting on it.
- **Blocking calls freeze the browser tab.** No `Thread.Sleep`, no synchronous web requests, no busy-wait loops. All network I/O via `UnityWebRequest` in coroutines.
- **`Application.persistentDataPath` maps to IndexedDB** and is async-flushed. It works, but do not assume an immediate write. Keep saves small.
- **No `System.Net.Http`**, no threads, no `Task.Run`. Coroutines only.

### 2.2 No third-party packages
Only what is already in `Packages/manifest.json`: URP, Input System, TextMeshPro, uGUI, and built-in modules.
- **Do NOT use LeanTween or DOTween.** They are not installed. The earlier v3 brief mentioned LeanTween — that instruction is void. Write a small coroutine/`AnimationCurve`-based tween helper in `Assets/Scripts/Runtime/Utils/` instead.
- Do not add packages to `manifest.json` yourself (that file is outside your lane). If you genuinely need one, note it in `INTEGRATION_NOTES.md`.

### 2.3 Architecture rules (from Pillar 7 §preamble)
- Namespace root: `CMF3D.Runtime`
- **Zero GC allocation** in `Update()`, `FixedUpdate()`, and per-frame hot paths: no LINQ, no `new` on managed types, no boxing, no string concatenation, no lambdas that capture.
- Pre-allocate all buffers once (arrays / pre-sized `List<T>` reused in place).
- **Object pooling** for every frequently spawned object: grid pieces, projectiles, fracture shards, VFX. No `Instantiate`/`Destroy` during play.
- Use `RaycastNonAlloc` / `NonAlloc` physics variants.
- Use `MaterialPropertyBlock` for per-renderer colour/alpha changes — never `renderer.material` (which leaks a material instance per object).
- Cross-system communication via the `WorldStateBus` event aggregator (Pillar 1 §1.1). No `FindObjectOfType` in `Update()`.
- SOLID: `GridManager3D` must not contain booster-specific or reaction-specific branching — those route through `IBoardEffect` / `ReactionExecutor`.

### 2.4 Scene contract
`SceneAutoBuilder.cs` creates these GameObjects by **exact name**. Resolve them once in `Awake`/`Initialize`, cache, never re-query per frame:
- `GridAnchor` — parent for all pooled grid pieces
- `BossSpawnPoint` — boss placement
- `StageRoot` — environment root

Asset naming convention from the Blender pipeline (already on disk):
- Eggs: `Egg_Hardboiled`, `Egg_Fried`, `Egg_Scrambled`
- Sockets (empties, children of each egg): `Socket_Eyewear`, `Socket_Head`
- Cosmetics: `Cosmetic_Eyewear_Sunglasses`, `Cosmetic_Eyewear_Monocle`, `Cosmetic_Head_TopHat`, `Cosmetic_Head_Beanie`
- Fracture variants: `Fracture_<Archetype>_<Light|Medium|Shatter>`

---

## 3. BUILD ORDER — implement in this sequence

Priority matters. If you run short on time or context, a **working grid is worth more than six half-finished systems**. Complete each tier before starting the next.

### TIER 1 — Playable core (must work standalone)
| File | Purpose |
|---|---|
| `Runtime/Core/WorldStateBus.cs` | Static struct-event aggregator (Pillar 1 §1.1). Events: `OnGravityVectorChanged`, `OnSeasonChanged`, `OnWeatherChanged`, `OnTemperatureThresholdCrossed`, `OnPlayerStressLevelChanged` |
| `Runtime/Core/ObjectPool.cs` | Generic `IObjectPool<T>` + `IPoolable`. Pre-warm on init |
| `Runtime/Utils/TweenHelper.cs` | Coroutine/`AnimationCurve` tweens (LeanTween replacement) |
| `Runtime/Gameplay/EggPiece.cs` | Pillar 7 §7.7. `IPoolable`, elemental tags, archetype, board pos |
| `Runtime/Gameplay/GridManager3D.cs` | Pillar 7 §7.7. Fixed 7×8 board, raycast input, swap, match detect, cascade, gravity fall, pooling |

**Tier 1 exit criteria:** a player can click/drag to swap adjacent pieces, valid matches of 3+ clear, pieces fall to fill gaps, cascades resolve, score increments. This alone is a shippable slice.

### TIER 2 — Feel and identity
| File | Purpose |
|---|---|
| `Runtime/Systems/MeshFractureEngine.cs` | Pillar 7 §7.6 + Pillar 3 §3.5. Force→variant mapping, pooled shards, `MaterialPropertyBlock` fade, max 6 concurrent |
| `Runtime/Systems/CulinaryReactionSystem.cs` | Pillar 7 §7.5 + Pillar 3 §3.2. `ElementalTag` flags, priority-sorted recipes, `ReactionExecutor`, discovery log |
| `Runtime/Sensors/GyroGravityController.cs` | Pillar 7 §7.1 + Pillar 1 §1.2. **Include the WebGL mouse/keyboard fallback (§2.1)** |

### TIER 3 — Depth
| File | Purpose |
|---|---|
| `Runtime/Combat/BossBattleManager.cs` | Pillar 7 §7.4 + Pillar 2. Combo→attack pipeline, phases, counter-attacks. Put pure math in a static `BossCombatMath` class |
| `Runtime/Sensors/StressDetector.cs` | Pillar 7 §7.3 + Pillar 1 §1.5. Throttled tier evaluation, meta-humor trigger + paired hint offer |
| `Runtime/Systems/WeatherSyncManager.cs` | Pillar 7 §7.2 + Pillar 1 §1.4. Open-Meteo (no API key), 15-min cache, **fail-soft to `Clear`** |

### TIER 4 — Only if Tiers 1–3 are genuinely complete
ScriptableObject definitions (`BossProfile`, `ReactionRecipeSO`, `CounterAttackSO`, `CosmeticDefinitionSO`), dialogue database, seasonal manager, cosmetic socket assembler.

---

## 4. QUALITY BAR

- **No placeholders.** No `// TODO`, no `throw new NotImplementedException()`, no truncated method bodies, no "rest of implementation omitted".
- **It must plausibly compile.** You cannot build (Unity is unlicensed), so be conservative: verify every Unity API you call exists in Unity 6 with the correct signature. Prefer well-established APIs over exotic ones. Do not guess at API surface.
- **Unity 6 API notes:** `FindObjectsOfType` is deprecated → use `FindObjectsByType(FindObjectsSortMode.None)`. `Object.FindObjectOfType` → `Object.FindFirstObjectByType`. Only in init paths, never per-frame.
- **Every public member gets an XML doc comment** stating what it does and any allocation/threading caveat.
- **Serialize tunables** with `[SerializeField]` and set defaults to the exact values in the pillar docs, so a designer can retune without recompiling.
- **Null-guard external references** resolved by name (`GridAnchor` etc.) and log a clear, actionable error if missing rather than throwing a bare `NullReferenceException`.

---

## 5. WHEN YOU HIT SOMETHING UNDERSPECIFIED

Do not stall and do not invent a conflicting API. In order:
1. Check the relevant pillar doc.
2. If still ambiguous, pick the **simplest implementation consistent with the Pillar 7 signature**, implement it fully, and record the decision in `Assets/Scripts/Runtime/INTEGRATION_NOTES.md` under `## Assumptions`.
3. Never change a signature from Pillar 7 to make your life easier — that breaks integration silently.

---

## 6. DEFINITION OF DONE

Report back with:
1. **File manifest** — every file created, with line counts.
2. **Tier completion** — which tiers are fully done vs. partial.
3. **`INTEGRATION_NOTES.md`** containing:
   - `## Assumptions` — every underspecified decision you made
   - `## Needs From Claude` — anything required outside your lane (packages, prefabs, ProjectSettings, materials, scene objects)
   - `## Known Risks` — any API you were less than confident about, so Claude checks it first during the compile pass
4. **Self-review pass** — before declaring done, re-read your own code specifically hunting for: LINQ or allocations in `Update()`, `Instantiate`/`Destroy` during play, `renderer.material` usage, WebGL-incompatible APIs, and any Pillar 7 signature you drifted from. Fix what you find and say what you fixed.

Do not report success on anything you did not actually write.
