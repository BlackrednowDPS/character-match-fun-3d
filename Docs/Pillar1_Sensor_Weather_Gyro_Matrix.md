# PILLAR 1 — REAL-TIME SENSOR, WEATHER & GYRO MATRIX
### Character Match Fun 3D — Local C# Architecture Specification
Owning agents: Dept. 2 (World, Sensors & Live-Ops) — Seasons/Geolocation Manager, Gyro & Hardware Sensor Specialist, Weather Sync Agent, Meta-Humor & Stress Reaction Scriptwriter. Audited by Dept. 5 Harsh Critic.

---

## 1.1 System Overview

The board is not a static grid — it is a physical object embedded in a world that reacts to three live inputs:

| Input Source | Sampling Rate | Consumer System |
|---|---|---|
| Device Gyroscope / Accelerometer | 60 Hz (`Input.gyro`) | `GyroGravityController` |
| System Clock + IP/GPS Geolocation | Once per session + on resume | `SeasonalEventManager` |
| Local Weather REST API | Every 15 min (cached) | `WeatherSyncManager` |
| Touch velocity / tap cadence | Per-touch, rolling 5s window | `StressDetector` |

All four systems publish to a central `WorldStateBus` (a lightweight static event aggregator, zero-GC, `struct`-based events) that the grid, camera, VFX, and dialogue systems subscribe to. No system talks to another directly — this keeps the 26-department outputs decoupled and lets Codex generate each script independently in Pillar 7.

```
WorldStateBus (static, struct events, no boxing)
 ├─ OnGravityVectorChanged(Vector3 gravity)
 ├─ OnSeasonChanged(SeasonType season, HolidayEvent activeEvent)
 ├─ OnWeatherChanged(WeatherState weather)
 ├─ OnTemperatureThresholdCrossed(float celsius, ThresholdDirection dir)
 └─ OnPlayerStressLevelChanged(StressTier tier)
```

## 1.2 Gyro & Hardware Sensor Matrix

**Agent:** Gyro & Hardware Sensor Specialist

### 1.2.1 Gravity Tilt
- Reads `Input.gyro.gravity` (iOS/Android via `Input.gyro.enabled = true`).
- Remaps device-space gravity into board-local space using the current camera's up-vector, then feeds it to a `PhysicsMaterial`-driven `Rigidbody.AddForce(gravity * gravityScale, ForceMode.Acceleration)` per egg piece.
- Dead-zone: ±3° from vertical is ignored (prevents idle jitter from nudging pieces).
- Clamp: max tilt influence capped at 35° equivalent force so the grid never becomes unplayable on a moving bus/train (accessibility requirement flagged by Harsh Critic).

### 1.2.2 Shake to Scramble
- Monitors `Input.acceleration` magnitude of jerk (derivative of acceleration) over a 0.4s window.
- Trigger threshold: peak jerk > `6.5g/s` sustained across ≥3 samples (debounced to avoid false positives from pocket bumps while device is locked — gated on `Application.isFocused`).
- On trigger: `GridManager3D.ScrambleBoard()` — physically impulses every piece with a randomized `AddExplosionForce` from a virtual point above the board, then re-settles into a new valid (non-pre-matched) configuration. Costs one "Scramble Charge" (regenerates 1 per 3 levels, purchasable via soft currency — ties into Pillar 4 economy).
- Cooldown: 8 seconds hardware-side regardless of charges, to prevent physics spam.

### 1.2.3 Parameter Table

| Parameter | Value | Notes |
|---|---|---|
| `gravityScale` | 0.6 | Tuned so tilt is felt but never overrides match-3 grid snapping |
| `deadZoneDegrees` | 3.0 | |
| `maxTiltDegrees` | 35.0 | Accessibility clamp |
| `shakeJerkThreshold` | 6.5 g/s | |
| `shakeSampleWindow` | 0.4 s | |
| `shakeCooldown` | 8.0 s | |
| `scrambleForceRadius` | 2.2 units | |

## 1.3 Seasonal & Geolocation Event Manager

