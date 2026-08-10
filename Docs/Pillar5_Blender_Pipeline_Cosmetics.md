# PILLAR 5 — AUTOMATED BLENDER PYTHON PIPELINE & COSMETICS
### Character Match Fun 3D — Local CLI Execution Specification
Owning agent: Blender Python Automation Specialist. Supported by Apparel/Skins/Accessories Tailor (cosmetic design) and Procedural Mesh Fracture Specialist (fracture variant generation, see Pillar 3.5).

---

## 5.1 Execution Command

```bash
blender --background --python Blender/generate_assets.py -- --outdir "../Assets/Models" --lowpoly
```

Everything after `--` is forwarded as script args (parsed via `sys.argv`), so the same script can be re-run with different flags without editing source — CI-friendly for future automated art pipelines.

## 5.2 What the Script Generates

| Asset | Method | Poly Budget (target) |
|---|---|---|
| Egg base mesh (3 archetype silhouettes: Hardboiled/Fried/Scrambled) | Modifier-stack procedural (subdivided icosphere + shape lattice deform) | ≤ 450 tris |
| Sunglasses | Boolean-built from primitive lens + frame curves | ≤ 120 tris |
| Monocle | Torus + thin cylinder chain | ≤ 80 tris |
| Top Hat | Cylinder + torus brim | ≤ 150 tris |
| Beanie | Deformed UV sphere cap | ≤ 100 tris |
| Fracture variants (`Crack_Light/Medium/Shatter`) | `bpy.ops.object.add_fracture_cell_objects` (Cell Fracture add-on) run against each egg base, then decimated | ≤ 300 tris/shard |

All accessories are built around a shared **socket convention**: each egg archetype has empties named `Socket_Eyewear`, `Socket_Head`, so Unity import can auto-parent cosmetics via a naming-convention-driven prefab assembler (ties to Pillar 6's `SceneAutoBuilder`).

## 5.3 Export Convention

- Export format: `.fbx`, Y-up, scale 1.0 (Unity-native), one file per generated object into `Assets/Models/`.
- Naming: `Egg_<Archetype>.fbx`, `Cosmetic_<Category>_<Name>.fbx`, `Fracture_<Archetype>_<Severity>.fbx`.
- UVs: box-projected then packed via `bpy.ops.uv.smart_project(angle_limit=66, island_margin=0.02)` — sufficient for the flat-shaded stylized material approach (URP Toon-lit) used at runtime, no need for hand UV work in a $0-budget pipeline.
- Materials are NOT exported baked-in; Unity assigns URP materials post-import by name convention (art-directed centrally, keeps Blender-side iteration fast).

## 5.4 Cosmetics System (Apparel, Skins & Accessories Tailor)

- Every cosmetic is modular and socket-based rather than full-character skins — this is a deliberate art-budget decision: N archetypes × M accessories gives combinatorial variety (N+M assets instead of N×M).
- Cosmetic metadata (which socket, which archetypes it's compatible with, shop rarity tier) lives in a `CosmeticDefinitionSO` on the Unity side, generated in a manifest JSON by this same script (`cosmetic_manifest.json`) so both pipelines stay in sync automatically.

## 5.5 Idempotency & Safety

- Script clears the default scene (`bpy.ops.wm.read_factory_settings(use_empty=True)`) before generating, so repeated CLI runs are deterministic and reproducible.
- All mesh generation is seeded (`random.seed(ASSET_SEED)`) for reproducible builds across machines/CI.
- Output directory is created if missing; existing files with the same name are overwritten (this is the source-of-truth generator, not a hand-authored asset folder).

See `Blender/generate_assets.py` for the complete, runnable implementation.
