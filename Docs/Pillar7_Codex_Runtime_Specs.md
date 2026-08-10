# PILLAR 7 — C# RUNTIME CODE SPECIFICATIONS FOR CODEX GENERATION
### Character Match Fun 3D — Structured Technical Contracts
Owning agent: Unity Technical Architect & C# Code Specifier. This pillar is the handoff artifact: `Origin Prompts/codex_implementation_prompt_v3.md` instructs Codex to generate full, complete (no `TODO`, no placeholders) implementations of the seven runtime scripts specified below. Every contract here is binding — Codex should not deviate from method signatures, namespaces, or event names without updating this document first, since Pillars 1–4 reference these exact names.

**Global architectural rules (apply to every script in this pillar):**
- Namespace root: `CMF3D.Runtime`
- SOLID principles; no static mutable game-state outside the `WorldStateBus` (Pillar 1.1) and object pools.
- **Zero GC allocation in any `Update()`, `FixedUpdate()`, or per-frame hot path.** No LINQ, no `new` on managed types, no boxing, no string concatenation in hot paths.
- All frequently-instantiated objects (projectiles, grid pieces, fracture shards, VFX) go through an `IObjectPool<T>` — no runtime `Instantiate`/`Destroy` calls once a level is running.
- All cross-system communication goes through `WorldStateBus` events (struct-based, no closures) — no direct `GetComponent`/`FindObjectOfType` calls in `Update()`.
- Every `MonoBehaviour` intended for runtime (player build) inclusion lives under `Assets/Scripts/Runtime/`, never `Assets/Scripts/Editor/` (see Pillar 6.3 note).

---

## 7.1 `GyroGravityController.cs`

**Location:** `Assets/Scripts/Runtime/GyroGravityController.cs`
**Consumes:** device gyro/accelerometer input
**Publishes:** `WorldStateBus.OnGravityVectorChanged(Vector3)`
**Consumed by:** `GridManager3D` (per-piece Rigidbody force), `EggPiece`

```csharp
namespace CMF3D.Runtime
{
    public sealed class GyroGravityController : MonoBehaviour
    {
        [SerializeField] private float gravityScale = 0.6f;
        [SerializeField] private float deadZoneDegrees = 3.0f;
        [SerializeField] private float maxTiltDegrees = 35.0f;
        [SerializeField] private float shakeJerkThreshold = 6.5f;   // g/s
        [SerializeField] private float shakeSampleWindow = 0.4f;    // seconds
        [SerializeField] private float shakeCooldown = 8.0f;        // seconds
        [SerializeField] private float scrambleForceRadius = 2.2f;

        public bool GyroAvailable { get; private set; }
        public int ScrambleChargesRemaining { get; private set; }

        private void Awake();       // enables Input.gyro, checks SystemInfo.supportsGyroscope
        private void Update();      // samples gyro, computes clamped/dead-zoned gravity vector,
                                     // raises OnGravityVectorChanged only when delta exceeds an
                                     // epsilon (avoids event spam every frame)
        private void SampleJerk();  // rolling buffer (fixed-size array, no allocation) of
                                     // Input.acceleration magnitude deltas over shakeSampleWindow
        private bool DetectShake(); // returns true once per cooldown window when jerk threshold crossed
        public bool TryConsumeScrambleCharge(out Vector3 explosionOrigin);
        public void GrantScrambleCharge(int amount);
    }
}
```

**Acceptance criteria:** dead-zone and max-tilt clamp values are exposed as serialized fields (designer-tunable without recompiling); jerk sampling uses a fixed-capacity ring buffer allocated once in `Awake()`; `DetectShake()` must not fire while `!Application.isFocused`.

---

## 7.2 `WeatherSyncManager.cs`

**Location:** `Assets/Scripts/Runtime/WeatherSyncManager.cs`
**Consumes:** `Input.location`, Open-Meteo REST API (no API key)
**Publishes:** `WorldStateBus.OnWeatherChanged(WeatherState)`, `WorldStateBus.OnTemperatureThresholdCrossed(float, ThresholdDirection)`

