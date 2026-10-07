using UnityEditor;
using UnityEngine;

namespace Combat.Weapons.EditorTools
{
    // Visual pattern editor for PelletDeliverySO.
    //
    // Shows the pattern in REAL METERS at a chosen distance, with a body-sized target,
    // so spreadAngle stops being an abstract number. Drag pellets to place them;
    // shift-click empty space to add one; right-click a pellet to delete it.
    //
    // The preview multiplier stands in for the pellet_spread stat (Full Choke, Shot
    // Package...) — it changes only the preview, never the asset.
    [CustomEditor(typeof(PelletDeliverySO))]
    public class PelletDeliverySOEditor : Editor
    {
        private const float DotRadius = 5f;
        private const float PickRadius = 9f;
        private const float BodyWidth = 0.5f;     // meters
        private const float BodyHeight = 1.8f;
        private const float HeadDiameter = 0.25f;

        private static float previewDistance = 10f;
        private static float previewSpread = 1f;
        private static bool showBody = true;

        private int selected = -1;
        private int dragging = -1;

        public override void OnInspectorGUI()
        {
            var so = (PelletDeliverySO)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("pelletDelivery"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spreadAngle"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Pattern Preview", EditorStyles.boldLabel);

            previewDistance = EditorGUILayout.Slider("Distance (m)", previewDistance, 1f, 50f);
            previewSpread = EditorGUILayout.Slider(
                new GUIContent("Preview pellet_spread", "Preview only — simulates spread perks."),
                previewSpread, 0.1f, 2f);
            showBody = EditorGUILayout.Toggle("Show target body", showBody);

            float radiusMeters = previewDistance *
                Mathf.Tan(Mathf.Clamp(so.spreadAngle * previewSpread, 0f, 89f) * Mathf.Deg2Rad);

            int onBody = CountOnBody(so, radiusMeters);
            EditorGUILayout.HelpBox(
                $"{so.pattern.Count} pellets   |   pattern width {radiusMeters * 2f:F2} m at {previewDistance:F0} m" +
                (showBody ? $"   |   {onBody}/{so.pattern.Count} on body " +
                            $"({(so.pattern.Count > 0 ? 100f * onBody / so.pattern.Count : 0f):F0}% damage)" : ""),
                MessageType.None);

            DrawPreview(so, radiusMeters);

            EditorGUILayout.LabelField("Drag to move · Shift+click to add · Right-click to delete",
                EditorStyles.centeredGreyMiniLabel);

            DrawSelectedField(so);
            DrawButtons(so);

            EditorGUILayout.Space(6);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pattern"), true);
            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ preview

        private void DrawPreview(PelletDeliverySO so, float radiusMeters)
        {
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, 340f);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // View extent in meters: fit the pattern and, if shown, the body.
            float halfView = Mathf.Max(radiusMeters * 1.25f, 0.25f);
            if (showBody) halfView = Mathf.Max(halfView, BodyHeight * 0.5f + HeadDiameter + 0.1f);
            float pxPerMeter = (size * 0.5f) / halfView;
            Vector2 center = rect.center;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));

                if (showBody) DrawBody(center, pxPerMeter);

                Handles.color = new Color(1f, 1f, 1f, 0.12f);
                Handles.DrawLine(new Vector3(rect.xMin, center.y), new Vector3(rect.xMax, center.y));
                Handles.DrawLine(new Vector3(center.x, rect.yMin), new Vector3(center.x, rect.yMax));

                // Spread edge (unit circle) at the preview multiplier.
                Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                Handles.DrawWireDisc(center, Vector3.forward, radiusMeters * pxPerMeter);

                // 1 m reference tick.
                Handles.color = new Color(1f, 1f, 1f, 0.35f);
                var tickY = rect.yMax - 8f;
                Handles.DrawLine(new Vector3(rect.xMin + 8f, tickY), new Vector3(rect.xMin + 8f + pxPerMeter, tickY));
                GUI.Label(new Rect(rect.xMin + 8f, tickY - 16f, 60f, 16f), "1 m", EditorStyles.miniLabel);

