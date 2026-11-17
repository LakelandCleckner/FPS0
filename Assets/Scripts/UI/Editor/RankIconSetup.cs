using UnityEditor;
using UnityEngine;

namespace Combat.UI.EditorTools
{
    // Imports the rank icons as sprites and assigns them to every UI style.
    public static class RankIconSetup
    {
        private const string Folder = "Assets/Art/UI/RankIcons/";
        private static readonly string[] Ranks = { "Minor", "Major", "Elite", "Miniboss", "Boss" };

        [MenuItem("FPS0/Art/Setup Rank Icons")]
        private static void Setup()
        {
            var sprites = new Sprite[Ranks.Length];
            for (int i = 0; i < Ranks.Length; i++)
            {
                string path = Folder + "Rank_" + Ranks[i] + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { Debug.LogWarning("[RankIcons] Missing " + path); continue; }
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            int styles = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UIStyleSO"))
            {
                var style = AssetDatabase.LoadAssetAtPath<UIStyleSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (style == null) continue;
                Undo.RecordObject(style, "Setup Rank Icons");
                style.rankIconMinor = sprites[0];
                style.rankIconMajor = sprites[1];
                style.rankIconElite = sprites[2];
                style.rankIconMiniboss = sprites[3];
                style.rankIconBoss = sprites[4];
                EditorUtility.SetDirty(style);
                styles++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[RankIcons] Assigned rank icons to " + styles + " UI style(s).");
        }
    }
}