```csharp
namespace CMF3D.Runtime
{
    public enum WeatherState { Clear, Rain, Snow }
    public enum ThresholdDirection { Rising, Falling }

    public sealed class WeatherSyncManager : MonoBehaviour
    {
        [SerializeField] private float pollIntervalMinutes = 15f;
        [SerializeField] private float hotThresholdCelsius = 30f;
        [SerializeField] private float coldThresholdCelsius = 0f;

        public WeatherState CurrentWeather { get; private set; } = WeatherState.Clear;
        public float CurrentTemperatureCelsius { get; private set; }

        private IEnumerator LocationRoutine();   // requests location permission, resolves lat/long,
                                                  // falls back silently to cached/default region
        private IEnumerator PollRoutine();       // UnityWebRequest to Open-Meteo, respects
                                                  // pollIntervalMinutes, disk-caches last successful
                                                  // response for offline play (fail-soft to Clear)
        private void ApplyWeather(WeatherState state, float tempCelsius);
        // internal: diffs previous vs. new temperature against threshold constants,
        // raises OnTemperatureThresholdCrossed exactly once per crossing (not every poll)
    }
}
```

**Acceptance criteria:** never blocks the main thread; a failed/timed-out request must not throw — it logs and retains the last known-good `WeatherState`. Disk cache path: `Application.persistentDataPath/weather_cache.json`.

---

## 7.3 `StressDetector.cs`

**Location:** `Assets/Scripts/Runtime/StressDetector.cs`
**Consumes:** touch input (`Input.touches` / new Input System pointer events)
**Publishes:** `WorldStateBus.OnPlayerStressLevelChanged(StressTier)`
**Consumed by:** dialogue trigger system (Pillar 4.4), Player Behavior Engine (Pillar 4.11)

```csharp
namespace CMF3D.Runtime
{
    public enum StressTier { Calm, Focused, Frantic }

    public sealed class StressDetector : MonoBehaviour
    {
        [SerializeField] private float frenticTapsPerSecond = 3.5f;
        [SerializeField] private float focusedTapsPerSecond = 1.5f;
        [SerializeField] private float rollingWindowSeconds = 5f;
        [SerializeField, Range(0f, 1f)] private float metaHumorTriggerChance = 0.15f;
        [SerializeField] private float metaHumorCooldownSeconds = 45f;

        public StressTier CurrentTier { get; private set; } = StressTier.Calm;

        private void RegisterTap(Vector2 screenPos, bool wasValidMatchTarget);
        // called by input layer per tap; appends to a fixed-capacity ring buffer,
        // increments a mis-tap counter when wasValidMatchTarget == false

        private void EvaluateTier();       // runs on a throttled timer (5-10Hz), not every Update
        private void TryFireMetaHumorLine();
        // rolls metaHumorTriggerChance only on Frantic-tier entry, respects cooldown,
        // pulls a line from DialogueDatabase pool tagged "MetaHumor_Stress",
        // and simultaneously requests a hint/booster offer from the assist system
        // (never fires the joke without the accompanying breather offer)
    }
}
```

**Acceptance criteria:** tier evaluation must be decoupled from per-touch events (throttled) to avoid recomputation storms during frantic tapping; `metaHumorCooldownSeconds` enforced via a single `float` timestamp comparison, no coroutines per line.

---

## 7.4 `BossBattleManager.cs`

**Location:** `Assets/Scripts/Runtime/BossBattleManager.cs`
**Consumes:** match events from `GridManager3D`, `BossProfile`/`BossPhase`/`CounterAttackSO` data assets
**Publishes:** `OnBossPhaseChanged(int phaseIndex)`, `OnBossDefeated()`, `OnBossCounterAttackTelegraphed(CounterAttackSO)`

```csharp
namespace CMF3D.Runtime
{
    public sealed class BossBattleManager : MonoBehaviour
    {
        [SerializeField] private BossProfile activeBoss;
        [SerializeField] private ProjectilePool projectilePool;

        public int CurrentHP { get; private set; }
        public int CurrentPhaseIndex { get; private set; }

        public event Action<int> OnBossPhaseChanged;
        public event Action OnBossDefeated;
        public event Action<CounterAttackSO> OnBossCounterAttackTelegraphed;

        public void InitializeEncounter(BossProfile profile);
        public void ReceiveMatchAttack(int matchSize, MatchShape shape, int cascadeLinkIndex);
        // maps matchSize/shape -> attack type per Pillar 2.2 table, computes chain
        // multiplier (0.25x per cascade link, capped 4.0x), spawns pooled projectiles
        // via projectilePool.Get(), applies damage on projectile-hitzone collision callback

        private void ApplyDamage(int amount, HitZone zone);
        private void EvaluatePhaseTransition();   // checks HP thresholds from activeBoss.Phases
        private void TickEnrageTimer(float deltaTime);
        private void ScheduleCounterAttacks();    // reads activeBoss.CounterAttacks cooldowns,
                                                    // telegraphs 1.2s before executing (per Pillar 2.3)
        private void ExecuteCounterAttack(CounterAttackSO counter);
        // dispatches to GridManager3D.ApplyWhiskScramble / ApplyHotOilLock / ApplyHazardTiles
    }
}
```

