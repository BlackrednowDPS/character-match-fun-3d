"""
generate_assets.py
Character Match Fun 3D — Automated Blender Asset Pipeline (Pillar 5)
Owning agent: Blender Python Automation Specialist

Run headless via CLI:
    blender --background --python Blender/generate_assets.py -- --outdir "../Assets/Models" --lowpoly

Generates:
    - 3 egg archetype base meshes (Hardboiled, Fried, Scrambled) with named
      socket empties (Socket_Eyewear, Socket_Head) for Unity-side auto-assembly.
    - Modular cosmetics: Sunglasses, Monocle, TopHat, Beanie.
    - Pre-fractured shard variants (Light / Medium / Shatter) per archetype
      for Pillar 3's MeshFractureEngine runtime mesh-swap system.
    - cosmetic_manifest.json describing socket compatibility for the Unity
      SceneAutoBuilder / cosmetic shop system.

This script is idempotent and deterministic (seeded RNG) so repeated CLI
runs on any machine produce identical output.
"""

import bpy
import bmesh
import json
import os
import random
import sys
import mathutils

# ---------------------------------------------------------------------------
# 1. ARGUMENT PARSING (everything after "--")
# ---------------------------------------------------------------------------

def parse_args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []

    args = {
        "outdir": "../Assets/Models",
        "lowpoly": False,
        "seed": 8261,
    }
    i = 0
    while i < len(argv):
        token = argv[i]
        if token == "--outdir" and i + 1 < len(argv):
            args["outdir"] = argv[i + 1]
            i += 2
        elif token == "--lowpoly":
            args["lowpoly"] = True
            i += 1
        elif token == "--seed" and i + 1 < len(argv):
            args["seed"] = int(argv[i + 1])
            i += 2
        else:
            i += 1
    return args


ARGS = parse_args()
ASSET_SEED = ARGS["seed"]
random.seed(ASSET_SEED)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, ARGS["outdir"]))
os.makedirs(OUT_DIR, exist_ok=True)

POLY_BUDGET = {
    "egg": 450,
    "sunglasses": 120,
    "monocle": 80,
    "tophat": 150,
    "beanie": 100,
    "shard": 300,
}

EGG_ARCHETYPES = {
    # name : (radius_x, radius_y, radius_z, squash, notes)
    "Hardboiled": (0.48, 0.48, 0.62, 1.00, "Detective — upright, symmetrical"),
    "Fried":      (0.62, 0.55, 0.30, 1.35, "Diva — flattened, wide silhouette"),
    "Scrambled":  (0.55, 0.50, 0.50, 0.85, "Neurotic — lumpy, asymmetric"),
}

COSMETIC_CATEGORIES = ["Eyewear", "Head"]


# ---------------------------------------------------------------------------
# 2. SCENE RESET
# ---------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for coll in list(bpy.data.collections):
        if coll.name != "Collection":
            bpy.data.collections.remove(coll)


def new_collection(name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
    return coll


def link_only_to(obj, collection):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)


# ---------------------------------------------------------------------------
# 3. GEOMETRY HELPERS
# ---------------------------------------------------------------------------

def decimate_to_budget(obj, target_tris):
    """Applies a Decimate modifier ratio-tuned to approach the target
    triangle budget, then applies it so exported FBX carries plain geometry."""
    bpy.context.view_layer.objects.active = obj
    mesh = obj.data
    current_tris = sum(len(p.vertices) - 2 for p in mesh.polygons)  # approx tri count
    if current_tris <= target_tris or current_tris == 0:
        return
    ratio = max(0.05, min(1.0, target_tris / float(current_tris)))
    mod = obj.modifiers.new(name="Decimate", type='DECIMATE')
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=mod.name)


def smart_uv_unwrap(obj):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=66.0, island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')


def add_socket_empty(parent_obj, name, local_offset):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = 'PLAIN_AXES'
    empty.empty_display_size = 0.05
    empty.parent = parent_obj
    empty.location = local_offset
    bpy.context.scene.collection.objects.link(empty)
    return empty


def shade_smooth(obj):
    bpy.context.view_layer.objects.active = obj
    for poly in obj.data.polygons:
        poly.use_smooth = True


# ---------------------------------------------------------------------------
# 4. EGG BASE MESH GENERATION
# ---------------------------------------------------------------------------

