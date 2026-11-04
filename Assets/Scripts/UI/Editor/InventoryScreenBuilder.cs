using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Combat.Loadout;
using Combat.Weapons;

namespace Combat.UI.EditorTools
{
    // Creates the inventory screen once with a default layout. After that the layout is yours; this only selects it.
    public static class InventoryScreenBuilder
    {
        private const string AssetFolder = "Assets/GameData/UI";

        [MenuItem("Tools/UI/Build Inventory Screen")]
        private static void Build()
        {
            var existing = Object.FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[InventoryBuilder] An InventoryScreen already exists; selected it. Delete it first to rebuild the default layout.");
                return;
            }

            var style = FindOrCreate<UIStyleSO>("UIStyle");
            var keys = FindFirst<WeaponStatKeys>();
            var sheet = FindOrCreateSheet(keys);
            var start = FindFirst<StartingLoadoutSO>();

            int ui = LayerMask.NameToLayer("UI");
            var rootGo = new GameObject("InventoryScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (ui >= 0) rootGo.layer = ui;
            Undo.RegisterCreatedObjectUndo(rootGo, "Build Inventory Screen");

            var canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var content = Rect(rootGo.transform, "Content", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var backdrop = content.gameObject.AddComponent<Image>();
            backdrop.color = style.backdrop;

            var header = Rect(content, "Header", new Vector2(0f, 1f), Vector2.one, new Vector2(72f, -118f), new Vector2(-72f, -36f));
            var loadoutTab = Rect(content, "LoadoutTab", Vector2.zero, Vector2.one, new Vector2(72f, 96f), new Vector2(-72f, -140f));
            var characterTab = Rect(content, "CharacterTab", Vector2.zero, Vector2.one, new Vector2(72f, 96f), new Vector2(-72f, -140f));
            var footer = Rect(content, "Footer", Vector2.zero, new Vector2(1f, 0f), new Vector2(72f, 28f), new Vector2(-72f, 76f));

            var slots = Rect(loadoutTab, "Slots", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(470f, 0f));
            var carried = Rect(loadoutTab, "Carried", Vector2.zero, new Vector2(0f, 1f), new Vector2(494f, 0f), new Vector2(912f, 0f));
            var detail = Rect(loadoutTab, "Detail", Vector2.zero, Vector2.one, new Vector2(936f, 0f), Vector2.zero);

            var screen = rootGo.AddComponent<InventoryScreen>();
            var so = new SerializedObject(screen);
            so.FindProperty("style").objectReferenceValue = style;
            so.FindProperty("weaponStatSheet").objectReferenceValue = sheet;
            so.FindProperty("statKeys").objectReferenceValue = keys;
            so.FindProperty("content").objectReferenceValue = content.gameObject;
            so.FindProperty("backdrop").objectReferenceValue = backdrop;
            so.FindProperty("loadoutTab").objectReferenceValue = loadoutTab.gameObject;
            so.FindProperty("characterTab").objectReferenceValue = characterTab.gameObject;
            so.FindProperty("header").objectReferenceValue = header.gameObject.AddComponent<InventoryHeader>();
            so.FindProperty("slotsPanel").objectReferenceValue = slots.gameObject.AddComponent<LoadoutSlotsPanel>();
            so.FindProperty("carriedPanel").objectReferenceValue = carried.gameObject.AddComponent<CarriedPanel>();
            so.FindProperty("detailPanel").objectReferenceValue = detail.gameObject.AddComponent<ItemDetailPanel>();
            so.FindProperty("footer").objectReferenceValue = footer.gameObject.AddComponent<InventoryFooter>();
            so.FindProperty("previewLoadout").objectReferenceValue = start;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(rootGo.scene);
            Selection.activeObject = rootGo;
            Debug.Log("[InventoryBuilder] Built the inventory screen. Arrange the panel rects freely; styling lives on " +
                      AssetDatabase.GetAssetPath(style) + ".");
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        private static T FindFirst<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        private static T FindOrCreate<T>(string fileName) where T : ScriptableObject
        {
            var found = FindFirst<T>();
            if (found != null) return found;
            EnsureFolder();
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, AssetFolder + "/" + fileName + ".asset");
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static StatSheetSO FindOrCreateSheet(WeaponStatKeys keys)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:StatSheetSO"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "WeaponStatSheet") return AssetDatabase.LoadAssetAtPath<StatSheetSO>(path);
            }

            EnsureFolder();
            var sheet = ScriptableObject.CreateInstance<StatSheetSO>();
            if (keys != null)
            {
                var g = new StatSheetSO.Group();
                Add(g, keys.weaponDamage, "Damage", StatDisplay.Number);
                Add(g, keys.rpm, "Rounds per minute", StatDisplay.Number);
                Add(g, keys.range, "Range", StatDisplay.Bar);
                Add(g, keys.aimAssist, "Aim assist", StatDisplay.Bar);
                Add(g, keys.handling, "Handling", StatDisplay.Bar);
                Add(g, keys.magazineSize, "Magazine", StatDisplay.Number);
                Add(g, keys.reloadTime, "Reload time", StatDisplay.Number, lower: true, unit: "s", decimals: 2);
                Add(g, keys.equipTime, "Equip time", StatDisplay.Number, lower: true, unit: "s", decimals: 2);
                Add(g, keys.adsZoom, "Zoom", StatDisplay.Number, unit: "x", decimals: 2);
                sheet.groups.Add(g);
            }
            AssetDatabase.CreateAsset(sheet, AssetFolder + "/WeaponStatSheet.asset");
            AssetDatabase.SaveAssets();
            return sheet;
        }

        private static void Add(StatSheetSO.Group g, Combat.Stats.StatDefinitionSO stat, string label, StatDisplay display,
                                bool lower = false, string unit = "", int decimals = -1)
        {
            if (stat == null) return;
            g.entries.Add(new StatSheetSO.Entry { stat = stat, label = label, display = display, lowerIsBetter = lower, unit = unit, decimals = decimals });
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(AssetFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/GameData")) AssetDatabase.CreateFolder("Assets", "GameData");
            AssetDatabase.CreateFolder("Assets/GameData", "UI");
        }
    }
}
