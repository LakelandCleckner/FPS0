using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Combat.UI.EditorTools
{
    // Adds EnemyHealthBars to the HUD canvas as a full-screen layer. Safe to run again.
    public static class EnemyHealthBarsSetup
    {
        [MenuItem("Tools/UI/Add Enemy Health Bars")]
        private static void Add()
        {
            var existing = Object.FindFirstObjectByType<EnemyHealthBars>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[EnemyHealthBars] Already in the scene; nothing added.");
                return;
            }

            var canvas = HudCanvas();
            if (canvas == null)
            {
                Debug.LogWarning("[EnemyHealthBars] No screen-space canvas found. Open the scene with the HUD and try again.");
                return;
            }

            var go = new GameObject("EnemyHealthBars", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(go, "Add Enemy Health Bars");
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();

            var bars = go.AddComponent<EnemyHealthBars>();
            var so = new SerializedObject(bars);
            so.FindProperty("style").objectReferenceValue = FindStyle();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeObject = go;
            Debug.Log("[EnemyHealthBars] Added to " + canvas.name + ", behind the other HUD elements.");
        }

        // The HUD: the ammo readout's canvas, else the first screen-space canvas.
        private static Canvas HudCanvas()
        {
            var ammo = Object.FindFirstObjectByType<Combat.Weapons.AmmoDisplay>(FindObjectsInactive.Include);
            var c = ammo != null ? ammo.GetComponentInParent<Canvas>(true) : null;
            if (c != null) return c.rootCanvas;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace) return canvas;
            return null;
        }

        private static UIStyleSO FindStyle()
        {
            var guids = AssetDatabase.FindAssets("t:UIStyleSO");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<UIStyleSO>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