**Acceptance criteria:** damage/HP math is pure and unit-testable (no `MonoBehaviour` state dependency inside the calculation methods themselves — wrap in a plain `BossCombatMath` static class); losing a boss attempt must NOT clear earned rewards accumulated during the attempt (Pillar 2.6 no-hard-fail rule).

---

## 7.5 `CulinaryReactionSystem.cs`

**Location:** `Assets/Scripts/Runtime/CulinaryReactionSystem.cs`
**Consumes:** `ElementalTag` data on matched `EggPiece` instances, `ReactionRecipeSO[]` database
**Publishes:** `OnReactionTriggered(ReactionRecipeSO, Vector3 originWorldPos)`
**Writes to:** `RecipeBookEntry` save data (Pillar 3.3 schema)

```csharp
namespace CMF3D.Runtime
{
    [Flags]
    public enum ElementalTag { None = 0, Spicy = 1, Frozen = 2, HardBoiled = 4, Beaten = 8, Fried = 16, Pickled = 32 }

    public sealed class CulinaryReactionSystem : MonoBehaviour
    {
        [SerializeField] private ReactionRecipeSO[] recipeDatabase; // sorted by Priority desc at Awake
        [SerializeField] private RecipeBookSaveData recipeBook;

        public event Action<ReactionRecipeSO, Vector3> OnReactionTriggered;

        public void EvaluateMatch(IReadOnlyList<EggPiece> matchedPieces, Vector3 originWorldPos);
        // aggregates ElementalTag flags across matchedPieces (bitwise OR, no allocation),
        // queries recipeDatabase for first Priority-ordered recipe whose ComponentA|ComponentB
        // mask is satisfied, invokes ReactionExecutor.Execute, logs first-discovery to recipeBook

        private void RecordDiscovery(string recipeId);  // idempotent — no-op if already discovered
    }

    public static class ReactionExecutor
    {
        public static void Execute(ReactionRecipeSO recipe, MatchContext context, GridManager3D grid);
        // switches on recipe.ReactionType (ThermalShock / MeringueShield / UmamiOverload /
        // ScovilleCascade / FlashFreeze) per the Pillar 3.2 effect table
    }
}
```

**Acceptance criteria:** recipe priority resolution is O(n) over a pre-sorted array (sorted once, not per-match); `RecordDiscovery` must be safe to call every match without duplicate-entry bugs or save-file bloat.

---

## 7.6 `MeshFractureEngine.cs`

**Location:** `Assets/Scripts/Runtime/MeshFractureEngine.cs`
**Consumes:** impact force from `BossBattleManager` projectile hits and `GridManager3D` match-clears; pre-fractured variant prefabs from Pillar 5 (`Fracture_<Archetype>_<Severity>`)
**Pools:** shard `Rigidbody` prefabs via `IObjectPool<FractureShard>`

```csharp
namespace CMF3D.Runtime
{
    public enum FractureSeverity { Light, Medium, Shatter }

    public sealed class MeshFractureEngine : MonoBehaviour
    {
        [SerializeField] private int maxConcurrentFractureEvents = 6;
        [SerializeField] private float shardLifetimeSeconds = 1.4f;
        [SerializeField] private float shardFadeStartSeconds = 1.1f; // 0.3s fade-out window

        public void TriggerFracture(string archetypeName, Vector3 worldPos, float normalizedImpactForce);
        // 0.0-0.35 -> Light, 0.35-0.7 -> Medium, 0.7-1.0 -> Shatter (Pillar 3.5.2 table);
        // if activeFractureEvents.Count >= maxConcurrentFractureEvents, culls the oldest
        // active event immediately instead of queuing (mobile perf budget, Pillar 3.5.3)

        private FractureShard[] SpawnShardsFromPool(string archetypeName, FractureSeverity severity, Vector3 worldPos);
        private void ApplyExplosionImpulse(FractureShard[] shards, Vector3 origin, float force);
        private IEnumerator DespawnAfterLifetime(FractureShard[] shards);
        // fades MeshRenderer material alpha (MaterialPropertyBlock, no per-instance material
        // instantiation) starting at shardFadeStartSeconds, returns shards to pool at shardLifetimeSeconds
    }
}
```

