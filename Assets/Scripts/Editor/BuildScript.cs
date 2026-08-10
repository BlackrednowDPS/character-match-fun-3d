// BuildScript.cs
// Character Match Fun 3D — Pillar 6: Unity Batchmode Build Script
// Owning agent: Unity Editor Automation & Scene Builder
// Audited by: Harsh Critic, QA & System Auditor
//
// Exposes static entry points callable from Unity CLI batchmode:
//
//   Unity.exe -batchmode -quit -projectPath "." \
//     -executeMethod BuildScript.BuildStandalone -logFile build_log.txt
//
//   Unity.exe -batchmode -quit -projectPath "." \
//     -executeMethod BuildScript.BuildMobile -logFile build_log.txt
//
// Both entry points first force a fresh SceneAutoBuilder pass so a CLI
// build is never stale relative to the Editor-visible scene, then run
// BuildPipeline.BuildPlayer, then write a machine-readable
// Builds/build_report.json so pass/fail can be checked programmatically
// without parsing the Unity log — the "zero-error CLI build status"
// validation the Harsh Critic audit step depends on.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CMF3D.EditorTools
{
    public static class BuildScript
    {
        private const string SceneToBuild = "Assets/Scenes/Main_Game.unity";
        private const string BuildsRoot = "Builds";
        private const string ProductName = "CharacterMatchFun3D";

        [MenuItem("Tools/Character Match Fun 3D/Build Standalone")]
        public static void BuildStandalone()
        {
            RunBuild(
                target: EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneOSX
                    ? BuildTarget.StandaloneOSX
                    : BuildTarget.StandaloneWindows64,
                targetGroup: BuildTargetGroup.Standalone,
                outputSubfolder: "Standalone");
        }

        [MenuItem("Tools/Character Match Fun 3D/Build Mobile (Android)")]
        public static void BuildMobile()
        {
            RunBuild(
                target: BuildTarget.Android,
                targetGroup: BuildTargetGroup.Android,
                outputSubfolder: "Android");
        }

        private static void RunBuild(BuildTarget target, BuildTargetGroup targetGroup, string outputSubfolder)
        {
            var startTime = DateTime.UtcNow;
            bool succeeded = false;
            string errorMessage = null;
            BuildReport report = null;

            try
            {
                Debug.Log($"[BuildScript] Ensuring scene is up to date via SceneAutoBuilder...");
                SceneAutoBuilder.BuildScene();

                EnsureBuildTarget(target, targetGroup);

                string outDir = Path.Combine(BuildsRoot, outputSubfolder);
                Directory.CreateDirectory(outDir);

                string executableName = target == BuildTarget.Android
                    ? $"{ProductName}.apk"
                    : (Application.platform == RuntimePlatform.OSXEditor
                        ? $"{ProductName}.app"
                        : $"{ProductName}.exe");

                string locationPathName = Path.Combine(outDir, executableName);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SceneToBuild },
                    locationPathName = locationPathName,
                    target = target,
                    targetGroup = targetGroup,
                    options = BuildOptions.None,
                };

                report = BuildPipeline.BuildPlayer(options);
                succeeded = report.summary.result == BuildResult.Succeeded;

                if (!succeeded)
                {
                    errorMessage = $"Build finished with result: {report.summary.result} " +
                                    $"({report.summary.totalErrors} error(s)).";
                }
            }
            catch (Exception ex)
            {
                succeeded = false;
                errorMessage = ex.ToString();
                Debug.LogError($"[BuildScript] Exception during build: {ex}");
            }
            finally
            {
                WriteBuildReport(report, succeeded, errorMessage, startTime, target, outputSubfolder);

                // Batchmode callers rely on the process exit code to detect
                // failure without parsing logs — this is the concrete
                // mechanism behind the Harsh Critic's "zero-error CLI build
                // status" audit gate.
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(succeeded ? 0 : 1);
                }
            }
        }

        private static void EnsureBuildTarget(BuildTarget target, BuildTargetGroup targetGroup)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target);
                if (!switched)
                {
                    throw new InvalidOperationException(
                        $"Failed to switch active build target to {target}. " +
                        "Ensure the corresponding Unity Build Support module is installed.");
                }
            }
        }

        private static void WriteBuildReport(
            BuildReport report,
            bool succeeded,
            string errorMessage,
            DateTime startTimeUtc,
            BuildTarget target,
            string outputSubfolder)
        {
            Directory.CreateDirectory(BuildsRoot);
            string reportPath = Path.Combine(BuildsRoot, "build_report.json");

            int totalErrors = report?.summary.totalErrors ?? -1;
            int totalWarnings = report?.summary.totalWarnings ?? -1;
            ulong totalSizeBytes = report?.summary.totalSize ?? 0;
            double buildSeconds = (DateTime.UtcNow - startTimeUtc).TotalSeconds;

            string json = "{\n" +
                $"  \"succeeded\": {(succeeded ? "true" : "false")},\n" +
                $"  \"buildTarget\": \"{target}\",\n" +
                $"  \"outputSubfolder\": \"{outputSubfolder}\",\n" +
                $"  \"totalErrors\": {totalErrors},\n" +
                $"  \"totalWarnings\": {totalWarnings},\n" +
                $"  \"totalSizeBytes\": {totalSizeBytes},\n" +
                $"  \"buildDurationSeconds\": {buildSeconds:F2},\n" +
                $"  \"startedAtUtc\": \"{startTimeUtc:o}\",\n" +
                $"  \"errorMessage\": {(errorMessage == null ? "null" : $"\"{EscapeJson(errorMessage)}\"")}\n" +
                "}\n";

            File.WriteAllText(reportPath, json);
            Debug.Log($"[BuildScript] Build report written to {reportPath} (succeeded={succeeded}).");
        }

        private static string EscapeJson(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }
    }
}
