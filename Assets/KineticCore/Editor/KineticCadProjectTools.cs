#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KineticCore.Editor
{
    [InitializeOnLoad]
    public static class KineticCadProjectTools
    {
        static KineticCadProjectTools()
        {
            EditorApplication.delayCall += VerifyProjectSettings;
        }

        [MenuItem("KineticCAD/Verify Project & Shader Settings")]
        public static void VerifyProjectSettings()
        {
            // Never mutate PlayerSettings or re-import assets while Unity is compiling or building a Player!
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            bool dirty = false;

            SyncNebiusKeyToResources();
            if (!PlayerSettings.Android.forceInternetPermission)
            {
                PlayerSettings.Android.forceInternetPermission = true;
                dirty = true;
            }

            // 1. Remove any HideFlags.DontSave shaders (e.g. GUI/Text Shader from unity default resources)
            //    from in-memory GraphicsSettings.asset and Meta Quest.asset so Android Player builds never fail!
            Object[] graphicsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsAssets != null)
            {
                foreach (Object obj in graphicsAssets)
                {
                    if (obj != null)
                        dirty |= CleanDontSaveShaders(obj);
                }
            }

            Object[] profileAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/Build Profiles/Meta Quest.asset");
            if (profileAssets != null)
            {
                foreach (Object obj in profileAssets)
                {
                    if (obj != null)
                        dirty |= CleanDontSaveShaders(obj);
                }
            }

            // 2. Ensure OculusProjectConfig.asset has insightPassthroughEnabled = true in memory
            Object ovrCfg = AssetDatabase.LoadMainAssetAtPath("Assets/Oculus/OculusProjectConfig.asset");
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
            Object[] qualityAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
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
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[KineticCAD Editor] Verified URP Pipeline, Shaders, and Quest 3 Passthrough Config.");
            }
        }

        private static void SyncNebiusKeyToResources()
        {
            try
            {
                DirectoryInfo root = Directory.GetParent(Application.dataPath);
                if (root == null) return;
                string rootKey = Path.Combine(root.FullName, "nebius_key.txt");
                string resKey = Path.Combine(Application.dataPath, "Resources", "nebius_key.txt");
                if (File.Exists(rootKey))
                {
                    string keyText = File.ReadAllText(rootKey).Trim();
                    if (!string.IsNullOrEmpty(keyText))
                    {
                        bool needsWrite = !File.Exists(resKey) || File.ReadAllText(resKey).Trim() != keyText;
                        if (needsWrite)
                        {
                            File.WriteAllText(resKey, keyText);
                            AssetDatabase.ImportAsset("Assets/Resources/nebius_key.txt");
                        }
                    }
                }
            }
            catch { }
        }

        private static bool CleanDontSaveShaders(Object assetObj)
        {
            SerializedObject so = new SerializedObject(assetObj);
            SerializedProperty prop = so.FindProperty("m_AlwaysIncludedShaders");
            if (prop == null || !prop.isArray) return false;

            bool modified = false;
            for (int i = prop.arraySize - 1; i >= 0; i--)
            {
                Object elem = prop.GetArrayElementAtIndex(i).objectReferenceValue;
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
}
#endif
