using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Enemies;
using Combat.Info;
using Combat.Perks;
using Combat.Status;
using Combat.UI;

// Generated player-facing text under the Inspector, so the wording can be checked while tuning.
public static class DescriptionPreview
{
    private static GUIStyle style;
    private static KeywordLibrarySO library;

    public static void Draw(Object target)
    {
        if (!(target is IDescribable source)) return;
        bool dark = EditorGUIUtility.isProSkin;
        var ctx = new DescribeContext
        {
            Keywords = Library(),
            Number = dark ? new Color(1f, 0.85f, 0.55f) : new Color(0.45f, 0.25f, 0f),
            Good = dark ? new Color(0.5f, 0.86f, 0.58f) : new Color(0.1f, 0.45f, 0.15f),
            Bad = dark ? new Color(0.96f, 0.44f, 0.4f) : new Color(0.65f, 0.1f, 0.1f)
        };
        string text = Describer.Build(source, ctx);
        text = Regex.Replace(text, "</?link[^>]*>|</?u>", "");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Description preview", EditorStyles.boldLabel);
        if (style == null) style = new GUIStyle(EditorStyles.helpBox) { richText = true, wordWrap = true, fontSize = 12, padding = new RectOffset(8, 8, 6, 6) };
        EditorGUILayout.LabelField(string.IsNullOrEmpty(text) ? "(nothing generated)" : text, style);
        if (ctx.Keywords == null) EditorGUILayout.HelpBox("No Keyword Library found: keywords show as plain words. FPS0/Info/Create Keywords makes one.", MessageType.None);
    }

    private static KeywordLibrarySO Library()
    {
        if (library != null) return library;
        var guids = AssetDatabase.FindAssets("t:KeywordLibrarySO");
        library = guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<KeywordLibrarySO>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        return library;
    }
}

[CustomEditor(typeof(PerkSO), true), CanEditMultipleObjects]
public class PerkInfoEditor : Editor
{
    public override void OnInspectorGUI() { DrawDefaultInspector(); DescriptionPreview.Draw(target); }
}

[CustomEditor(typeof(StatusSO), true), CanEditMultipleObjects]
public class StatusInfoEditor : Editor
{
    public override void OnInspectorGUI() { DrawDefaultInspector(); DescriptionPreview.Draw(target); }
}

[CustomEditor(typeof(HitEffectSO), true), CanEditMultipleObjects]
public class HitEffectInfoEditor : Editor
{
    public override void OnInspectorGUI() { DrawDefaultInspector(); DescriptionPreview.Draw(target); }
}

[CustomEditor(typeof(EnemyDeathEffectSO), true), CanEditMultipleObjects]
public class DeathEffectInfoEditor : Editor
{
    public override void OnInspectorGUI() { DrawDefaultInspector(); DescriptionPreview.Draw(target); }
}

[CustomEditor(typeof(EnemyShieldSO)), CanEditMultipleObjects]
public class ShieldInfoEditor : Editor
{
    public override void OnInspectorGUI() { DrawDefaultInspector(); DescriptionPreview.Draw(target); }
}

[CustomEditor(typeof(EnemyAffixSO), true), CanEditMultipleObjects]
public class AffixInfoEditor : Editor
{
    public override void OnInspectorGUI() { DrawDefaultInspector(); DescriptionPreview.Draw(target); }
}

// Makes the keyword library, built-in keywords, and a keyword for every element and status that lacks one.
public static class KeywordSetup
{
    private const string Folder = "Assets/GameData/Info";
    private const string KeywordFolder = Folder + "/Keywords";

    private static readonly (string id, string name, Color color, string rule)[] BuiltIn =
    {
        ("slow", "Slow", new Color(0.55f, 0.8f, 1f), "Slowed targets move slower. How much and for how long is shown where it's applied."),
        ("pool", "Pool", new Color(0.75f, 0.75f, 0.6f), "A patch on the ground that hits everyone standing in it at a steady rate until it fades."),
        ("explode", "Explosion", new Color(1f, 0.6f, 0.3f), "Hits everything inside the radius at once."),
        ("precision", "Precision", new Color(1f, 0.85f, 0.4f), "A hit on a weak point, usually the head. Deals extra damage."),
        ("shield", "Shield", new Color(0.45f, 0.8f, 1f), "Soaks damage before health and refills after a short break from damage. Hits of the shield's own element break it faster.")
    };

    [MenuItem("FPS0/Info/Create Keywords", priority = 720)]
    private static void Create()
    {
        Ensure("Assets/GameData", "Info");
        Ensure(Folder, "Keywords");
        var added = new List<string>();

        var library = AssetDatabase.LoadAssetAtPath<KeywordLibrarySO>(Folder + "/KeywordLibrary.asset");
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<KeywordLibrarySO>();
            AssetDatabase.CreateAsset(library, Folder + "/KeywordLibrary.asset");
            added.Add("KeywordLibrary");
        }

        foreach (var b in BuiltIn)
        {
            if (library.Find(b.id) != null) continue;
            var k = Make(b.id, b.name, b.color, b.rule);
            library.keywords.Add(k);
            added.Add(b.name);
        }
        EditorUtility.SetDirty(library);

        foreach (var guid in AssetDatabase.FindAssets("t:DamageTypeSO"))
        {
            var t = AssetDatabase.LoadAssetAtPath<DamageTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (t == null || t.keyword != null) continue;
            string n = string.IsNullOrWhiteSpace(t.displayName) ? t.name : t.displayName;
            t.keyword = Make("element_" + (string.IsNullOrEmpty(t.id) ? t.name.ToLowerInvariant() : t.id), n, t.color, "");
            EditorUtility.SetDirty(t);
            added.Add(n);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:StatusSO"))
        {
            var s = AssetDatabase.LoadAssetAtPath<StatusSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (s == null || s.keyword != null) continue;
            string n = Words(s.name.EndsWith("Status") ? s.name.Substring(0, s.name.Length - 6) : s.name);
            var c = s.damageType != null ? s.damageType.color : new Color(0.8f, 0.8f, 0.8f);
            s.keyword = Make("status_" + s.name.ToLowerInvariant(), n, c, "");
            EditorUtility.SetDirty(s);
            added.Add(n);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:UIStyleSO"))
        {
            var style = AssetDatabase.LoadAssetAtPath<UIStyleSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (style == null || style.keywords != null) continue;
            style.keywords = library;
            EditorUtility.SetDirty(style);
            added.Add("UIStyle keyword library");
        }

        AssetDatabase.SaveAssets();
        Selection.activeObject = library;
        Debug.Log(added.Count == 0 ? "[Keywords] Everything already has a keyword." : "[Keywords] Added: " + string.Join(", ", added) + ". Rename and recolour them in " + KeywordFolder + ".");
    }

    private static KeywordSO Make(string id, string displayName, Color color, string rule)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath(KeywordFolder + "/" + Safe(displayName) + ".asset");
        var k = ScriptableObject.CreateInstance<KeywordSO>();
        k.id = id;
        k.displayName = displayName;
        k.color = new Color(color.r, color.g, color.b, 1f);
        k.description = rule;
        AssetDatabase.CreateAsset(k, path);
        return k;
    }

    // "FireBurn" -> "Fire Burn".
    private static string Words(string s) => Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ").Trim();

    private static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    private static void Ensure(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
