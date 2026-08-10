# PILLAR 4 — PSYCHOLOGICAL LOOP, NARRATIVE & META-HUMOR SCRIPTING
### Character Match Fun 3D — Design & Systems Specification
Owning agents: Dept. 1 (Narrative, Characters & Psychology) in full, plus Monetization & Economy Architect, Player Behavior Engine, and SecOps/Cloud Save Guardian from Dept. 2.

---

## 4.1 Core Premise & Tone Guardrails

Sentient 3D egg characters travel the multiverse to uncover why eggs are always the breakfast target. Tone is **darkly sarcastic, never cruel** — jokes punch at the absurdity of the premise, not at the player. This is the standing brief the Harsh Critic audits every dialogue pass against.

## 4.2 Character Roster (Dept. 1, Character Personality & Lore Creator)

| Character | Archetype | Voice | Signature Trait |
|---|---|---|---|
| **Detective Hardboil** | Hard-Boiled Detective | Deadpan noir narrator | Refuses to acknowledge he's already been boiled once — treats it as "a bad case" |
| **Madame Fried** | Fried Diva | Theatrical, self-important | Considers "sunny side up" a fashion statement, not a cooking method |
| **Scrambles** | Scrambled Neurotic | Anxious, fast-talking | Physically incapable of holding a single form for long — animation rig reflects this (Pillar 4.6) |

Each has a dedicated `CharacterProfileSO`: personality tags, VO pitch range, idle-animation set, and a dialogue pool ID used by the Sarcastic Dialogue Scriptwriter's line database.

## 4.3 Narrative Structure (Writer of the Endless Story)

- **Macro arc:** The Breakfast Conspiracy — a slow-burn mystery revealed across "dimension hubs" (biomes), each ending in a lore-reveal cinematic (Cinematic Director, Pillar 4.7).
- **Endless layer:** Past the authored arc, levels procedurally remix biome+boss+modifier combinations from the same data pools, with the Endless Story Writer's dialogue system inserting callback jokes referencing player milestones (e.g., level 250 unlocks a line acknowledging the player "should really see a doctor about this obsession").
- Narrative delivery is entirely diegetic-light: pre-level one-liners, boss banter, and recipe-book flavor text — no forced cutscene walls that block replay speed.

## 4.4 Psychological & Emotional Design (Dept. 1, agent 4)

Engineered responsibly — loops reward engagement, never exploit compulsion:

| Loop | Mechanism | Guardrail |
|---|---|---|
| **Dopamine** | Juicy match VFX/SFX, escalating combo particle density, screen-shake tiers | Diminishing visual returns past combo x6 to avoid sensory overload |
| **Oxytocin** | "Rescue" framing — cracked/endangered eggs highlighted before boss phases; saving them plays a warm, non-sarcastic beat (deliberate tonal contrast) | Never monetized directly (no "pay to rescue") — keeps the beat emotionally honest |
| **Adrenaline** | Timed clutch moments (last-move boss kill slow-mo at 0.4x timescale for 1.2s) | Slow-mo is cosmetic only, does not extend the real move-timer, so it can't be exploited |

This system cross-references Pillar 1's `StressDetector` — stress-tier state is one of the inputs the Emotional Designer's pacing curve reads to decide whether to offer a hint nudge instead of another difficulty ramp.

## 4.5 Seasonal & Limited-Time NPCs (NPC & Eventual Characters Architect)

- Spawned/despawned by Pillar 1's `SeasonalEventManager`.
- Each holiday NPC carries a 3–5 node **micro-questline** (`MicroQuestSO`): simple objective chains (e.g., "clear 3 Frozen-tagged matches") rewarding a cosmetic exclusive to that event window.
- Examples: `Jack-o'-Lantern Egg` (Halloween), `Santa Yolk` (Winter holiday).

## 4.6 Character & Grid Animator (rig notes)

- Blendshape-driven facial rig (12 blendshapes minimum per character: brow, eye-squint L/R, mouth-wide, mouth-smirk, cheek-puff, etc.) for comedic expressiveness without needing bone-heavy facial rigs (mobile budget).
- State machine: `Idle → Anticipation → MatchReact → ComboEscalate → Settle`, blended via Animator layers so combo reactions can interrupt idle without a hard cut.
- Match-3 "combo explosion" animation is procedural (scale + rotation curve driven by combo size), not hand-keyed per combo tier — keeps the animation budget flat regardless of how large a combo chain gets.

## 4.7 Cinematic & Short Video Director

- Biome-transition and boss-intro beats are 3-second Unity Timeline cutscenes, Cinemachine virtual cameras with a shared "comedic push-in" template (fast dolly-in + settle) reused across all characters to keep authoring cost low.
- No pre-rendered video — everything is in-engine Timeline so it inherits the live seasonal/weather dressing from Pillar 1 automatically.

## 4.8 Stages & Environment Architect

- Stage prefab sets are tagged by `SeasonType` + `WeatherState` and pooled/cross-faded exactly as described in Pillar 1.3.
- Each biome (dimension hub) has one base stage with modular season/weather dressing layers rather than fully separate stage sets — art budget discipline for a $0-budget production.

## 4.9 Adaptive Music & Synthetic Sound Designer

- Procedural audio state machine: `Explore → Tension(pre-boss) → Combat(boss) → Victory/Retry`, crossfaded via an `AudioMixerSnapshot` per state, driven by the same `WorldStateBus` events from Pillar 1 plus boss-phase events from Pillar 2.
- Character voice lines use lightweight synthetic voice synthesis (pitch/formant-shifted shared VO takes per archetype) to avoid a full VO budget while keeping each character sonically distinct.

## 4.10 Monetization & Economy Architect ($0-budget)

| System | Design |
|---|---|
| **Battle Pass** | Free-only track at launch (no paid tier until post-launch revenue justifies IAP integration); rewards cosmetics and Scramble Charges |
| **Rewarded Ads** | Opt-in only, never forced; grants extra Scramble Charges or hint reveals |
| **Skin Shop** | Soft-currency only at launch; cosmetics are modular (Pillar 5 socket system) so shop inventory is combinatorial, not asset-per-item |
| **Currencies** | Soft (Yolk Coins, earned via play) + Hard (Golden Yolks, earned via Battle Pass/ads only at launch — no real-money purchase path until a store account exists) |

## 4.11 Player Behavior Engine

- Tracks rolling win/loss rate, average moves-to-clear, and fail streak per level.
- Dynamic difficulty adjustment (DDA) nudges drop-rate tables ±8% max per adjustment step, and never adjusts mid-attempt (only between attempts) so play never feels like it "changed under" the player.
- Fail-streak ≥3 on the same level triggers a soft-assist: next attempt gets +1 starting booster, framed narratively ("Scrambles slipped you a lucky spatula").

## 4.12 SecOps, Cloud Save & Login Guardian

- Guest-first auth (no forced account creation to start playing), with optional upgrade to a linked account for cross-device Unity Cloud Save.
- Local save is AES-encrypted at rest; cloud sync uses conflict resolution favoring highest total progress (never silently overwrites a further-along save).
- Anti-cheat: server-side (or, for local-only builds, checksum-validated local) sanity bounds on currency/level deltas per session — flags, does not hard-ban, pending manual review, appropriate for a solo/small-team $0-budget project.

Full runtime specifications for `StressDetector` integration and dialogue trigger contracts are finalized in **Pillar 7**.
