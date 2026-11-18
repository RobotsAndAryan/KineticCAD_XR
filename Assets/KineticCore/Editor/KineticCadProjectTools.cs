#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace KineticCore.Editor
{
    [InitializeOnLoad]
    public static class KineticCadProjectTools
    {
        public const string TargetPackageId = "com.RobotsAndAryan.KineticCADXR";
        public const string LegacyTemplatePackageId = "com.UnityTechnologies.com.unity.template.urpblank";
        public const string TargetCompanyName = "RobotsAndAryan";
        public const string TargetProductName = "KineticCAD_XR";
        public const string TargetBundleVersion = "1.0.0";
        public const int MinBundleVersionCode = 2;

        static KineticCadProjectTools()
        {
            EditorApplication.delayCall += VerifyProjectSettings;
        }

        [MenuItem("KineticCAD/Verify Project & Shader Settings")]
        public static void VerifyProjectSettings()
        {
            // Never mutate PlayerSettings or re-import assets while Unity is compiling or updating
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            bool dirty = false;

            RemoveBundledNebiusKeyFromResources();
            dirty |= EnsureStoreCompliantPlayerSettings();

            if (!BuildPipeline.isBuildingPlayer)
            {
                // 1. Remove any HideFlags.DontSave shaders (e.g. GUI/Text Shader from unity default resources)
                //    from in-memory GraphicsSettings.asset and Meta Quest.asset so Android Player builds never fail!
                UnityEngine.Object[] graphicsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
                if (graphicsAssets != null)
                {
                    foreach (UnityEngine.Object obj in graphicsAssets)
                    {
                        if (obj != null)
                            dirty |= CleanDontSaveShaders(obj);
                    }
                }

                UnityEngine.Object[] profileAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/Build Profiles/Meta Quest.asset");
                if (profileAssets != null)
                {
                    foreach (UnityEngine.Object obj in profileAssets)
                    {
                        if (obj != null)
                            dirty |= CleanDontSaveShaders(obj);
                    }
                }

                // 2. Ensure OculusProjectConfig.asset has insightPassthroughEnabled = true in memory
                UnityEngine.Object ovrCfg = AssetDatabase.LoadMainAssetAtPath("Assets/Oculus/OculusProjectConfig.asset");
                if (ovrCfg != null)
                {
                    SerializedObject ovrSo = new SerializedObject(ovrCfg);
                    SerializedProperty ptProp = ovrSo.FindProperty("insightPassthroughEnabled");
                    if (ptProp != null && !ptProp.boolValue)
                    {
                        ptProp.boolValue = true;
                        ovrSo.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(ovrCfg);
                        dirty = true;
                    }
                }

                // 3. Ensure Meta Quest (Build Profile) quality level has a valid URP asset assigned
                RenderPipelineAsset mobileUrp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
                UnityEngine.Object[] qualityAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
                if (mobileUrp != null && qualityAssets != null && qualityAssets.Length > 0)
                {
                    SerializedObject qso = new SerializedObject(qualityAssets[0]);
                    SerializedProperty levels = qso.FindProperty("m_QualitySettings");
                    if (levels != null && levels.isArray)
                    {
                        bool qDirty = false;
                        for (int i = 0; i < levels.arraySize; i++)
                        {
                            SerializedProperty level = levels.GetArrayElementAtIndex(i);
                            SerializedProperty crp = level.FindPropertyRelative("customRenderPipeline");
                            if (crp != null && crp.objectReferenceValue == null)
                            {
                                crp.objectReferenceValue = mobileUrp;
                                qDirty = true;
                            }
                        }
                        if (qDirty)
                        {
                            qso.ApplyModifiedPropertiesWithoutUndo();
                            dirty = true;
                        }
                    }
                }

                if (dirty)
                    AssetDatabase.SaveAssets();
            }

            if (dirty)
            {
                Debug.Log($"[KineticCAD Editor] Verified 64-bit ARM64, package '{TargetPackageId}', URP Pipeline, Shaders, and Quest 3 Passthrough Config.");
            }
        }

        /// <summary>
        /// Builds a clean 64-bit-only ARM64 Meta Quest 3 APK with unique package ID into unity_vr_apps/KineticCAD_final_v4_meta.apk.
        /// </summary>
        [MenuItem("KineticCAD/Build Clean 64-Bit Quest 3 APK (Competition)")]
        public static void BuildClean64BitQuestApk()
        {
            VerifyProjectSettings();
            AssetDatabase.ImportAsset("Assets/Settings/Build Profiles/Meta Quest.asset", ImportAssetOptions.ForceUpdate);

            string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "unity_vr_apps"));
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string apkPath = Path.Combine(outputDir, "KineticCAD_final_v4_meta.apk");
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s != null && s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                scenes = new[] { "Assets/Scenes/SampleScene.unity" };

            BuildPlayerOptions opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[KineticCAD Build] Succeeded! Clean 64-bit APK ({TargetPackageId}) written to: {apkPath}");
                EditorUtility.RevealInFinder(apkPath);
            }
            else
            {
                Debug.LogError($"[KineticCAD Build] Failed with result: {report.summary.result} (errors: {report.summary.totalErrors})");
            }
        }

        private static bool EnsureStoreCompliantPlayerSettings()
        {
            bool modified = false;

            if (PlayerSettings.companyName != TargetCompanyName)
            {
                PlayerSettings.companyName = TargetCompanyName;
                modified = true;
            }
            if (PlayerSettings.productName != TargetProductName)
            {
                PlayerSettings.productName = TargetProductName;
                modified = true;
            }
            if (PlayerSettings.bundleVersion != TargetBundleVersion)
            {
                PlayerSettings.bundleVersion = TargetBundleVersion;
                modified = true;
            }

            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != TargetPackageId)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, TargetPackageId);
                modified = true;
            }
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone) != TargetPackageId)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, TargetPackageId);
                modified = true;
            }

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                modified = true;
            }
            if (PlayerSettings.Android.bundleVersionCode < MinBundleVersionCode)
            {
                PlayerSettings.Android.bundleVersionCode = MinBundleVersionCode;
                modified = true;
            }
            if (!PlayerSettings.Android.forceInternetPermission)
            {
                PlayerSettings.Android.forceInternetPermission = true;
                modified = true;
            }

            // Disable any local ARMv7 native plugins placed under Assets/
            if (!BuildPipeline.isBuildingPlayer)
            {
                foreach (PluginImporter importer in PluginImporter.GetAllImporters())
                {
                    if (importer == null || string.IsNullOrEmpty(importer.assetPath))
                        continue;
                    if (!importer.assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!importer.GetCompatibleWithPlatform(BuildTarget.Android))
                        continue;

                    string cpu = importer.GetPlatformData(BuildTarget.Android, "CPU");
                    if (string.Equals(cpu, "ARMv7", StringComparison.OrdinalIgnoreCase) ||
                        importer.assetPath.IndexOf("armeabi-v7a", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
                        importer.SaveAndReimport();
                        modified = true;
                    }
                }
            }

            return modified;
        }

        private static void RemoveBundledNebiusKeyFromResources()
        {
            try
            {
                string resKey = Path.Combine(Application.dataPath, "Resources", "nebius_key.txt");
                if (File.Exists(resKey))
                {
                    AssetDatabase.DeleteAsset("Assets/Resources/nebius_key.txt");
                }
            }
            catch { }
        }

        private static bool CleanDontSaveShaders(UnityEngine.Object assetObj)
        {
            SerializedObject so = new SerializedObject(assetObj);
            SerializedProperty prop = so.FindProperty("m_AlwaysIncludedShaders");
            if (prop == null || !prop.isArray) return false;

            bool modified = false;
            for (int i = prop.arraySize - 1; i >= 0; i--)
            {
                UnityEngine.Object elem = prop.GetArrayElementAtIndex(i).objectReferenceValue;
                if (elem == null ||
                    (elem.hideFlags & HideFlags.DontSave) != 0 ||
                    elem.name == "GUI/Text Shader" ||
                    AssetDatabase.GetAssetPath(elem) == "Library/unity default resources")
                {
                    prop.DeleteArrayElementAtIndex(i);
                    modified = true;
                }
            }

            if (modified)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(assetObj);
            }
            return modified;
        }
    }

    /// <summary>
    /// Ensures PlayerSettings & Meta Quest Build Profile package ID and ARM64 architecture are verified before every Android build.
    /// </summary>
    public class KineticCadPreprocessBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            KineticCadProjectTools.VerifyProjectSettings();
        }
    }

    /// <summary>
    /// Strips 32-bit armeabi-v7a libraries (both jniLibs and AAR-embedded assets/lib/armeabi-v7a) from the exported
    /// Gradle project so Meta Horizon Store sees a pure 64-bit (arm64-v8a) APK with the unique KineticCAD package ID.
    /// </summary>
    public class KineticCadGradle64BitStripper : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            if (string.IsNullOrEmpty(unityLibraryPath) || !Directory.Exists(unityLibraryPath))
                return;

            string gradleRoot = Directory.GetParent(unityLibraryPath)?.FullName ?? unityLibraryPath;
            string launcherDir = Path.Combine(gradleRoot, "launcher");

            // 1. Strip armeabi-v7a entries inside AAR dependencies copied to unityLibrary/libs (e.g. OVRPlugin.aar)
            string libsDir = Path.Combine(unityLibraryPath, "libs");
            if (Directory.Exists(libsDir))
            {
                foreach (string aarFile in Directory.GetFiles(libsDir, "*.aar", SearchOption.AllDirectories))
                {
                    Strip32BitEntriesFromArchive(aarFile);
                }
            }

            // 2. Remove any staged or cached armeabi-v7a directories in the Gradle workspace
            DeleteAll32BitDirectories(gradleRoot);

            // 3. Configure AGP 9+ build.gradle rules cleanly in both launcher and unityLibrary
            PatchGradleFile(Path.Combine(unityLibraryPath, "build.gradle"));
            PatchGradleFile(Path.Combine(launcherDir, "build.gradle"));

            // 4. Safety net: replace any stale URP blank template package ID in generated manifests/gradle
            EnsureUniquePackageIdInGeneratedFiles(gradleRoot);
        }

        private static void Strip32BitEntriesFromArchive(string archivePath)
        {
            try
            {
                int removed = 0;
                using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
                {
                    List<ZipArchiveEntry> toDelete = new List<ZipArchiveEntry>();
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string norm = entry.FullName.Replace('\\', '/');
                        if (norm.IndexOf("armeabi-v7a/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            norm.EndsWith("armeabi-v7a", StringComparison.OrdinalIgnoreCase))
                        {
                            toDelete.Add(entry);
                        }
                    }

                    foreach (ZipArchiveEntry entry in toDelete)
                    {
                        entry.Delete();
                        removed++;
                    }
                }

                if (removed > 0)
                    Debug.Log($"[KineticCAD 64-Bit] Stripped {removed} 32-bit armeabi-v7a entries from {Path.GetFileName(archivePath)}.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KineticCAD 64-Bit] Could not strip 32-bit entries from {archivePath}: {ex.Message}");
            }
        }

        private static void DeleteAll32BitDirectories(string rootDir)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(rootDir, "armeabi-v7a", SearchOption.AllDirectories))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void PatchGradleFile(string gradleFilePath)
        {
            if (!File.Exists(gradleFilePath))
                return;

            string content = File.ReadAllText(gradleFilePath);
            string updated = content;

            // Ensure AAPT2 ignores any armeabi-v7a directories in merged assets
            const string defaultIgnoreMarker = "!*~:!*armeabi-v7a*";
            if (!updated.Contains("!*armeabi-v7a*") && updated.Contains("!*~\""))
            {
                updated = updated.Replace("!*~\"", defaultIgnoreMarker + "\"");
            }

            // Ensure AGP 8/9 packaging.jniLibs excludes any 32-bit native libraries
            if (!updated.Contains("**/armeabi-v7a/**"))
            {
                const string jniMarker = "jniLibs {\n";
                const string jniMarkerCrlf = "jniLibs {\r\n";
                if (updated.Contains(jniMarkerCrlf))
                {
                    updated = updated.Replace(jniMarkerCrlf, jniMarkerCrlf + "            excludes += ['**/armeabi-v7a/**']\r\n");
                }
                else if (updated.Contains(jniMarker))
                {
                    updated = updated.Replace(jniMarker, jniMarker + "            excludes += ['**/armeabi-v7a/**']\n");
                }
                else
                {
                    updated += "\nandroid {\n    packaging {\n        jniLibs {\n            excludes += ['**/armeabi-v7a/**']\n        }\n    }\n}\n";
                }
            }

            if (updated != content)
                File.WriteAllText(gradleFilePath, updated);
        }

        private static void EnsureUniquePackageIdInGeneratedFiles(string gradleRoot)
        {
            string[] candidates =
            {
                Path.Combine(gradleRoot, "launcher", "build.gradle"),
                Path.Combine(gradleRoot, "launcher", "src", "main", "AndroidManifest.xml"),
                Path.Combine(gradleRoot, "unityLibrary", "src", "main", "AndroidManifest.xml")
            };

            foreach (string file in candidates)
            {
                if (!File.Exists(file))
                    continue;

                string text = File.ReadAllText(file);
                if (text.Contains(KineticCadProjectTools.LegacyTemplatePackageId))
                {
                    text = text.Replace(KineticCadProjectTools.LegacyTemplatePackageId, KineticCadProjectTools.TargetPackageId);
                    File.WriteAllText(file, text);
                    Debug.Log($"[KineticCAD 64-Bit] Replaced legacy URP template ID in {Path.GetFileName(file)} with {KineticCadProjectTools.TargetPackageId}.");
                }
            }
        }
    }
}
#endif
