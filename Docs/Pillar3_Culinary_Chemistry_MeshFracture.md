# PILLAR 3 — CULINARY CHEMISTRY & PROCEDURAL MESH FRACTURE
### Character Match Fun 3D — Local C# Architecture Specification
Owning agents: Culinary Chemistry & Reaction Architect, Procedural Mesh Fracture Specialist, Superpowers & Boosters Designer.

---

## 3.1 Elemental Tag System

Every egg piece carries 0–2 `ElementalTag` values assigned at spawn (weighted by season/weather per Pillar 1, level design curve, and booster effects):

`Spicy · Frozen · HardBoiled · Beaten · Fried · Pickled`

Tags are a `[Flags] enum` so pieces can combine (e.g., a piece can be both `Spicy` and `HardBoiled`).

## 3.2 Reaction Matrix

A match that includes 2+ differently-tagged pieces triggers a **Culinary Reaction** instead of (or in addition to) a normal clear. Reactions are data-driven via `ReactionRecipeSO` so the "Multiverse Recipe Book" can grow post-launch without code changes.

| Combination | Reaction Name | Effect |
|---|---|---|
| Spicy + Frozen | **Thermal Shock** | Radial shockwave clears a 3x3 area around the match origin |
| Hard-Boiled + Beaten | **Meringue Shield** | Spawns a temporary shield tile that blocks the next hazard/boss counter-attack from affecting that column |
| Fried + Pickled | **Umami Overload** | Doubles score value of the next 2 matches (stacking window: 6s) |
| Spicy + Spicy (3+ stack) | **Scoville Cascade** | Chain-triggers an automatic adjacent match-check (free cascade, does not consume a move) |
| Frozen + Frozen (3+ stack) | **Flash Freeze** | Locks the lowest-value tile on the board for 1 turn as a light "cool-down" tempo effect |

### 3.2.1 Recipe Resolution Order
```
1. Detect match → collect ElementalTags of all pieces in the cleared group
2. Query ReactionDatabase for the highest-priority matching recipe (recipes have an explicit Priority int to resolve overlaps)
3. If found: fire ReactionExecutor.Execute(recipe, matchContext)
4. Log to MultiverseRecipeBook (per-player persistent unlock log — first-time discovery grants a bonus + codex entry, feeding long-term collection meta)
```

## 3.3 Multiverse Recipe Book (Data Schema)

```csharp
[Serializable]
public class RecipeBookEntry
{
    public string RecipeId;
    public ElementalTag ComponentA;
    public ElementalTag ComponentB;
    public bool Discovered;
    public DateTime FirstDiscoveredUtc;
    public string FlavorTextKey;   // localized, sarcastic in-fiction description
}
```
Persisted in the player's cloud/local save (SecOps system, Pillar 4) as an append-only discovery log — never resets, functions as a soft completionist meta-progression track.

## 3.4 Superpowers & Boosters (board-clearing)

**Agent:** Superpowers & Boosters Designer

| Booster | Trigger | Effect | Physical Impact |
|---|---|---|---|
| **Omelette Blast** | Player-activated (earned currency or match-5+) | Clears entire row + column from center point | Large radial `MeshFractureEngine` event, camera shake tier 2 |
| **Yolk Laser** | Player-activated | Clears a single column with a beam VFX, ignores shields | Sequential small fracture events per piece hit, staggered 0.05s apart for readability |
| **Egg-Sterminator** | Rare drop, single-use | Removes all instances of one randomly-chosen ElementalTag from the board | No fracture VFX — pieces "warp out" (keeps it visually distinct from destructive clears) |

All boosters route through the same `IBoardEffect` interface so the grid manager doesn't need to know booster-specific logic — keeps `GridManager3D` (Pillar 7) closed for modification per SOLID.

## 3.5 Procedural Mesh Fracture Specification

**Agent:** Procedural Mesh Fracture Specialist

### 3.5.1 Approach
Real-time Voronoi-based fracture is too expensive for mobile at 60fps with frequent match events, so the pipeline uses **pre-fractured mesh swapping**, generated offline by the Blender pipeline (Pillar 5):

1. Blender's Cell Fracture (via `bpy.ops.object.add_fracture_cell_objects`) pre-generates 3 fracture-severity variants per egg archetype at export time: `Crack_Light`, `Crack_Medium`, `Crack_Shatter`.
2. At runtime, `MeshFractureEngine` selects the variant based on impact force (from Pillar 2's projectile system or standard match-clear force) and swaps the intact mesh's `MeshRenderer` for a pooled fractured-piece prefab, then applies outward impulses to each shard (`Rigidbody.AddExplosionForce`) scaled by impact force.
3. Shard pieces are pooled and auto-despawn after 1.4s (fade-out over final 0.3s) — no long-lived fractured debris accumulating on mobile.

### 3.5.2 Force → Variant Mapping

| Impact Force (normalized 0–1) | Variant | Shard Count |
|---|---|---|
| 0.0 – 0.35 | `Crack_Light` | 4–6 shards |
| 0.35 – 0.7 | `Crack_Medium` | 8–12 shards |
| 0.7 – 1.0 | `Crack_Shatter` | 16–20 shards |

### 3.5.3 Performance Budget
- Max concurrent active fracture events on-screen: 6 (queued beyond that, oldest completes early/culls).
- Shard meshes capped at 300 tris each pre-decimation (Blender pipeline enforces via `bpy.ops.mesh.decimate` in `generate_assets.py`, Pillar 5).

Full `MeshFractureEngine.cs` and `CulinaryReactionSystem.cs` class contracts are finalized in **Pillar 7**.
