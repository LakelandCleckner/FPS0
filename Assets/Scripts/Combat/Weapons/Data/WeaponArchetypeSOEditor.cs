#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Core;

namespace Combat.Weapons.EditorTools
{
    // Archetype inspector with a recoil pattern editor.
    //
    // Pattern panel: the authored kicks as a climbing path in base-kick units, before stats.
    // Drag a shot to move just that shot, shift-click to add one at the end, right-click to delete.
    // An empty pattern shows the seeded one greyed out; Generate From Seed copies it in to edit.
    //
    // Spray panel: where each shot lands at a distance with no countering, simulated with the same
    // code the game runs (kick landing, centre delay, centre speed, recentring between shots). With a
    // target prefab the enemy is rendered behind it and shots are coloured head, body or miss.
    // Preview settings are editor-only (EditorPrefs) and never touch the asset.
    [CustomEditor(typeof(WeaponArchetypeSO))]
    public class WeaponArchetypeSOEditor : Editor
    {
        private const float DotRadius = 4f;
        private const float PickRadius = 8f;
        private const int Ghosts = 4;
        private const string Prefix = "FPS0.RecoilEditor.";

        // ---- preview settings (persisted per machine)
        private bool open;
        private float distance, ads, poseTime, aimHeight, facing;
        private int shots;
        private bool useArchetypeStats, showGhosts;
        private float stability, direction;
        private GameObject targetPrefab;
        private AnimationClip poseClip;

        // ---- preview scene
        private PreviewRenderUtility preview;
        private GameObject instance;
        private bool instanceDirty = true;
        private Material flatMaterial;
        private readonly List<EnemyHitbox> hitboxes = new List<EnemyHitbox>();

        // ---- pattern editing
        private int selected = -1, dragging = -1;
        private Rect frozenBounds;

        private void OnEnable()
        {
            open = EditorPrefs.GetBool(Prefix + "open", true);
            distance = EditorPrefs.GetFloat(Prefix + "distance", 15f);
            shots = EditorPrefs.GetInt(Prefix + "shots", 30);
            ads = EditorPrefs.GetFloat(Prefix + "ads", 1f);
            useArchetypeStats = EditorPrefs.GetBool(Prefix + "useStats", true);
            showGhosts = EditorPrefs.GetBool(Prefix + "ghosts", true);
            stability = EditorPrefs.GetFloat(Prefix + "stability", 50f);
            direction = EditorPrefs.GetFloat(Prefix + "direction", 75f);
            aimHeight = EditorPrefs.GetFloat(Prefix + "aimHeight", 1.3f);
            facing = EditorPrefs.GetFloat(Prefix + "facing", 0f);
            poseTime = EditorPrefs.GetFloat(Prefix + "poseTime", 0f);
            // Falls back to the pellet editor's target so one setup serves both tools.
            string prefab = EditorPrefs.GetString(Prefix + "prefab", EditorPrefs.GetString("FPS0.PelletEditor.prefab", ""));
            string clip = EditorPrefs.GetString(Prefix + "clip", EditorPrefs.GetString("FPS0.PelletEditor.clip", ""));
            targetPrefab = LoadByGuid<GameObject>(prefab);
            poseClip = LoadByGuid<AnimationClip>(clip);
            instanceDirty = true;
        }

        private void OnDisable()
        {
            DestroyInstance();
            if (preview != null) { preview.Cleanup(); preview = null; }
            if (flatMaterial != null) { Object.DestroyImmediate(flatMaterial); flatMaterial = null; }
        }

        private static T LoadByGuid<T>(string guid) where T : Object
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string GuidOf(Object o)
            => o != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long _) ? guid : "";

        // ================================================================ inspector

        public override void OnInspectorGUI()
        {
            var a = (WeaponArchetypeSO)target;
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "recoilPattern", "patternLoopFrom");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            open = EditorGUILayout.Foldout(open, "Recoil Pattern", true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(Prefix + "open", open);
            if (!open) return;

            DrawSettings(a);
            float stab = useArchetypeStats ? a.stability : stability;
            float dir = useArchetypeStats ? a.recoilDirection : direction;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Authored shape (before stats)", EditorStyles.miniBoldLabel);
            DrawPatternPanel(a);
            EditorGUILayout.LabelField(a.recoilPattern.Count == 0
                ? "Seeded from this archetype. Generate From Seed to edit it."
                : "Drag to move a shot - Shift+click to add - Right-click to delete", EditorStyles.centeredGreyMiniLabel);
            DrawSelectedField(a);
            DrawButtons(a);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"Spray at {distance:F0} m, no countering", EditorStyles.miniBoldLabel);
            if (targetPrefab != null && instanceDirty) RebuildInstance();
            // Placed before hit tests; the first shot lands on the aim height.
            if (instance != null)
                instance.transform.SetPositionAndRotation(new Vector3(0f, -aimHeight, distance), Quaternion.Euler(0f, 180f + facing, 0f));
            var main = Simulate(a, stab, dir, null, out Vector2 settle, out float duration);
            DrawSpray(a, stab, dir, main, settle);
            DrawReadout(a, main, settle, duration, stab, dir);

