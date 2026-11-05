using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Combat.Core;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.UI.EditorTools
{
    // Creates the inventory screen with a default layout, or adds only the parts an existing one is missing. Never moves existing layout.
    public static class InventoryScreenBuilder
    {
        private const string AssetFolder = "Assets/GameData/UI";

        [MenuItem("Tools/UI/Build Inventory Screen")]
        private static void Build()
        {
            var existing = Object.FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);
            if (existing != null)
            {
                AddMissing(existing);
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
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
            AddMissing(screen);

            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(rootGo.scene);
            Selection.activeObject = rootGo;
            Debug.Log("[InventoryBuilder] Built the inventory screen. Arrange the panel rects freely; styling lives on " +
                      AssetDatabase.GetAssetPath(style) + ".");
        }

        // Adds the Character tab parts and their assets if absent; existing objects and layout are left alone.
        private static void AddMissing(InventoryScreen screen)
        {
            var added = new List<string>();
            var so = new SerializedObject(screen);
            var content = so.FindProperty("content").objectReferenceValue as GameObject;
            var contentT = content != null ? content.transform : screen.transform;

            var tabProp = so.FindProperty("characterTab");
            var tab = tabProp.objectReferenceValue as GameObject;
            if (tab == null)
            {
                tab = Rect(contentT, "CharacterTab", Vector2.zero, Vector2.one, new Vector2(72f, 96f), new Vector2(-72f, -140f)).gameObject;
                Undo.RegisterCreatedObjectUndo(tab, "Add Character Tab");
                tabProp.objectReferenceValue = tab;
                added.Add("CharacterTab");
            }

            var panelProp = so.FindProperty("characterPanel");
            if (panelProp.objectReferenceValue == null)
            {
                var panel = tab.GetComponent<CharacterPanel>();
                if (panel == null) panel = Undo.AddComponent<CharacterPanel>(tab);
                panelProp.objectReferenceValue = panel;
                added.Add("CharacterPanel");
            }

            var tipProp = so.FindProperty("tooltip");
            if (tipProp.objectReferenceValue == null)
            {
                var tip = contentT.GetComponentInChildren<StatTooltip>(true);
                if (tip == null)
                {
                    var rt = Rect(contentT, "StatTooltip", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    rt.SetAsLastSibling();
                    tip = rt.gameObject.AddComponent<StatTooltip>();
                    Undo.RegisterCreatedObjectUndo(rt.gameObject, "Add Stat Tooltip");
                }
                tipProp.objectReferenceValue = tip;
                added.Add("StatTooltip");
            }

            AddVault(so, contentT, added);

            var sheetProp = so.FindProperty("characterStatSheet");
            if (sheetProp.objectReferenceValue == null)
            {
                sheetProp.objectReferenceValue = FindOrCreateCharacterSheet();
                added.Add("CharacterStatSheet");
            }

            so.ApplyModifiedProperties();

            if (added.Count == 0) Debug.Log("[InventoryBuilder] Inventory screen is up to date; nothing added.");
            else
            {
                EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
                Debug.Log("[InventoryBuilder] Added: " + string.Join(", ", added) + ".");
            }
        }

        // Vault tab: filter bar across the top, carried grid left, vault grid middle, detail right.
        private static void AddVault(SerializedObject so, Transform contentT, List<string> added)
        {
            var tabProp = so.FindProperty("vaultTab");
            var tab = tabProp.objectReferenceValue as GameObject;
            if (tab == null)
            {
                tab = Rect(contentT, "VaultTab", Vector2.zero, Vector2.one, new Vector2(72f, 96f), new Vector2(-72f, -140f)).gameObject;
                tab.SetActive(false);
                Undo.RegisterCreatedObjectUndo(tab, "Add Vault Tab");
                tabProp.objectReferenceValue = tab;
                added.Add("VaultTab");
            }

            const float bar = 158f;
            var t = tab.transform;
            Part<VaultFilterBar>(so, "filterBar", t, "Filters", new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -bar), Vector2.zero, added);
            var carried = Part<ItemGridPanel>(so, "vaultCarriedPanel", t, "Carried", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(440f, -bar - 16f), added);
            var vault = Part<ItemGridPanel>(so, "vaultPanel", t, "Vault", Vector2.zero, Vector2.one, new Vector2(464f, 0f), new Vector2(-624f, -bar - 16f), added);
            Part<ItemDetailPanel>(so, "vaultDetailPanel", t, "Detail", new Vector2(1f, 0f), Vector2.one, new Vector2(-600f, 0f), new Vector2(0f, -bar - 16f), added);

            SetMode(carried, ItemGridPanel.Mode.Carried);
            SetMode(vault, ItemGridPanel.Mode.Vault);
        }

        // The panel on the screen's field, else a new rect under the tab with that component.
        private static T Part<T>(SerializedObject so, string field, Transform parent, string name,
                                 Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, List<string> added) where T : Component
        {
            var prop = so.FindProperty(field);
            if (prop.objectReferenceValue is T existing) return existing;

            var rt = Rect(parent, name, min, max, offsetMin, offsetMax);
            Undo.RegisterCreatedObjectUndo(rt.gameObject, "Add " + name);
            var c = rt.gameObject.AddComponent<T>();
            prop.objectReferenceValue = c;
            added.Add("Vault " + name);
            return c;
        }

        private static void SetMode(ItemGridPanel panel, ItemGridPanel.Mode mode)
        {
            if (panel == null) return;
            var pso = new SerializedObject(panel);
            var m = pso.FindProperty("mode");
            if (m.enumValueIndex == (int)mode) return;
            m.enumValueIndex = (int)mode;
            pso.ApplyModifiedProperties();
        }

        private static StatSheetSO FindOrCreateCharacterSheet()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:StatSheetSO"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "CharacterStatSheet") return AssetDatabase.LoadAssetAtPath<StatSheetSO>(path);
            }

            EnsureFolder();
            var sheet = ScriptableObject.CreateInstance<StatSheetSO>();
            var defense = FindFirst<DefenseStatKeys>();
            var combat = FindFirst<CombatStatKeys>();
            var move = FindFirst<MovementStatKeys>();

            var surv = new StatSheetSO.Group { label = "Survivability" };
            Add(surv, FindStat("max_health"), "Max health", StatDisplay.Number);
            Add(surv, defense != null ? defense.damageTaken : null, "Damage taken", StatDisplay.Number, lower: true, format: StatValueFormat.Both);
            foreach (var type in DamageTypes())
                Add(surv, type.resistanceStat, (string.IsNullOrWhiteSpace(type.displayName) ? type.name : type.displayName) + " resistance", StatDisplay.Number, format: StatValueFormat.Bonus);

            var off = new StatSheetSO.Group { label = "Offense" };
            if (combat != null)
            {
                Add(off, combat.critChance, "Crit chance", StatDisplay.Number, decimals: 2);
                Add(off, combat.critDamage, "Crit damage", StatDisplay.Number, decimals: 2, format: StatValueFormat.Both);
            }

            var mov = new StatSheetSO.Group { label = "Movement" };
            if (move != null)
            {
                Add(mov, move.moveSpeed, "Move speed", StatDisplay.Number, decimals: 1);
                Add(mov, move.sprintMultiplier, "Sprint multiplier", StatDisplay.Number, unit: "x", decimals: 2);
                Add(mov, move.jumpForce, "Jump force", StatDisplay.Number, decimals: 1);
            }

            foreach (var g in new[] { surv, off, mov }) if (g.entries.Count > 0) sheet.groups.Add(g);

            AssetDatabase.CreateAsset(sheet, AssetFolder + "/CharacterStatSheet.asset");
            AssetDatabase.SaveAssets();
            return sheet;
        }

        // Damage types with a resistance stat, one per stat, by name.
        private static List<DamageTypeSO> DamageTypes()
        {
            var list = new List<DamageTypeSO>();
            var seen = new HashSet<StatDefinitionSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:DamageTypeSO"))
            {
                var t = AssetDatabase.LoadAssetAtPath<DamageTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null && t.resistanceStat != null && seen.Add(t.resistanceStat)) list.Add(t);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private static StatDefinitionSO FindStat(string id)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:StatDefinitionSO"))
            {
                var s = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.id == id) return s;
            }
            return null;
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

        private static void Add(StatSheetSO.Group g, StatDefinitionSO stat, string label, StatDisplay display,
                                bool lower = false, string unit = "", int decimals = -1, StatValueFormat format = StatValueFormat.Plain)
        {
            if (stat == null) return;
            g.entries.Add(new StatSheetSO.Entry { stat = stat, label = label, display = display, lowerIsBetter = lower, unit = unit, decimals = decimals, format = format });
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(AssetFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/GameData")) AssetDatabase.CreateFolder("Assets", "GameData");
            AssetDatabase.CreateFolder("Assets/GameData", "UI");
        }
    }
}
