using UnityEngine;
using UnityEngine.Scripting;

namespace KineticCore.Runtime
{
    /// <summary>
    /// Creates world-space 3D text (TextMesh) that renders crisply in both Quest 3 Single-Pass Instanced VR
    /// and the flat Unity Editor, using our bundled Assets/Resources/KineticWorldText.shader.
    /// </summary>
    [Preserve]
    public static class WorldTextFactory
    {
        private static Font _sharedFont;
        private static Material _sharedTextMaterial;

        public static Font SharedFont
        {
            get
            {
                if (_sharedFont != null) return _sharedFont;
                _sharedFont = Font.CreateDynamicFontFromOSFont("SansSerif", 48);
                if (_sharedFont == null)
                {
                    try { _sharedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                    catch { _sharedFont = null; }
                }
                return _sharedFont;
            }
        }

        private static Material GetSharedTextMaterial(Font font)
        {
            if (font == null) return null;
            if (_sharedTextMaterial != null)
            {
                if (font.material != null && font.material.mainTexture != null)
                    _sharedTextMaterial.mainTexture = font.material.mainTexture;
                return _sharedTextMaterial;
            }

            Shader customShader = Resources.Load<Shader>("KineticWorldText");
            if (customShader == null)
                customShader = Shader.Find("KineticCore/WorldText");

            if (customShader != null && font.material != null)
            {
                _sharedTextMaterial = new Material(customShader);
                _sharedTextMaterial.name = "KineticWorldText_RuntimeMat";
                _sharedTextMaterial.enableInstancing = false;
                _sharedTextMaterial.mainTexture = font.material.mainTexture;
                font.material = _sharedTextMaterial;
                return _sharedTextMaterial;
            }

            return font.material;
        }

        public static TextMesh Create(string name, Transform parent, Vector3 localPos, Quaternion localRot,
                                      float worldScale, int fontSize, Color color, string text,
                                      TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one * worldScale;

            TextMesh tm = go.AddComponent<TextMesh>();
            Font f = SharedFont;
            if (f != null)
            {
                tm.font = f;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                Material mat = GetSharedTextMaterial(f);
                if (mr != null && mat != null)
                {
                    mr.sharedMaterial = mat;
                }
            }
            tm.fontSize = fontSize;
            tm.characterSize = 1.0f;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            return tm;
        }
    }
}
