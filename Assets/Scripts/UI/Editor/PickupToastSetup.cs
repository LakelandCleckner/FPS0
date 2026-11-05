using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Combat.Weapons;

namespace Combat.UI.EditorTools
{
    // Adds a PickupToast to the HUD canvas, just above the ammo readout. Safe to run again.
    public static class PickupToastSetup
    {
        private const float Width = 420f;
        private const float Height = 5 * 60f;
        private const float Gap = 16f;

        [MenuItem("Tools/UI/Add Pickup Toast")]
        private static void Add()
        {
            var existing = Object.FindFirstObjectByType<PickupToast>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[PickupToast] Already in the scene; nothing added.");
                return;
            }

            var ammo = Object.FindFirstObjectByType<AmmoDisplay>(FindObjectsInactive.Include);
            var ammoRect = AmmoRect(ammo);
            var canvas = ammoRect != null ? ammoRect.GetComponentInParent<Canvas>(true) : null;
            if (canvas != null) canvas = canvas.rootCanvas;
            if (canvas == null)
            {
                Debug.LogWarning("[PickupToast] No AmmoDisplay on a canvas found. Open the scene with the HUD and try again.");
                return;
            }

            var canvasRt = (RectTransform)canvas.transform;
            var go = new GameObject("PickupToast", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(go, "Add Pickup Toast");
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRt, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(Width, Height);
            rt.anchoredPosition = AboveAmmo(canvasRt, ammoRect);

            var toast = go.AddComponent<PickupToast>();
            var so = new SerializedObject(toast);
            so.FindProperty("style").objectReferenceValue = FindStyle();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeObject = go;
            Debug.Log("[PickupToast] Added above the ammo readout on " + canvas.name + ". Move it freely; rows stack up from its bottom-right.");
        }

        // Bottom-right offset that puts the toast's bottom edge just above the ammo rect, right edges aligned.
        private static Vector2 AboveAmmo(RectTransform canvas, RectTransform ammo)
        {
            if (ammo == null) return new Vector2(-48f, 160f);
            var corners = new Vector3[4];
            ammo.GetWorldCorners(corners);
            Vector2 topRight = canvas.InverseTransformPoint(corners[2]);
            var r = canvas.rect;
            return new Vector2(topRight.x - r.xMax, topRight.y - r.yMin + Gap);
        }

        // The readout's label rect, else the AmmoDisplay's own if it sits on the canvas.
        private static RectTransform AmmoRect(AmmoDisplay ammo)
        {
            if (ammo == null) return null;
            var label = new SerializedObject(ammo).FindProperty("label").objectReferenceValue as Component;
            if (label != null && label.transform is RectTransform lr) return lr;
            return ammo.transform as RectTransform;
        }

        private static UIStyleSO FindStyle()
        {
            var guids = AssetDatabase.FindAssets("t:UIStyleSO");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<UIStyleSO>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