            EditorGUILayout.Space(4);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("patternLoopFrom"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("recoilPattern"), true);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings(WeaponArchetypeSO a)
        {
            EditorGUI.BeginChangeCheck();
            distance = EditorGUILayout.Slider("Distance (m)", distance, 1f, 60f);
            shots = EditorGUILayout.IntSlider("Shots", shots, 1, 100);
            ads = EditorGUILayout.Slider(new GUIContent("Aimed", "0 = hip, 1 = fully aimed."), ads, 0f, 1f);
            useArchetypeStats = EditorGUILayout.Toggle(new GUIContent("Use Archetype Stats", "Off = preview other stat values, as perks or rolls would."), useArchetypeStats);
            using (new EditorGUI.DisabledScope(useArchetypeStats))
            {
                stability = EditorGUILayout.Slider("Preview Stability", useArchetypeStats ? a.stability : stability, 0f, 100f);
                direction = EditorGUILayout.Slider("Preview Recoil Direction", useArchetypeStats ? a.recoilDirection : direction, 0f, 100f);
            }
            showGhosts = EditorGUILayout.Toggle(new GUIContent("Show Variance", "Faint extra sprays showing the random share."), showGhosts);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(Prefix + "distance", distance);
                EditorPrefs.SetInt(Prefix + "shots", shots);
                EditorPrefs.SetFloat(Prefix + "ads", ads);
                EditorPrefs.SetBool(Prefix + "useStats", useArchetypeStats);
                EditorPrefs.SetBool(Prefix + "ghosts", showGhosts);
                if (!useArchetypeStats)
                {
                    EditorPrefs.SetFloat(Prefix + "stability", stability);
                    EditorPrefs.SetFloat(Prefix + "direction", direction);
                }
            }

            EditorGUI.BeginChangeCheck();
            targetPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Target Prefab", "Enemy to preview against. Editor-only."), targetPrefab, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(targetPrefab == null))
            {
                poseClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Pose Clip", "Optional: stand the model in this clip instead of its bind pose."), poseClip, typeof(AnimationClip), false);
                using (new EditorGUI.DisabledScope(poseClip == null))
                    poseTime = EditorGUILayout.Slider("Pose Time", poseTime, 0f, 1f);
                aimHeight = EditorGUILayout.Slider(new GUIContent("Aim Height (m)", "Height above the target's feet where the first shot lands."), aimHeight, 0f, 2.5f);
                facing = EditorGUILayout.Slider(new GUIContent("Target Facing", "0 = facing you, 90 = side-on."), facing, -180f, 180f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(Prefix + "prefab", GuidOf(targetPrefab));
                EditorPrefs.SetString(Prefix + "clip", GuidOf(poseClip));
                EditorPrefs.SetFloat(Prefix + "poseTime", poseTime);
                EditorPrefs.SetFloat(Prefix + "aimHeight", aimHeight);
                EditorPrefs.SetFloat(Prefix + "facing", facing);
                instanceDirty = true;
                if (targetPrefab == null) DestroyInstance();
            }
        }

        // ================================================================ simulation

