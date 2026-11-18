using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace Combat.UI.EditorTools
{
    // Builds the inline text sprite asset for damage type icons from the atlas strip and assigns it to every UI style.
    // Runs on its own after a script reload when the asset is missing; the menu item rebuilds it on demand.
    [InitializeOnLoad]
    public static class DamageTypeIconSetup
    {
        static DamageTypeIconSetup()
        {
            EditorApplication.delayCall += BuildIfMissing;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) BuildIfMissing(); };
        }

        private static void BuildIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AssetPath) != null) return;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath) == null) return;
            Build();
        }

        private const string Folder = "Assets/Art/UI/DamageTypeIcons/";
        private const string AtlasPath = Folder + "DamageTypeIcons_Atlas.png";
        private const string AssetPath = Folder + "DamageTypeIcons.asset";
        // Atlas order, left to right; names match each damage type's Inline Sprite
        private static readonly string[] Names = { "Ballistic", "Fire", "Frost", "Shock", "Toxic" };
        private const int Cell = 128;

        [MenuItem("FPS0/Art/Build Damage Type Icon Sprites")]
        private static void Build()
        {
            try { BuildAsset(); }
            catch (System.Exception e) { Debug.LogError("[DamageTypeIcons] Build failed: " + e); }
        }

        private static void BuildAsset()
        {
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) { Debug.LogWarning("[DamageTypeIcons] Missing " + AtlasPath); return; }
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null) { Debug.LogError("[DamageTypeIcons] Atlas did not load: " + AtlasPath); return; }

            // Rebuild from scratch each run
            if (AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AssetPath) != null) AssetDatabase.DeleteAsset(AssetPath);

            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = "DamageTypeIcons";
            asset.spriteSheet = atlas;
            var shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null && TMP_Settings.defaultSpriteAsset != null && TMP_Settings.defaultSpriteAsset.material != null)
                shader = TMP_Settings.defaultSpriteAsset.material.shader;
            if (shader == null) { Debug.LogError("[DamageTypeIcons] TextMeshPro/Sprite shader not found. Import TMP Essential Resources."); return; }
            var mat = new Material(shader) { name = "DamageTypeIcons Material" };
            mat.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            asset.material = mat;
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.AddObjectToAsset(mat, asset);

            // Mark current so TMP doesn't try to upgrade it from the legacy sprite list
            var so = new SerializedObject(asset);
            var version = so.FindProperty("m_Version");
            if (version != null) { version.stringValue = "1.1.0"; so.ApplyModifiedPropertiesWithoutUndo(); }

            for (int i = 0; i < Names.Length; i++)
            {
                var rect = new GlyphRect(i * Cell, 0, Cell, Cell);
                var metrics = new GlyphMetrics(Cell, Cell, 0f, Cell * 0.8f, Cell);
                var glyph = new TMP_SpriteGlyph((uint)i, metrics, rect, 1f, 0);
                asset.spriteGlyphTable.Add(glyph);
                var character = new TMP_SpriteCharacter(0xFFFE, glyph) { name = Names[i], scale = 1f };
                asset.spriteCharacterTable.Add(character);
            }
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(asset);

            int styles = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UIStyleSO"))
            {
                var style = AssetDatabase.LoadAssetAtPath<UIStyleSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (style == null) continue;
                Undo.RecordObject(style, "Build Damage Type Icon Sprites");
                style.inlineIcons = asset;
                EditorUtility.SetDirty(style);
                styles++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[DamageTypeIcons] Built " + Names.Length + " inline icons and assigned them to " + styles + " UI style(s).");
        }
    }
}