def create_egg_base(name, rx, ry, rz, squash_top):
    """Builds a stylized egg silhouette from an icosphere using a
    lattice-free vertex deform: top hemisphere pinched, bottom rounded."""
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=3, radius=1.0, location=(0, 0, 0)
    )
    obj = bpy.context.active_object
    obj.name = f"Egg_{name}"

    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)

    for v in bm.verts:
        x, y, z = v.co.x, v.co.y, v.co.z
        # Egg profile: narrower top, wider bottom (classic egg silhouette)
        t = (z + 1.0) * 0.5  # 0 at bottom, 1 at top
        top_pinch = 1.0 - (t ** 2.2) * (squash_top - 1.0) * 0.35
        v.co.x = x * rx * top_pinch
        v.co.y = y * ry * top_pinch
        v.co.z = z * rz

        # Scrambled archetype gets mild per-vertex jitter for a lumpy look
        if name == "Scrambled":
            jitter = 0.04
            v.co.x += random.uniform(-jitter, jitter)
            v.co.y += random.uniform(-jitter, jitter)
            v.co.z += random.uniform(-jitter, jitter)

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    shade_smooth(obj)
    smart_uv_unwrap(obj)

    if ARGS["lowpoly"]:
        decimate_to_budget(obj, POLY_BUDGET["egg"])

    # Socket anchors for modular cosmetics (Unity SceneAutoBuilder reads
    # these names to auto-parent accessory prefabs at import time).
    add_socket_empty(obj, "Socket_Head", (0.0, 0.0, rz * 0.92))
    add_socket_empty(obj, "Socket_Eyewear", (0.0, ry * 0.75, rz * 0.15))

    return obj


# ---------------------------------------------------------------------------
# 5. COSMETIC GENERATION
# ---------------------------------------------------------------------------