        // Where each shot lands (degrees: x up, y right), run through the game's own recoil step.
        private List<Vector2> Simulate(WeaponArchetypeSO a, float stab, float dir, System.Random rng,
                                       out Vector2 settle, out float duration)
        {
            const float dt = 1f / 240f;
            float rpm = a.roundsPerMinute > 0f ? a.roundsPerMinute : 600f;
            float interval = 60f / rpm;

            var s = new WeaponRecoil.State { SinceShot = 100f };
            var baked = Vector2.zero;
            var landed = new List<Vector2>(shots);

            for (int i = 0; i < shots; i++)
            {
                landed.Add(baked + s.Offset);
                float r1 = rng != null ? (float)rng.NextDouble() * 2f - 1f : 0f;
                float r2 = rng != null ? (float)rng.NextDouble() * 2f - 1f : 0f;
                Vector2 residual = rng != null ? Random2(rng) : Vector2.zero;
                WeaponRecoil.AddKick(ref s, a, WeaponRecoil.ShotKick(a, s.Shot, stab, dir, ads, r1, r2), residual);

                if (i == shots - 1) break;
                for (float t = 0f; t < interval; t += dt)
                    if (WeaponRecoil.Step(ref s, a, dt)) Bake(ref s, ref baked);
            }

            // Let it recentre after the last shot.
            duration = (shots - 1) * interval;
            for (float t = 0f; t < 4f; t += dt)
                if (WeaponRecoil.Step(ref s, a, dt)) { Bake(ref s, ref baked); break; }
            settle = baked + s.Offset;
            return landed;
        }

        // Mirrors the runtime bake: settled rest moves into the look and the pattern restarts.
        private static void Bake(ref WeaponRecoil.State s, ref Vector2 baked)
        {
            baked += s.Rest;
            s.Offset = s.Rest = Vector2.zero;
            s.Shot = 0;
        }

        private static Vector2 Random2(System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        // ================================================================ pattern panel

        private List<Vector2> ShownPattern(WeaponArchetypeSO a)
            => a.recoilPattern.Count > 0 ? a.recoilPattern : WeaponRecoil.SeededPattern(a, shots);

        private static List<Vector2> Cumulative(List<Vector2> kicks)
        {
            var c = new List<Vector2>(kicks.Count);
            var sum = Vector2.zero;
            foreach (var k in kicks) { sum += k; c.Add(sum); }
            return c;
        }

        private void DrawPatternPanel(WeaponArchetypeSO a)
        {
            float width = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, 340f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect rect = GUILayoutUtility.GetRect(width, 220f, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            bool authored = a.recoilPattern.Count > 0;
            var points = Cumulative(ShownPattern(a));

            // Frozen while dragging so the view doesn't chase the point.
            Rect b = dragging >= 0 ? frozenBounds : Bounds(points);
            if (dragging < 0) frozenBounds = b;
            const float margin = 14f;
            float scale = Mathf.Min((rect.width - 2f * margin) / b.width, (rect.height - 2f * margin) / b.height);
            var origin = new Vector2(rect.center.x - b.center.x * scale, rect.yMax - margin + b.yMin * scale);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
                Handles.color = new Color(1f, 1f, 1f, 0.12f);
                Handles.DrawLine(new Vector3(origin.x, rect.yMin), new Vector3(origin.x, rect.yMax));

                Vector2 prev = origin;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 px = ToPx(points[i], origin, scale);
                    Handles.color = new Color(1f, 1f, 1f, authored ? 0.35f : 0.15f);
                    Handles.DrawLine(prev, px);
                    prev = px;
                }
                Handles.color = new Color(1f, 1f, 1f, 0.6f);
                Handles.DrawWireDisc(origin, Vector3.forward, DotRadius);
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 px = ToPx(points[i], origin, scale);
                    Handles.color = Color.black;
                    Handles.DrawSolidDisc(px, Vector3.forward, DotRadius + 1f);
                    Handles.color = !authored ? new Color(0.55f, 0.55f, 0.55f)
                                  : i == selected ? Color.cyan
                                  : i >= a.patternLoopFrom && a.patternLoopFrom > 0 ? new Color(1f, 0.7f, 0.3f)
                                  : new Color(0.9f, 0.9f, 0.9f);
                    Handles.DrawSolidDisc(px, Vector3.forward, DotRadius);
                }
            }

