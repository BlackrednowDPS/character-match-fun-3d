// URPSetup.cs
// Character Match Fun 3D — Editor utility
//
// Creates a Universal Render Pipeline asset (plus its renderer data) and
// assigns it in Graphics + Quality settings.
//
// Without this the project silently falls back to the Built-in pipeline,
// which Unity 6 flags as deprecated and which would make every URP material
// render incorrectly (magenta) at runtime. BuildScript calls this before
// building so CI never produces a Built-in-pipeline player by accident.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CMF3D.EditorTools
{
    public static class URPSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string UrpAssetPath = SettingsFolder + "/CMF3D_URPAsset.asset";
        private const string RendererDataPath = SettingsFolder + "/CMF3D_URPRenderer.asset";

        [MenuItem("Tools/Character Match Fun 3D/Setup URP Pipeline")]
        public static void SetupMenuEntry()
        {
            EnsureUrpAssigned();
            EditorUtility.DisplayDialog(
                "Character Match Fun 3D",
                "URP asset created and assigned in Graphics + Quality settings.",
                "OK");
        }

        /// <summary>
        /// Idempotent: reuses the existing URP asset if one is already on disk,
        /// and only writes settings when the assignment actually differs.
        /// </summary>
        public static void EnsureUrpAssigned()
        {
            EnsureFolder(SettingsFolder);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);
                Debug.Log($"[URPSetup] Created renderer data at {RendererDataPath}");
            }

            var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urpAsset == null)
            {
                urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urpAsset, UrpAssetPath);
                Debug.Log($"[URPSetup] Created URP asset at {UrpAssetPath}");
            }

            AssetDatabase.SaveAssets();

            if (GraphicsSettings.defaultRenderPipeline != urpAsset)
            {
                GraphicsSettings.defaultRenderPipeline = urpAsset;
                Debug.Log("[URPSetup] Assigned URP asset to GraphicsSettings.defaultRenderPipeline");
            }

            if (QualitySettings.renderPipeline != urpAsset)
            {
                QualitySettings.renderPipeline = urpAsset;
                Debug.Log("[URPSetup] Assigned URP asset to QualitySettings.renderPipeline");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

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
