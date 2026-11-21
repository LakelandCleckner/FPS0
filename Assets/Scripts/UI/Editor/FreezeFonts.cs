using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Combat.UI.EditorTools
{
    // Bakes Extended ASCII into every dynamic TextMeshPro font and makes it static, so play sessions stop rewriting font assets.
    public static class FreezeFonts
    {
        private const string FontDir = "Assets/TextMesh Pro/Fonts";
        private const string Tag = "[FreezeFonts] ";

        [MenuItem("FPS0/UI/Freeze UI Fonts", priority = 710)]
        public static void Run()
        {
            string chars = Charset();
            int frozen = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null || font.atlasPopulationMode == AtlasPopulationMode.Static) continue;
                if (font.sourceFontFile == null) { Debug.LogWarning(Tag + "No source font on " + path + "; left dynamic."); continue; }

                font.TryAddCharacters(chars, out string missing);
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                EditorUtility.SetDirty(font);
                if (font.atlasTextures != null)
                    foreach (var tex in font.atlasTextures) if (tex != null) EditorUtility.SetDirty(tex);
                if (font.material != null) EditorUtility.SetDirty(font.material);
                frozen++;

                int miss = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
                Debug.Log(Tag + "Static: " + path + (miss > 0 ? " (" + miss + " characters not in the font or atlas full)" : ""));
            }
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + (frozen > 0 ? "Done, " + frozen + " fonts made static." : "No dynamic fonts left."));
        }

        // Printable ASCII, Latin-1 and common punctuation (dashes, curly quotes, bullet, ellipsis).
        private static string Charset()
        {
            var sb = new StringBuilder();
            for (int c = 32; c <= 126; c++) sb.Append((char)c);
            for (int c = 160; c <= 255; c++) sb.Append((char)c);
            sb.Append("\u2013\u2014\u2018\u2019\u201C\u201D\u2022\u2026");
            return sb.ToString();
        }
    }
}