            if (authored) HandlePatternInput(a, rect, points, origin, scale);
        }

        private static Rect Bounds(List<Vector2> points)
        {
            float minX = -1f, maxX = 1f, minY = 0f, maxY = 2f;
            foreach (var p in points)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            return Rect.MinMaxRect(minX - 0.5f, minY, maxX + 0.5f, maxY + 0.5f);
        }

        private static Vector2 ToPx(Vector2 p, Vector2 origin, float scale) => origin + new Vector2(p.x, -p.y) * scale;
        private static Vector2 FromPx(Vector2 px, Vector2 origin, float scale) { var d = (px - origin) / scale; return new Vector2(d.x, -d.y); }

        private void HandlePatternInput(WeaponArchetypeSO a, Rect rect, List<Vector2> points, Vector2 origin, float scale)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) && dragging < 0) return;
            var list = a.recoilPattern;

            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    int hit = Pick(points, e.mousePosition, origin, scale);
                    if (e.button == 1 && hit >= 0)
                    {
                        Undo.RecordObject(a, "Remove Shot");
                        // Later shots keep their place: the next kick absorbs this one.
                        if (hit + 1 < list.Count) list[hit + 1] += list[hit];
                        list.RemoveAt(hit);
                        selected = -1;
                        EditorUtility.SetDirty(a);
                        e.Use();
                    }
                    else if (e.button == 0 && hit >= 0) { selected = dragging = hit; e.Use(); }
                    else if (e.button == 0 && e.shift)
                    {
                        Undo.RecordObject(a, "Add Shot");
                        Vector2 last = points.Count > 0 ? points[points.Count - 1] : Vector2.zero;
                        list.Add(FromPx(e.mousePosition, origin, scale) - last);
                        selected = list.Count - 1;
                        EditorUtility.SetDirty(a);
                        e.Use();
                    }
                    else if (e.button == 0) { selected = -1; e.Use(); }
                    break;
                }

                case EventType.MouseDrag when dragging >= 0 && dragging < list.Count:
                {
                    Undo.RecordObject(a, "Move Shot");
                    // Move only this shot: its kick changes and the next kick undoes the change.
                    Vector2 delta = FromPx(e.mousePosition, origin, scale) - points[dragging];
                    list[dragging] += delta;
                    if (dragging + 1 < list.Count) list[dragging + 1] -= delta;
                    EditorUtility.SetDirty(a);
                    e.Use();
                    break;
                }

                case EventType.MouseUp when dragging >= 0:
                    dragging = -1;
                    e.Use();
                    break;
            }

            if (e.type == EventType.Used) Repaint();
        }

        private static int Pick(List<Vector2> points, Vector2 mouse, Vector2 origin, float scale)
        {
            int best = -1;
            float bestDist = PickRadius * PickRadius;
            for (int i = 0; i < points.Count; i++)
            {
                float d = (ToPx(points[i], origin, scale) - mouse).sqrMagnitude;
                if (d <= bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private void DrawSelectedField(WeaponArchetypeSO a)
        {
            if (selected < 0 || selected >= a.recoilPattern.Count) return;
            EditorGUI.BeginChangeCheck();
            Vector2 v = EditorGUILayout.Vector2Field($"Shot {selected + 1} kick", a.recoilPattern[selected]);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(a, "Edit Shot");
                a.recoilPattern[selected] = v;
                EditorUtility.SetDirty(a);
            }
        }

        private void DrawButtons(WeaponArchetypeSO a)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Generate From Seed", "Copy the seeded pattern in, one point per preview shot.")))
                Edit(a, "Generate Pattern", () => a.recoilPattern = WeaponRecoil.SeededPattern(a, shots));
            using (new EditorGUI.DisabledScope(a.recoilPattern.Count == 0))
            {
                if (GUILayout.Button("Mirror"))
                    Edit(a, "Mirror Pattern", () => { for (int i = 0; i < a.recoilPattern.Count; i++) a.recoilPattern[i] = new Vector2(-a.recoilPattern[i].x, a.recoilPattern[i].y); });
                if (GUILayout.Button(new GUIContent("Smooth", "Average each shot's sideways kick with its neighbours.")))
                    Edit(a, "Smooth Pattern", () => Smooth(a.recoilPattern));
                if (GUILayout.Button(new GUIContent("Clear", "Back to the seeded pattern.")))
                    Edit(a, "Clear Pattern", () => a.recoilPattern.Clear());
            }
            GUILayout.EndHorizontal();
        }

        private void Edit(WeaponArchetypeSO a, string label, System.Action change)
        {
            Undo.RecordObject(a, label);
            change();
            selected = -1;
            EditorUtility.SetDirty(a);
        }

        private static void Smooth(List<Vector2> p)
        {
            var copy = new List<Vector2>(p);
            for (int i = 0; i < p.Count; i++)
            {
                float l = copy[Mathf.Max(0, i - 1)].x, r = copy[Mathf.Min(p.Count - 1, i + 1)].x;
                p[i] = new Vector2((l + copy[i].x * 2f + r) * 0.25f, copy[i].y);
            }
        }

        // ================================================================ spray panel

        private void DrawSpray(WeaponArchetypeSO a, float stab, float dir, List<Vector2> main, Vector2 settle)
        {
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, 340f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (Event.current.type != EventType.Repaint) return;

            // View extent in metres: fit the spray and a full body.
            float halfView = 1.1f;
            foreach (var d in main) halfView = Mathf.Max(halfView, Mathf.Max(Mathf.Abs(Metres(d.y)), Mathf.Abs(Metres(d.x))) + 0.3f);
            if (instance != null) halfView = Mathf.Max(halfView, aimHeight + 0.2f);
            float ppm = size * 0.5f / halfView;
            Vector2 center = rect.center;

            if (instance != null) DrawModel(rect, halfView);
            else
            {
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
                DrawBox(center, ppm);
            }

            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawLine(new Vector3(rect.xMin, center.y), new Vector3(rect.xMax, center.y));
            Handles.DrawLine(new Vector3(center.x, rect.yMin), new Vector3(center.x, rect.yMax));

            if (showGhosts && a.patternVariance > 0f)
                for (int g = 0; g < Ghosts; g++)
                {
                    var ghost = Simulate(a, stab, dir, new System.Random(1000 + g * 7919), out _, out _);
                    DrawPath(ghost, center, ppm, new Color(1f, 1f, 1f, 0.12f), false);
                }

            DrawPath(main, center, ppm, new Color(1f, 0.8f, 0.2f, 0.6f), true);

            // Where the aim settles after recentring.
            Vector2 s = ToSpray(settle, center, ppm);
            Handles.color = new Color(0.4f, 0.8f, 1f);
            Handles.DrawLine(s + new Vector2(-5f, -5f), s + new Vector2(5f, 5f));
            Handles.DrawLine(s + new Vector2(-5f, 5f), s + new Vector2(5f, -5f));

            Handles.color = new Color(1f, 1f, 1f, 0.5f);
            float tickY = rect.yMax - 8f;
            Handles.DrawLine(new Vector3(rect.xMin + 8f, tickY), new Vector3(rect.xMin + 8f + ppm, tickY));
            GUI.Label(new Rect(rect.xMin + 8f, tickY - 16f, 60f, 16f), "1 m", EditorStyles.miniLabel);
        }

        private void DrawPath(List<Vector2> landed, Vector2 center, float ppm, Color line, bool dots)
        {
            Handles.color = line;
            for (int i = 1; i < landed.Count; i++)
                Handles.DrawLine(ToSpray(landed[i - 1], center, ppm), ToSpray(landed[i], center, ppm));
            if (!dots) return;

            for (int i = 0; i < landed.Count; i++)
            {
                Vector2 px = ToSpray(landed[i], center, ppm);
                Handles.color = Color.black;
                Handles.DrawSolidDisc(px, Vector3.forward, DotRadius + 1f);
                Handles.color = HitColor(landed[i]);
                Handles.DrawSolidDisc(px, Vector3.forward, DotRadius);
                if (i == 0 || (i + 1) % 5 == 0)
                    GUI.Label(new Rect(px.x + 5f, px.y - 8f, 30f, 16f), (i + 1).ToString(), EditorStyles.miniLabel);
            }
        }

        private float Metres(float degrees) => distance * Mathf.Tan(degrees * Mathf.Deg2Rad);
        private Vector2 ToSpray(Vector2 deg, Vector2 center, float ppm) => center + new Vector2(Metres(deg.y), -Metres(deg.x)) * ppm;

        private Color HitColor(Vector2 deg)
        {
            var hit = Classify(deg);
            if (hit == 2) return new Color(1f, 0.85f, 0.1f);
            if (hit == 1) return new Color(0.3f, 1f, 0.4f);
            return new Color(1f, 0.3f, 0.25f);
        }

        // 2 head, 1 body, 0 miss: against the model's hitboxes, else a body-sized box.
        private int Classify(Vector2 deg)
        {
            if (instance != null && hitboxes.Count > 0)
            {
                var dir = new Vector3(Mathf.Tan(deg.y * Mathf.Deg2Rad), Mathf.Tan(deg.x * Mathf.Deg2Rad), 1f).normalized;
                var ray = new Ray(Vector3.zero, dir);
                EnemyHitbox best = null;
                float bestT = distance + 10f;
                foreach (var hb in hitboxes)
                {
                    if (hb == null) continue;
                    foreach (var col in hb.GetComponentsInChildren<Collider>(true))
                    {
                        if (col.GetComponentInParent<EnemyHitbox>() != hb) continue;
                        if (ColliderMath.Raycast(col, ray, out float t) && t < bestT) { bestT = t; best = hb; }
                    }
                }
                return best == null ? 0 : best.bodyPart == BodyPart.Head ? 2 : 1;
            }

            var m = new Vector2(Metres(deg.y), Metres(deg.x));
            const float headR = 0.125f;
            var head = new Vector2(0f, 0.9f + headR);
            if ((m - head).sqrMagnitude <= headR * headR) return 2;
            return Mathf.Abs(m.x) <= 0.25f && Mathf.Abs(m.y) <= 0.9f ? 1 : 0;
        }

        private void DrawReadout(WeaponArchetypeSO a, List<Vector2> landed, Vector2 settle, float duration, float stab, float dir)
        {
            int head = 0, body = 0;
            foreach (var d in landed) { int c = Classify(d); if (c == 2) head++; else if (c == 1) body++; }
            Vector2 last = landed.Count > 0 ? landed[landed.Count - 1] : Vector2.zero;
            float bias = WeaponRecoil.Bias(dir);
            string lean = Mathf.Abs(bias) < 0.05f ? "vertical" : bias > 0f ? "leans right" : "leans left";

            string text = $"{landed.Count} shots over {duration:F2} s at {a.roundsPerMinute:F0} rpm   |   stability {stab:F0}, direction {dir:F0} ({lean})\n" +
                          $"Last shot: {last.x:F2} deg up, {last.y:F2} deg {(last.y >= 0f ? "right" : "left")}  ({Metres(last.x):F2} m, {Mathf.Abs(Metres(last.y)):F2} m)\n" +
                          $"Settles at {settle.x:F2} deg up, {settle.y:F2} deg sideways   |   Head {head}  Body {body}  Miss {landed.Count - head - body}";
            if (targetPrefab != null && instance != null && hitboxes.Count == 0)
                text += "\nTarget has no EnemyHitbox colliders - showing hits against the box outline instead.";
            EditorGUILayout.HelpBox(text, MessageType.None);
        }

        // ================================================================ preview scene

        private void EnsurePreview()
        {
            if (preview != null) return;
            preview = new PreviewRenderUtility();
            preview.camera.nearClipPlane = 0.05f;
            preview.camera.farClipPlane = 200f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, 25f, 0f);
            preview.lights[1].intensity = 0.6f;
            preview.ambientColor = new Color(0.35f, 0.35f, 0.35f);
        }

        private void RebuildInstance()
        {
            instanceDirty = false;
            DestroyInstance();
            if (targetPrefab == null) return;

            EnsurePreview();
            instance = (GameObject)Object.Instantiate(targetPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            ApplyFlatMaterial(instance);
            preview.AddSingleGO(instance);

            if (poseClip != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                poseClip.SampleAnimation(animator != null ? animator.gameObject : instance, poseTime * poseClip.length);
            }

            hitboxes.Clear();
            instance.GetComponentsInChildren(true, hitboxes);
        }

        // Flat grey unlit material on the preview copy only; the project's lit shaders render magenta here.
        private void ApplyFlatMaterial(GameObject go)
        {
            if (flatMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                flatMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                var grey = new Color(0.5f, 0.5f, 0.53f);
                if (flatMaterial.HasProperty("_Color")) flatMaterial.SetColor("_Color", grey);
                if (flatMaterial.HasProperty("_ZWrite")) flatMaterial.SetInt("_ZWrite", 1);
                if (flatMaterial.HasProperty("_Cull")) flatMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = flatMaterial;
                r.sharedMaterials = mats;
            }
        }

        private void DestroyInstance()
        {
            if (instance != null) Object.DestroyImmediate(instance);
            instance = null;
            hitboxes.Clear();
        }

        // Camera at the origin looking down +Z, spanning +-halfView metres at the preview distance.
        private void DrawModel(Rect rect, float halfView)
        {
            EnsurePreview();
            var cam = preview.camera;
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cam.fieldOfView = 2f * Mathf.Atan(halfView / distance) * Mathf.Rad2Deg;
            cam.farClipPlane = distance + 20f;
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render();
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private static void DrawBox(Vector2 center, float ppm)
        {
            var c = new Color(0.3f, 0.5f, 0.85f, 0.25f);
            float w = 0.5f * ppm, h = 1.8f * ppm;
            EditorGUI.DrawRect(new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h), c);
            Handles.color = new Color(0.3f, 0.5f, 0.85f, 0.45f);
            float headR = 0.125f * ppm;
            Handles.DrawSolidDisc(new Vector2(center.x, center.y - h * 0.5f - headR), Vector3.forward, headR);
        }
    }
}
#endif