                for (int i = 0; i < so.pattern.Count; i++)
                {
                    Vector2 px = ToPixels(so.pattern[i], center, radiusMeters, pxPerMeter);
                    Handles.color = i == selected ? Color.cyan : new Color(1f, 0.35f, 0.25f);
                    Handles.DrawSolidDisc(px, Vector3.forward, DotRadius);
                }
            }

            HandleInput(so, rect, center, radiusMeters, pxPerMeter);
        }

        private static void DrawBody(Vector2 center, float pxPerMeter)
        {
            var c = new Color(0.3f, 0.5f, 0.85f, 0.25f);
            // Aim point = center mass: body centered on the crosshair, head above.
            float w = BodyWidth * pxPerMeter, h = BodyHeight * pxPerMeter;
            EditorGUI.DrawRect(new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h), c);

            Handles.color = new Color(0.3f, 0.5f, 0.85f, 0.45f);
            float headR = HeadDiameter * 0.5f * pxPerMeter;
            Handles.DrawSolidDisc(new Vector2(center.x, center.y - h * 0.5f - headR), Vector3.forward, headR);
        }

        private static int CountOnBody(PelletDeliverySO so, float radiusMeters)
        {
            int count = 0;
            float headR = HeadDiameter * 0.5f;
            Vector2 head = new Vector2(0f, BodyHeight * 0.5f + headR);
            foreach (var p in so.pattern)
            {
                Vector2 m = p * radiusMeters;
                bool torso = Mathf.Abs(m.x) <= BodyWidth * 0.5f && Mathf.Abs(m.y) <= BodyHeight * 0.5f;
                bool onHead = (m - head).sqrMagnitude <= headR * headR;
                if (torso || onHead) count++;
            }
            return count;
        }

        // Pattern space (unit circle, y up) <-> GUI pixels (y down).
        private static Vector2 ToPixels(Vector2 p, Vector2 center, float radiusMeters, float pxPerMeter)
            => center + new Vector2(p.x, -p.y) * radiusMeters * pxPerMeter;

        private static Vector2 ToPattern(Vector2 px, Vector2 center, float radiusMeters, float pxPerMeter)
        {
            if (radiusMeters <= 0f) return Vector2.zero;
            Vector2 d = (px - center) / (radiusMeters * pxPerMeter);
            Vector2 p = new Vector2(d.x, -d.y);
            return p.sqrMagnitude > 1f ? p.normalized : p;   // stay inside the spread edge
        }

        // -------------------------------------------------------------------- input

        private void HandleInput(PelletDeliverySO so, Rect rect, Vector2 center,
                                 float radiusMeters, float pxPerMeter)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) && dragging < 0) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    int hit = Pick(so, e.mousePosition, center, radiusMeters, pxPerMeter);

                    if (e.button == 1 && hit >= 0)
                    {
                        Undo.RecordObject(so, "Remove Pellet");
                        so.pattern.RemoveAt(hit);
                        selected = -1;
                        EditorUtility.SetDirty(so);
                        e.Use();
                    }
                    else if (e.button == 0 && hit >= 0)
                    {
                        selected = dragging = hit;
                        e.Use();
                    }
                    else if (e.button == 0 && e.shift)
                    {
                        Undo.RecordObject(so, "Add Pellet");
                        so.pattern.Add(ToPattern(e.mousePosition, center, radiusMeters, pxPerMeter));
                        selected = so.pattern.Count - 1;
                        EditorUtility.SetDirty(so);
                        e.Use();
                    }
                    else if (e.button == 0)
                    {
                        selected = -1;
                        e.Use();
                    }
                    break;
                }

                case EventType.MouseDrag when dragging >= 0 && dragging < so.pattern.Count:
                    Undo.RecordObject(so, "Move Pellet");
                    so.pattern[dragging] = ToPattern(e.mousePosition, center, radiusMeters, pxPerMeter);
                    EditorUtility.SetDirty(so);
                    e.Use();
                    break;

                case EventType.MouseUp when dragging >= 0:
                    dragging = -1;
                    e.Use();
                    break;
            }

            if (e.type == EventType.Used) Repaint();
        }

        private static int Pick(PelletDeliverySO so, Vector2 mouse, Vector2 center,
                                float radiusMeters, float pxPerMeter)
        {
            int best = -1;
            float bestDist = PickRadius * PickRadius;
            for (int i = 0; i < so.pattern.Count; i++)
            {
                float d = (ToPixels(so.pattern[i], center, radiusMeters, pxPerMeter) - mouse).sqrMagnitude;
                if (d <= bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------- extras

        private void DrawSelectedField(PelletDeliverySO so)
        {
            if (selected < 0 || selected >= so.pattern.Count) return;

            EditorGUI.BeginChangeCheck();
            Vector2 v = EditorGUILayout.Vector2Field($"Pellet {selected}", so.pattern[selected]);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(so, "Edit Pellet");
                so.pattern[selected] = v.sqrMagnitude > 1f ? v.normalized : v;
                EditorUtility.SetDirty(so);
            }
        }

        private void DrawButtons(PelletDeliverySO so)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Center + Ring (8)"))
            {
                Undo.RecordObject(so, "Generate Pattern");
                so.GenerateCenterRing8();
                selected = -1;
                EditorUtility.SetDirty(so);
            }
            if (GUILayout.Button("Two Rings (9)"))
            {
                Undo.RecordObject(so, "Generate Pattern");
                so.GenerateTwoRings9();
                selected = -1;
                EditorUtility.SetDirty(so);
            }
            if (GUILayout.Button("Mirror L→R"))
            {
                Undo.RecordObject(so, "Mirror Pattern");
                // Keep the left half (and center line), rebuild the right as its mirror.
                so.pattern.RemoveAll(q => q.x > 0.001f);
                int n = so.pattern.Count;
                for (int i = 0; i < n; i++)
                    if (so.pattern[i].x < -0.001f) so.pattern.Add(new Vector2(-so.pattern[i].x, so.pattern[i].y));
                EditorUtility.SetDirty(so);
            }
            GUILayout.EndHorizontal();
        }
    }
}
