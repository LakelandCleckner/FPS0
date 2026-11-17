using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Combat.Loadout;

namespace Combat.UI.EditorTools
{
    // Sets the loadout tab's column widths and the carried cap on the open scene. Detail takes the rest of the width.
    public class InventoryLayoutWindow : EditorWindow
    {
        public const float DefaultSlots = 460f;
        public const float DefaultCarried = 480f;
        public const float DefaultGap = 24f;
        public const int DefaultCap = 10;

        private float slots = DefaultSlots, carried = DefaultCarried, gap = DefaultGap;
        private int weaponCap = DefaultCap, armorCap = DefaultCap;

        [MenuItem("FPS0/UI/Inventory Layout", priority = 703)]
        private static void Open()
        {
            var w = GetWindow<InventoryLayoutWindow>("Inventory Layout");
            w.minSize = new Vector2(320f, 260f);
            w.ReadScene();
        }

        private void OnGUI()
        {
            var screen = FindScreen();
            if (screen == null)
            {
                EditorGUILayout.HelpBox("No inventory screen in the open scene.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Loadout tab columns", EditorStyles.boldLabel);
            slots = EditorGUILayout.FloatField("Slots width", slots);
            carried = EditorGUILayout.FloatField("Carried width", carried);
            gap = EditorGUILayout.FloatField("Gap", gap);
            float tabWidth = TabWidth(screen);
            if (tabWidth > 0f)
            {
                float detail = tabWidth - slots - carried - 2f * gap;
                EditorGUILayout.LabelField("Detail width", detail > 0f ? detail.ToString("0") : "none left");
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Carried cap per slot", EditorStyles.boldLabel);
            weaponCap = Mathf.Max(1, EditorGUILayout.IntField("Weapons", weaponCap));
            armorCap = Mathf.Max(1, EditorGUILayout.IntField("Armor", armorCap));

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Read From Scene")) ReadScene();
                if (GUILayout.Button("Defaults"))
                {
                    slots = DefaultSlots; carried = DefaultCarried; gap = DefaultGap;
                    weaponCap = armorCap = DefaultCap;
                }
                if (GUILayout.Button("Apply")) Apply(screen);
            }
        }

        // Left-anchored slots and carried columns; detail stretches to the right edge.
        public static void Place(RectTransform slotsRect, RectTransform carriedRect, RectTransform detailRect, float slots, float carried, float gap)
        {
            Set(slotsRect, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(slots, 0f));
            Set(carriedRect, Vector2.zero, new Vector2(0f, 1f), new Vector2(slots + gap, 0f), new Vector2(slots + gap + carried, 0f));
            Set(detailRect, Vector2.zero, Vector2.one, new Vector2(slots + gap + carried + gap, 0f), Vector2.zero);
        }

        private static void Set(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            if (rt == null) return;
            Undo.RecordObject(rt, "Inventory Layout");
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        private void Apply(InventoryScreen screen)
        {
            var so = new SerializedObject(screen);
            Place(RectOf(so, "slotsPanel"), RectOf(so, "carriedPanel"), RectOf(so, "detailPanel"), slots, carried, gap);

            foreach (var inv in Object.FindObjectsByType<PlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var iso = new SerializedObject(inv);
                iso.FindProperty("carriedWeaponCapPerSlot").intValue = weaponCap;
                iso.FindProperty("carriedArmorCapPerSlot").intValue = armorCap;
                iso.ApplyModifiedProperties();
            }

            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            screen.RequestRebuild();
        }

        private void ReadScene()
        {
            var screen = FindScreen();
            if (screen == null) return;
            var so = new SerializedObject(screen);
            var s = RectOf(so, "slotsPanel");
            var c = RectOf(so, "carriedPanel");
            if (s != null) slots = s.offsetMax.x - s.offsetMin.x;
            if (s != null && c != null)
            {
                gap = c.offsetMin.x - s.offsetMax.x;
                carried = c.offsetMax.x - c.offsetMin.x;
            }
            var inv = Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
            if (inv != null)
            {
                weaponCap = inv.CarriedWeaponCapPerSlot;
                armorCap = inv.CarriedArmorCapPerSlot;
            }
            Repaint();
        }

        private static float TabWidth(InventoryScreen screen)
        {
            var tab = new SerializedObject(screen).FindProperty("loadoutTab").objectReferenceValue as GameObject;
            return tab != null ? ((RectTransform)tab.transform).rect.width : 0f;
        }

        private static RectTransform RectOf(SerializedObject so, string field)
        {
            var c = so.FindProperty(field).objectReferenceValue as Component;
            return c != null ? (RectTransform)c.transform : null;
        }

        private static InventoryScreen FindScreen() => Object.FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);
    }
}
