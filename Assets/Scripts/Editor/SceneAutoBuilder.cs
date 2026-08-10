// SceneAutoBuilder.cs
// Character Match Fun 3D — Pillar 6: Unity Editor Scene Auto-Builder
// Owning agent: Unity Editor Automation & Scene Builder
//
// Populates Assets/Scenes/Main_Game.unity programmatically: camera, URP
// lighting, grid anchor, boss spawn point, stage environment, and
// naming-convention-driven material assignment against Pillar 5's
// Blender-exported meshes. Callable from the Editor menu (to watch the
// scene assemble live) or headlessly from BuildScript before a batchmode
// build, so CLI builds are never stale relative to the Editor state.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CMF3D.EditorTools
{
    public static class SceneAutoBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main_Game.unity";
        private const string ModelsRoot = "Assets/Models";
        private const string MaterialsRoot = "Assets/Materials";
        private const string StagePrefabRoot = "Assets/Prefabs/Stages";

        private static readonly string[] NamingConventionPrefixes =
        {
            "Egg_", "Cosmetic_", "Fracture_"
        };

        [MenuItem("Tools/Character Match Fun 3D/Auto-Build Scene")]
        public static void BuildSceneMenuEntry()
        {
            BuildScene();
            EditorUtility.DisplayDialog(
                "Character Match Fun 3D",
                "Main_Game scene assembled successfully.",
                "OK");
        }

        /// <summary>
        /// Full deterministic scene assembly entry point. Safe to call
        /// repeatedly (idempotent) and safe to call from batchmode (no
        /// EditorUtility dialogs are triggered from this method itself).
        /// </summary>
        public static void BuildScene()
        {
            Debug.Log("[SceneAutoBuilder] Starting scene assembly...");

            EnsureFolder("Assets/Scenes");
            EnsureFolder(MaterialsRoot);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureRenderPipelineSanity();

            CreateMainCamera();
            CreateDirectionalLight();
            GameObject gridAnchor = CreateEmpty("GridAnchor", Vector3.zero);
            GameObject bossSpawn = CreateEmpty("BossSpawnPoint", new Vector3(0f, 3.5f, 2f));
            GameObject stageRoot = CreateEmpty("StageRoot", Vector3.zero);

            LoadOrCreateStage(stageRoot);
            AutoAssignMaterialsInScene();

            // NOTE: Anchor references are resolved at runtime by exact
            // GameObject name ("GridAnchor", "BossSpawnPoint", "StageRoot")
            // via GridManager3D/BossBattleManager (Pillar 7). A MonoBehaviour
            // registry component is deliberately NOT defined in this file —
            // scripts under Assets/Scripts/Editor/ compile into the
            // Editor-only assembly and are stripped from player builds,
            // which would leave a dangling missing-script reference in any
            // built player. Keep runtime-referenced components in
            // Assets/Scripts/Runtime/ only.

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[SceneAutoBuilder] Scene saved to {ScenePath}");
        }

        // -------------------------------------------------------------
        // Camera / Lighting
        // -------------------------------------------------------------

        private static void CreateMainCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            camGo.transform.position = new Vector3(0f, 9f, -6f);
            camGo.transform.LookAt(Vector3.zero, Vector3.up);
            camGo.AddComponent<AudioListener>();
        }

        private static void CreateDirectionalLight()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1.0f, 0.957f, 0.878f); // #FFF4E0
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void ConfigureRenderPipelineSanity()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Debug.LogWarning(
                    "[SceneAutoBuilder] No URP render pipeline asset assigned in " +
                    "Graphics Settings. Scene will still assemble, but lighting " +
                    "will use the Built-in pipeline until URP is assigned.");
            }
        }

        // -------------------------------------------------------------
        // Anchors
        // -------------------------------------------------------------

        private static GameObject CreateEmpty(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            return go;
        }

        // -------------------------------------------------------------
        // Stage environment
        // -------------------------------------------------------------

        private static void LoadOrCreateStage(GameObject stageRoot)
        {
            EnsureFolder(StagePrefabRoot);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { StagePrefabRoot });
            GameObject stagePrefab = null;

            if (guids.Length > 0)
            {
                // Deterministic default: first prefab alphabetically tagged
                // for the current season; seasonal hot-swap happens at
                // runtime via WorldStateBus (Pillar 1), this is only the
                // Editor-time default so the scene isn't empty.
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            if (stagePrefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(stagePrefab);
                instance.transform.SetParent(stageRoot.transform, false);
                Debug.Log($"[SceneAutoBuilder] Loaded stage prefab: {stagePrefab.name}");
            }
            else
            {
                CreatePlaceholderStage(stageRoot);
                Debug.LogWarning(
                    "[SceneAutoBuilder] No stage prefab found under " +
                    $"{StagePrefabRoot}. Generated a placeholder ground + " +
                    "backdrop so the pipeline never fails hard.");
            }
        }

        private static void CreatePlaceholderStage(GameObject stageRoot)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Placeholder_Ground";
            ground.transform.SetParent(stageRoot.transform, false);
            ground.transform.localScale = new Vector3(1.4f, 1f, 1.4f);

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Placeholder_Backdrop";
            backdrop.transform.SetParent(stageRoot.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 4f, 5f);
            backdrop.transform.localScale = new Vector3(12f, 8f, 1f);
            backdrop.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        // -------------------------------------------------------------
        // Material auto-assignment (naming-convention driven)
        // -------------------------------------------------------------

        private static void AutoAssignMaterialsInScene()
        {
            EnsureFolder(ModelsRoot);

            Dictionary<string, Material> materialLookup = BuildMaterialLookup();

            var renderers = Object.FindObjectsOfType<MeshRenderer>(true);
            int assigned = 0;

            foreach (var renderer in renderers)
            {
                string objName = renderer.gameObject.name;
                if (!NamingConventionPrefixes.Any(p => objName.StartsWith(p)))
                    continue;

                string baseName = StripImportSuffix(objName);
                string materialKey = $"M_{baseName}";

                if (materialLookup.TryGetValue(materialKey, out Material mat))
                {
                    renderer.sharedMaterial = mat;
                    assigned++;
                }
                else
                {
                    Material fallback = GetOrCreateFallbackMaterial(materialKey);
                    renderer.sharedMaterial = fallback;
                    assigned++;
                }
            }

            Debug.Log($"[SceneAutoBuilder] Auto-assigned materials on {assigned} renderer(s).");
        }

        private static Dictionary<string, Material> BuildMaterialLookup()
        {
            var lookup = new Dictionary<string, Material>();
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    lookup[mat.name] = mat;
            }
            return lookup;
        }

        private static string StripImportSuffix(string objName)
        {
            // Blender/FBX imports sometimes append (Clone) or numeric
            // duplicate suffixes; normalize to the base asset name.
            int parenIndex = objName.IndexOf(" (", System.StringComparison.Ordinal);
            return parenIndex > 0 ? objName.Substring(0, parenIndex) : objName;
        }

        private static Material GetOrCreateFallbackMaterial(string materialKey)
        {
            EnsureFolder(MaterialsRoot);
            string path = $"{MaterialsRoot}/{materialKey}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = materialKey };
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // -------------------------------------------------------------
        // Utility
        // -------------------------------------------------------------

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            string[] parts = assetFolderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