**Acceptance criteria:** shard fade uses `MaterialPropertyBlock` exclusively — never `renderer.material` (which would instantiate a material instance per shard and defeat the batching/GC goals); pool must pre-warm on scene load, not on first fracture event (avoids a frame hitch on first boss hit).

---

## 7.7 `GridManager3D.cs` & `EggPiece.cs`

**Location:** `Assets/Scripts/Runtime/GridManager3D.cs`, `Assets/Scripts/Runtime/EggPiece.cs`
**Central hub** — receives gravity vector (7.1), weather friction modifiers (7.2), raycast touch input, and dispatches match results to `CulinaryReactionSystem` (7.5), `BossBattleManager` (7.4), and `MeshFractureEngine` (7.6).

```csharp
namespace CMF3D.Runtime
{
    public sealed class GridManager3D : MonoBehaviour
    {
        [SerializeField] private int columns = 7;
        [SerializeField] private int rows = 8;
        [SerializeField] private IObjectPool<EggPiece> piecePool;
        [SerializeField] private Transform gridAnchor; // resolved via GameObject.Find("GridAnchor")
                                                         // at scene load, per Pillar 6.3 anchor contract

        private EggPiece[,] board;          // fixed-size 2D array, allocated once
        private readonly List<EggPiece> matchScratchBuffer = new(16); // pre-allocated, reused

        public void Initialize(int seed);
        private void HandleRaycastInput();          // touch -> world raycast -> board coords,
                                                       // zero-alloc (RaycastNonAlloc)
        private bool TrySwap(Vector2Int a, Vector2Int b);
        private void DetectMatches();               // fills matchScratchBuffer in-place, no LINQ
        private void ResolveMatches();               // dispatches to CulinaryReactionSystem,
                                                       // MeshFractureEngine, BossBattleManager,
                                                       // then returns cleared EggPiece instances to pool
        private void ApplyGravityFall();              // per-column settle using current
                                                       // WorldStateBus gravity vector (Pillar 1)
        public void ScrambleBoard(Vector3 explosionOrigin, float radius);   // Pillar 1.2.2
        public void ApplyWhiskScramble(float percentage);                  // Pillar 2.3
        public void ApplyHotOilLock(int tileCount, int turnDuration);      // Pillar 2.3
        public void ApplyHazardTiles(int tileCount, float durationSeconds);// shared with weather hazard (Pillar 1.4)
    }

    public sealed class EggPiece : MonoBehaviour, IPoolable
    {
        public ElementalTag Tags { get; private set; }
        public string Archetype { get; private set; }  // "Hardboiled" | "Fried" | "Scrambled"
        public Vector2Int BoardPosition { get; set; }
        public Rigidbody Body { get; private set; }

        public void OnSpawnFromPool(string archetype, ElementalTag tags, Vector2Int boardPos);
        public void OnReturnToPool();  // resets velocity/tags/position, does NOT destroy the GameObject
    }
}
```

**Acceptance criteria:** `board` array and `matchScratchBuffer` are allocated exactly once (in `Initialize`), never resized per-match; `DetectMatches`/`ResolveMatches` must contain zero LINQ and zero `new List<>()` calls; `GridAnchor` resolution happens once at `Awake`/`Initialize`, cached, never re-queried in `Update`.

---

## 7.8 Cross-Pillar Dependency Graph

```
GyroGravityController ──┐
WeatherSyncManager ─────┼──> WorldStateBus ──> GridManager3D ──> EggPiece (pooled)
StressDetector ─────────┘                           │
                                                      ├──> CulinaryReactionSystem ──> MeshFractureEngine
                                                      └──> BossBattleManager ──> MeshFractureEngine
```

## 7.9 Handoff Checklist for Codex (from `codex_implementation_prompt_v3.md`)

1. Implement all seven scripts above with zero `TODO`/placeholder blocks, matching every signature exactly.
2. Enforce SOLID + object pooling + zero-GC-in-Update as specified per script.
3. Write files directly to the paths listed in each section header.
4. Cross-reference Pillars 1–4 for exact numeric parameters (thresholds, cooldowns, damage multipliers) — this document intentionally does not duplicate every tunable value; it points back to the owning pillar as the source of truth.
5. Run a compile pass against `Assets/Scripts/Editor/BuildScript.cs` (`BuildStandalone`) to confirm zero-error status before declaring the runtime layer complete — this closes the loop back into Pillar 6's CLI build gate.