def create_sunglasses():
    bpy.ops.mesh.primitive_torus_add(
        major_radius=0.22, minor_radius=0.045, location=(0, 0, 0)
    )
    frame = bpy.context.active_object
    frame.scale = (1.3, 0.55, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.ops.mesh.primitive_cube_add(size=0.05, location=(0, 0.05, 0))
    bridge = bpy.context.active_object
    bridge.scale = (2.0, 1.0, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.ops.object.select_all(action='DESELECT')
    frame.select_set(True)
    bridge.select_set(True)
    bpy.context.view_layer.objects.active = frame
    bpy.ops.object.join()

    frame.name = "Cosmetic_Eyewear_Sunglasses"
    shade_smooth(frame)
    smart_uv_unwrap(frame)
    if ARGS["lowpoly"]:
        decimate_to_budget(frame, POLY_BUDGET["sunglasses"])
    return frame


def create_monocle():
    bpy.ops.mesh.primitive_torus_add(
        major_radius=0.14, minor_radius=0.02, location=(0.16, 0, 0)
    )
    lens = bpy.context.active_object

    bpy.ops.mesh.primitive_cylinder_add(
        radius=0.006, depth=0.35, location=(0.1, -0.15, -0.15)
    )
    chain = bpy.context.active_object
    chain.rotation_euler = (0.9, 0, 0.3)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    bpy.ops.object.select_all(action='DESELECT')
    lens.select_set(True)
    chain.select_set(True)
    bpy.context.view_layer.objects.active = lens
    bpy.ops.object.join()

    lens.name = "Cosmetic_Eyewear_Monocle"
    shade_smooth(lens)
    smart_uv_unwrap(lens)
    if ARGS["lowpoly"]:
        decimate_to_budget(lens, POLY_BUDGET["monocle"])
    return lens


def create_tophat():
    bpy.ops.mesh.primitive_cylinder_add(
        radius=0.28, depth=0.05, location=(0, 0, 0)
    )
    brim = bpy.context.active_object

    bpy.ops.mesh.primitive_cylinder_add(
        radius=0.18, depth=0.35, location=(0, 0, 0.2)
    )
    crown = bpy.context.active_object

    bpy.ops.object.select_all(action='DESELECT')
    brim.select_set(True)
    crown.select_set(True)
    bpy.context.view_layer.objects.active = brim
    bpy.ops.object.join()

    brim.name = "Cosmetic_Head_TopHat"
    shade_smooth(brim)
    smart_uv_unwrap(brim)
    if ARGS["lowpoly"]:
        decimate_to_budget(brim, POLY_BUDGET["tophat"])
    return brim


def create_beanie():
    bpy.ops.mesh.primitive_uv_sphere_add(
        radius=0.24, location=(0, 0, 0.05), segments=16, ring_count=8
    )
    obj = bpy.context.active_object

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for v in bm.verts:
        if v.co.z < 0.0:
            v.co.z *= 0.15  # flatten the underside so it "caps" the egg
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    obj.name = "Cosmetic_Head_Beanie"
    shade_smooth(obj)
    smart_uv_unwrap(obj)
    if ARGS["lowpoly"]:
        decimate_to_budget(obj, POLY_BUDGET["beanie"])
    return obj


# ---------------------------------------------------------------------------
# 6. PROCEDURAL FRACTURE VARIANTS (feeds Pillar 3 MeshFractureEngine)
# ---------------------------------------------------------------------------

def try_enable_cell_fracture():
    try:
        bpy.ops.preferences.addon_enable(module="object_fracture_cell")
        return True
    except Exception:
        return False


def manual_bisect_fracture(obj, shard_count):
    """Fallback fracture: repeatedly bisects a duplicate mesh with random
    planes to approximate a Voronoi shatter when the Cell Fracture addon
    is unavailable in the running Blender build."""
    shards = []
    src = obj.copy()
    src.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(src)
    working = [src]

    cuts_needed = max(1, shard_count - 1)
    for _ in range(cuts_needed):
        target = working.pop(random.randrange(len(working)))
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bm = bmesh.from_edit_mesh(target.data)
        center = mathutils.Vector((
            random.uniform(-0.15, 0.15),
            random.uniform(-0.15, 0.15),
            random.uniform(-0.2, 0.2),
        ))
        normal = mathutils.Vector((
            random.uniform(-1, 1),
            random.uniform(-1, 1),
            random.uniform(-1, 1),
        )).normalized()
        bmesh.ops.bisect_plane(
            bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
            plane_co=center, plane_no=normal, clear_outer=False, clear_inner=False,
        )
        bmesh.update_edit_mesh(target.data)
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.mesh.separate(type='LOOSE')
        working = [o for o in bpy.context.selected_objects if o.type == 'MESH']
        if not working:
            working = [target]

    shards = working
    return shards


def create_fracture_variants(base_obj, archetype_name):
    severities = {
        "Light": (5, POLY_BUDGET["shard"]),
        "Medium": (10, POLY_BUDGET["shard"]),
        "Shatter": (18, POLY_BUDGET["shard"]),
    }
    variant_objects = {}
    has_cell_fracture = try_enable_cell_fracture()

    for severity, (shard_count, tri_budget) in severities.items():
        bpy.ops.object.select_all(action='DESELECT')
        base_obj.select_set(True)
        bpy.context.view_layer.objects.active = base_obj

        if has_cell_fracture:
            try:
                bpy.ops.object.add_fracture_cell_objects(
                    source={'OBJECT'},
                    source_limit=shard_count,
                    use_smooth_faces=False,
                    use_sharp_edges=True,
                    use_data_match=True,
                    margin=0.001,
                )
                shards = [o for o in bpy.context.selected_objects if o.type == 'MESH']
            except Exception:
                shards = manual_bisect_fracture(base_obj, shard_count)
        else:
            shards = manual_bisect_fracture(base_obj, shard_count)

        variant_coll = new_collection(f"Fracture_{archetype_name}_{severity}")
        for i, shard in enumerate(shards):
            shard.name = f"Fracture_{archetype_name}_{severity}_{i:02d}"
            decimate_to_budget(shard, tri_budget)
            shade_smooth(shard)
            link_only_to(shard, variant_coll)

        variant_objects[severity] = shards

    return variant_objects


# ---------------------------------------------------------------------------
# 7. EXPORT
# ---------------------------------------------------------------------------

def export_fbx(objects, filename):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    if not objects:
        return
    bpy.context.view_layer.objects.active = objects[0]

    filepath = os.path.join(OUT_DIR, filename)
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        global_scale=1.0,
        axis_forward='-Z',
        axis_up='Y',
        apply_unit_scale=True,
        bake_space_transform=True,
        object_types={'MESH', 'EMPTY'},
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
    )
    print(f"[generate_assets] exported {filepath}")


# ---------------------------------------------------------------------------
# 8. MANIFEST FOR UNITY-SIDE COSMETIC SYSTEM
# ---------------------------------------------------------------------------

def write_cosmetic_manifest(cosmetics):
    manifest = {
        "generatedBySeed": ASSET_SEED,
        "archetypes": list(EGG_ARCHETYPES.keys()),
        "cosmetics": [
            {
                "id": obj.name,
                "category": category,
                "compatibleArchetypes": list(EGG_ARCHETYPES.keys()),
                "socket": f"Socket_{category}",
            }
            for obj, category in cosmetics
        ],
    }
    manifest_path = os.path.join(OUT_DIR, "cosmetic_manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print(f"[generate_assets] wrote {manifest_path}")


# ---------------------------------------------------------------------------
# 9. MAIN
# ---------------------------------------------------------------------------

def main():
    print(f"[generate_assets] starting — seed={ASSET_SEED} outdir={OUT_DIR} lowpoly={ARGS['lowpoly']}")
    reset_scene()

    # --- Egg archetypes ---
    egg_objects = []
    for name, (rx, ry, rz, squash, _notes) in EGG_ARCHETYPES.items():
        egg = create_egg_base(name, rx, ry, rz, squash)
        egg_objects.append(egg)
        export_fbx([egg] + [c for c in egg.children], f"Egg_{name}.fbx")

    # --- Cosmetics ---
    sunglasses = create_sunglasses()
    monocle = create_monocle()
    tophat = create_tophat()
    beanie = create_beanie()

    cosmetics = [
        (sunglasses, "Eyewear"),
        (monocle, "Eyewear"),
        (tophat, "Head"),
        (beanie, "Head"),
    ]
    for obj, _category in cosmetics:
        export_fbx([obj], f"{obj.name}.fbx")

    write_cosmetic_manifest(cosmetics)

    # --- Fracture variants (per archetype, feeds Pillar 3) ---
    for egg in egg_objects:
        archetype_name = egg.name.replace("Egg_", "")
        variants = create_fracture_variants(egg, archetype_name)
        for severity, shards in variants.items():
            export_fbx(shards, f"Fracture_{archetype_name}_{severity}.fbx")

    print("[generate_assets] complete.")


if __name__ == "__main__":
    main()
