# PILLAR 2 — BOSS BATTLE SYSTEM & COMBAT MECHANICS
### Character Match Fun 3D — Local C# Architecture Specification
Owning agent: Dept. 3 Boss Battle & Combat Architect. Supported by 3D Physics & Board Specialist (impact), Character & Grid Animator (hit reactions), Sarcastic Dialogue Scriptwriter (boss banter).

---

## 2.1 Cadence & Structure

- A Boss Encounter replaces the standard level every **10th level** (`levelIndex % 10 == 0`).
- Bosses are drawn from a rotating `BossRosterSO` keyed to the current multiverse dimension/season, so boss identity ties into Pillar 1's seasonal system (e.g., a Winter rotation surfaces "The Whisk Reaper").
- Each boss fight is a hybrid: the match-3 grid remains active underneath, but matches now feed a **combo-to-attack pipeline** instead of only clearing score.

## 2.2 Combo → Attack Pipeline

| Match Size | Player Action Generated | Damage Multiplier |
|---|---|---|
| 3-in-a-row | Single Projectile (yolk shot) | 1.0x |
| 4-in-a-row | Machine-Gun Burst (5 rapid projectiles) | 1.6x |
| 5-in-a-row / L or T shape | Grenade Lob (AoE splash, cracks boss shell segment) | 2.4x |
| Combo chain (cascade ≥2 in one input) | Chain multiplier stacks +0.25x per cascade link, capped at 4.0x total | — |

Each generated attack is queued into an object-pooled `ProjectilePool` (zero runtime allocation — Pillar 5/7 architecture rule) and resolved against the boss's current exposed `HitZone`.

### 2.2.1 Boss HP & Phase Model
```
BossProfile
 ├─ MaxHP: int
 ├─ Phases: List<BossPhase>   // e.g., 3 phases at 100%, 60%, 25% HP thresholds
 │    ├─ HitZones: List<HitZone> (some zones shielded until a prior zone breaks)
 │    ├─ AttackPattern: BossAttackPatternSO
 │    └─ EnrageTimer: float   // forces phase-skip if player stalls too long
 └─ CounterAttacks: List<CounterAttackSO>
```

- Phase transitions trigger a scripted 1.5s "shell-crack" cinematic beat (ties to Pillar 4 Cinematic Director) and briefly disable board input to sell the hit.
- `EnrageTimer` exists purely as a pacing safety valve (Harsh Critic requirement) — if a phase runs past 90s, boss telegraphs stronger attacks to nudge the fight forward, never an instant-fail.

## 2.3 Boss Counter-Attacks

| Counter-Attack | Trigger Condition | Board Effect | Player Counterplay |
|---|---|---|---|
| **Whisk Scramble** | Boss loses 20% HP in one player turn | Randomly shuffles 40% of board tiles (does not destroy pieces) | Rescramble is telegraphed 1.2s in advance via a spinning-whisk VFX tell |
| **Hot Oil Lock** | Every 15s on a timer | Locks 1–3 random tiles under a translucent "hot oil" overlay for 2 turns; matching an adjacent 4+ combo pops the lock early | Encourages combo play near locked tiles |
| **Frying Pan Burn** (Phase 3 only) | HP < 25% | Marks 3 tiles as hazard tiles for 8s (same hazard type as Pillar 1 weather "Unplanned Frying") | Reuses the existing hazard-tile system — no new VFX budget needed |

All counter-attacks are authored as `ScriptableObject` data (`CounterAttackSO`) so QA/Harsh Critic can rebalance timings without touching code.

## 2.4 Example Boss Roster (initial 3, extensible via `BossRosterSO`)

| Boss | Dimension/Theme | Signature Mechanic |
|---|---|---|
| **The Whisk Reaper** | Winter Kitchen Multiverse | Whisk Scramble on cooldown reduced to 12s in Phase 2 |
| **Duchess Meringue** | Bakery Ballroom Dimension | Immune to Grenade Lobs while shielded; shield only breaks via 2 consecutive Machine-Gun Bursts |
| **The Sizzling Sovereign** | Volcanic Skillet Realm | Passive "ambient heat" halves cooldown on Frying Pan Burn from the start of the fight |

## 2.5 Physics & Impact (cross-reference Pillar 3)

- Projectile impact force is computed from combo size and fed into the `MeshFractureEngine` (Pillar 3) so boss shell cracking is visually proportional to the attack that caused it — a 5-match Grenade Lob produces a visibly larger fracture pattern than a 3-match shot.
- All projectile Rigidbodies use `ForceMode.Impulse` with capped max velocity (22 units/s) to keep mobile physics deterministic across frame-rate variance (fixed timestep 0.02s).

## 2.6 Failure & Retry Design

- No hard game-over on boss loss — losing a boss attempt returns the player to the pre-boss level select with earned partial rewards (dopamine-loop guardrail from Pillar 4's Psychological Designer: never erase progress, only delay it).
- Retry immediately available; no energy/lives cost on boss retries specifically (differentiates boss levels from standard economy-gated levels in Pillar 4).

Full `BossBattleManager.cs` class contract (HP state machine, phase transition events, counter-attack scheduler) is finalized in **Pillar 7**.