**Agent:** Seasons & Real-Time Geolocation Event Manager

- On session start, resolves player region via `Input.location` (with graceful fallback to system timezone + `CultureInfo` if location permission denied — never blocks play).
- Maps region + `DateTime.UtcNow` → `SeasonType` (Spring/Summer/Fall/Winter, hemisphere-aware) and `HolidayEvent` (e.g., `Halloween_JackOLanternEgg`, `Winter_SantaYolk`) via a data-driven `SeasonalCalendarSO` ScriptableObject — no hardcoded dates, so live-ops can push new events without a build.
- Environment swap is additive: skybox, fog color, stage prop set, and ambient audio bed are all season-tagged prefabs pooled and cross-faded (0.8s) — never a hard cut.
- Holiday NPCs (Pillar 4) are spawned/despawned by this system based on `HolidayEvent.activeWindowUtc`.

### 1.3.1 Season Resolution Algorithm (pseudocode)
```
season = ResolveSeason(hemisphere, DateTime.UtcNow.Month)
holiday = calendarSO.Events.FirstOrDefault(e => e.IsActive(DateTime.UtcNow, regionCode))
WorldStateBus.RaiseSeasonChanged(season, holiday)
```

## 1.4 Hyper-Local Weather Sync

**Agent:** Hyper-Local Weather Sync Agent

- `WeatherSyncManager` calls a free-tier REST weather API (Open-Meteo, no key required — keeps the $0-budget constraint) using resolved lat/long, cached to disk for 15 minutes to respect rate limits and offline play.
- Weather → gameplay hooks:

| Weather | Effect |
|---|---|
| Rain | Board friction coefficient −25% (pieces slide further after matches; combo chains feel "wetter") |
| Clear | Baseline friction |
| Snow | Gravity scale −15%, sparkle VFX overlay |
| Temp > 30°C | `OnTemperatureThresholdCrossed` fires "Unplanned Frying" hazard: a random board tile becomes a hot-pan hazard tile for 10s, auto-cooking any egg piece that lands on it (removes it from play, awards bonus points — risk/reward, not punishment) |
| Temp < 0°C | Pieces gain a "Frozen" tag chance (+10%) on spawn, feeding Pillar 3 culinary chemistry |

- Fails soft: if API unreachable, `WeatherState.Clear` is assumed and no gameplay penalty occurs.

## 1.5 Meta-Humor & Stress Reaction Scriptwriter Hooks

**Agent:** Meta-Humor & Stress Reaction Scriptwriter (logic authored by Psych/Emotional Designer, lines written by Dialogue Scriptwriter)

- `StressDetector` computes a rolling tap-velocity metric: taps/second and average touch-to-touch distance over a 5s window.
- Stress tiers: `Calm` (<1.5 taps/s), `Focused` (1.5–3.5), `Frantic` (>3.5 or ≥2 mis-taps on non-matchable tiles within 2s).
- On entering `Frantic`, a 15% roll (cooldown 45s) triggers a 4th-wall-breaking one-liner from the active egg character (e.g., *"I can see your finger shaking through the screen. Breathe, detective."*), pulled from a pool tagged `MetaHumor_Stress` in the dialogue database (Pillar 4).
- This is explicitly framed as empathetic comic relief, not mockery — Psychological Designer's guardrail: never trigger stress lines more than once per 45s, and always offer an in-fiction "breather" (auto-suggest a hint/booster) alongside the joke.

## 1.6 Cross-System Data Contract

All four managers write into a single `WorldStateSnapshot` struct sampled once per frame by consumers, avoiding per-system polling overhead:

```csharp
public readonly struct WorldStateSnapshot
{
    public readonly Vector3 GravityVector;
    public readonly SeasonType Season;
    public readonly WeatherState Weather;
    public readonly float TemperatureCelsius;
    public readonly StressTier PlayerStress;
}
```

Full class contracts for `GyroGravityController`, `WeatherSyncManager`, and `StressDetector` are finalized in **Pillar 7** for direct Codex generation.
