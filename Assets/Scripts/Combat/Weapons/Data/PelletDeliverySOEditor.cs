using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Core;

namespace Combat.Weapons.EditorTools
{
    // Visual pattern editor for PelletDeliverySO.
    //
    // Shows the pattern in REAL METERS at a chosen distance. With a Target Prefab set,
    // the enemy model is rendered behind the pattern by a camera matching the spread
    // geometry, and every pellet is raycast against the model's EnemyHitbox colliders,
    // so the readout is real: head / body / miss counts and expected damage including
    // each hitbox's multiplier. An optional Pose Clip stands the model the way it is in
    // game instead of in its bind (T) pose.
    //
    // Drag pellets to place them; shift-click empty space to add; right-click to delete.
    // Preview settings are editor-only (EditorPrefs) and never touch the asset —
    // including the pellet_spread preview, which only simulates spread perks.
    [CustomEditor(typeof(PelletDeliverySO))]
    public class PelletDeliverySOEditor : Editor
    {
        private const float DotRadius = 5f;
        private const float PickRadius = 9f;
        private const float BoxBodyWidth = 0.5f;
        private const float BoxBodyHeight = 1.8f;
        private const float BoxHeadDiameter = 0.25f;

        private const string Prefix = "FPS0.PelletEditor.";

        // ---- preview settings (persisted per machine)
        private float previewDistance;
        private float previewSpread;
        private float aimHeight;
        private float facing;
        private float poseTime;
        private GameObject targetPrefab;
        private AnimationClip poseClip;

        // ---- preview scene
        private PreviewRenderUtility preview;
        private GameObject instance;
        private bool instanceDirty = true;
        private Material flatMaterial;   // replaces the model's own materials in the preview
        private readonly List<EnemyHitbox> hitboxes = new List<EnemyHitbox>();

        private int selected = -1;
        private int dragging = -1;

        private enum PelletResult { Miss, Body, Head }
        private PelletResult[] results = new PelletResult[0];
        private float[] resultMultipliers = new float[0];

        // ================================================================ lifecycle

        private void OnEnable()
        {
            previewDistance = EditorPrefs.GetFloat(Prefix + "distance", 10f);
            previewSpread = EditorPrefs.GetFloat(Prefix + "spread", 1f);
            aimHeight = EditorPrefs.GetFloat(Prefix + "aimHeight", 1.3f);
            facing = EditorPrefs.GetFloat(Prefix + "facing", 0f);
            poseTime = EditorPrefs.GetFloat(Prefix + "poseTime", 0f);
            targetPrefab = LoadByGuid<GameObject>(EditorPrefs.GetString(Prefix + "prefab", ""));
            poseClip = LoadByGuid<AnimationClip>(EditorPrefs.GetString(Prefix + "clip", ""));
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
        {
            if (o == null) return "";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long _) ? guid : "";
        }

        // ================================================================ inspector

        public override void OnInspectorGUI()
        {
            var so = (PelletDeliverySO)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pelletDelivery"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spreadAngle"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Pattern Preview", EditorStyles.boldLabel);
            DrawPreviewSettings();

            float radiusMeters = previewDistance *
                Mathf.Tan(Mathf.Clamp(so.spreadAngle * previewSpread, 0f, 89f) * Mathf.Deg2Rad);

            if (targetPrefab != null && instanceDirty) RebuildInstance();
            EvaluatePellets(so, radiusMeters);
            DrawReadout(so, radiusMeters);

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

        private void DrawPreviewSettings()
        {
            EditorGUI.BeginChangeCheck();
            previewDistance = EditorGUILayout.Slider("Distance (m)", previewDistance, 1f, 50f);
            previewSpread = EditorGUILayout.Slider(
                new GUIContent("Preview pellet_spread", "Preview only — simulates spread perks."),
                previewSpread, 0.1f, 2f);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(Prefix + "distance", previewDistance);
                EditorPrefs.SetFloat(Prefix + "spread", previewSpread);
            }

            EditorGUI.BeginChangeCheck();
            targetPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Target Prefab", "Enemy to preview against. Editor-only."),
                targetPrefab, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(targetPrefab == null))
            {
                poseClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Pose Clip", "Optional: stand the model in this clip instead of its bind pose."),
                    poseClip, typeof(AnimationClip), false);
                using (new EditorGUI.DisabledScope(poseClip == null))
                    poseTime = EditorGUILayout.Slider("Pose Time", poseTime, 0f, 1f);
                aimHeight = EditorGUILayout.Slider(
                    new GUIContent("Aim Height (m)", "Height above the target's feet where the crosshair sits."),
                    aimHeight, 0f, 2.5f);
                facing = EditorGUILayout.Slider(
                    new GUIContent("Target Facing", "0 = facing you, 90 = side-on."), facing, -180f, 180f);
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

        private void DrawReadout(PelletDeliverySO so, float radiusMeters)
        {
            int n = so.pattern.Count;
            int head = 0, body = 0;
            float damage = 0f;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] == PelletResult.Head) head++;
                else if (results[i] == PelletResult.Body) body++;
                damage += resultMultipliers[i];
            }

            string text = $"{n} pellets   |   pattern width {radiusMeters * 2f:F2} m at {previewDistance:F0} m\n" +
                          $"Head {head}   Body {body}   Miss {n - head - body}   |   " +
                          $"expected damage {(n > 0 ? 100f * damage / n : 0f):F0}% of a full body hit";

            if (targetPrefab != null && hitboxes.Count == 0)
                text += "\nTarget has no EnemyHitbox colliders — showing hits against the box outline instead.";

            EditorGUILayout.HelpBox(text, MessageType.None);
        }

        // ================================================================ instance

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
                var poseRoot = animator != null ? animator.gameObject : instance;
                poseClip.SampleAnimation(poseRoot, poseTime * poseClip.length);
            }

            // Placed AFTER sampling: a clip with root curves must not move the target.
            // Camera sits at the origin looking down +Z; the aim point on the model lands
            // on the camera's center line.
            instance.transform.SetPositionAndRotation(
                new Vector3(0f, -aimHeight, previewDistance),
                Quaternion.Euler(0f, 180f + facing, 0f));

            hitboxes.Clear();
            instance.GetComponentsInChildren(true, hitboxes);
        }

        // A flat grey unlit material on every renderer. The model's own materials often can't
        // be drawn by the preview camera (magenta), and a plain silhouette reads the
        // pattern better anyway. Only the preview instance is touched, never the prefab.
        private void ApplyFlatMaterial(GameObject go)
        {
            if (flatMaterial == null)
            {
                // Unlit editor shaders: pipeline-independent, so the preview camera can
                // always draw them (the project's own lit shaders render magenta here).
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

        // ================================================================ hit tests

        private void EvaluatePellets(PelletDeliverySO so, float radiusMeters)
        {
            int n = so.pattern.Count;
            if (results.Length != n)
            {
                results = new PelletResult[n];
                resultMultipliers = new float[n];
            }

            // Distance moved: the instance must follow before testing.
            if (instance != null)
                instance.transform.position = new Vector3(0f, -aimHeight, previewDistance);

            bool useModel = instance != null && hitboxes.Count > 0;
            float tanR = previewDistance > 0f ? radiusMeters / previewDistance : 0f;

            for (int i = 0; i < n; i++)
            {
                Vector2 p = so.pattern[i];
                results[i] = PelletResult.Miss;
                resultMultipliers[i] = 0f;

                if (useModel)
                {
                    var dir = new Vector3(p.x * tanR, p.y * tanR, 1f).normalized;
                    var hb = RaycastHitboxes(new Ray(Vector3.zero, dir), previewDistance + 10f);
                    if (hb != null)
                    {
                        results[i] = hb.bodyPart == BodyPart.Head ? PelletResult.Head : PelletResult.Body;
                        resultMultipliers[i] = hb.damageMultiplier;
                    }
                }
                else
                {
                    results[i] = BoxResult(p * radiusMeters);
                    resultMultipliers[i] = results[i] == PelletResult.Miss ? 0f : 1f;
                }
            }
        }

        // Fallback target: a 0.5 x 1.8 m body centered on the crosshair, head above.
        private static PelletResult BoxResult(Vector2 m)
        {
            float headR = BoxHeadDiameter * 0.5f;
            var head = new Vector2(0f, BoxBodyHeight * 0.5f + headR);
            if ((m - head).sqrMagnitude <= headR * headR) return PelletResult.Head;
            if (Mathf.Abs(m.x) <= BoxBodyWidth * 0.5f && Mathf.Abs(m.y) <= BoxBodyHeight * 0.5f)
                return PelletResult.Body;
            return PelletResult.Miss;
        }

        // Nearest EnemyHitbox along the ray. Pure math against collider shapes — the
        // preview scene has no physics world to query.
        private EnemyHitbox RaycastHitboxes(Ray ray, float maxDist)
        {
            EnemyHitbox best = null;
            float bestT = maxDist;

            foreach (var hb in hitboxes)
            {
                if (hb == null) continue;
                foreach (var col in hb.GetComponentsInChildren<Collider>(true))
                {
                    if (col.GetComponentInParent<EnemyHitbox>() != hb) continue;
                    if (ColliderMath.Raycast(col, ray, out float t) && t < bestT)
                    {
                        bestT = t;
                        best = hb;
                    }
                }
            }
            return best;
        }

        // ================================================================ drawing

        private void DrawPreview(PelletDeliverySO so, float radiusMeters)
        {
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, 340f);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // View extent in meters: fit the pattern and a full body.
            float halfView = Mathf.Max(radiusMeters * 1.25f, 0.25f);
            halfView = Mathf.Max(halfView, BoxBodyHeight * 0.5f + BoxHeadDiameter + 0.1f);
            if (instance != null) halfView = Mathf.Max(halfView, aimHeight + 0.2f);
            float pxPerMeter = (size * 0.5f) / halfView;
            Vector2 center = rect.center;

            if (Event.current.type == EventType.Repaint)
            {
                if (instance != null) DrawModel(rect, halfView);
                else
                {
                    EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
                    DrawBox(center, pxPerMeter);
                }

                Handles.color = new Color(1f, 1f, 1f, 0.15f);
                Handles.DrawLine(new Vector3(rect.xMin, center.y), new Vector3(rect.xMax, center.y));
                Handles.DrawLine(new Vector3(center.x, rect.yMin), new Vector3(center.x, rect.yMax));

                Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                Handles.DrawWireDisc(center, Vector3.forward, radiusMeters * pxPerMeter);

                Handles.color = new Color(1f, 1f, 1f, 0.5f);
                float tickY = rect.yMax - 8f;
                Handles.DrawLine(new Vector3(rect.xMin + 8f, tickY), new Vector3(rect.xMin + 8f + pxPerMeter, tickY));
                GUI.Label(new Rect(rect.xMin + 8f, tickY - 16f, 60f, 16f), "1 m", EditorStyles.miniLabel);

                for (int i = 0; i < so.pattern.Count; i++)
                {
                    Vector2 px = ToPixels(so.pattern[i], center, radiusMeters, pxPerMeter);
                    Handles.color = Color.black;
                    Handles.DrawSolidDisc(px, Vector3.forward, DotRadius + 1.5f);
                    Handles.color = i == selected ? Color.cyan : ResultColor(i);
                    Handles.DrawSolidDisc(px, Vector3.forward, DotRadius);
                }
            }

            HandleInput(so, rect, center, radiusMeters, pxPerMeter);
        }

        private Color ResultColor(int i)
        {
            if (i >= results.Length) return Color.white;
            switch (results[i])
            {
                case PelletResult.Head: return new Color(1f, 0.85f, 0.1f);
                case PelletResult.Body: return new Color(0.3f, 1f, 0.4f);
                default: return new Color(1f, 0.3f, 0.25f);
            }
        }

        // Camera at the origin looking down +Z with a FOV that spans exactly
        // ±halfView meters at the preview distance, so pixels and the pellet overlay
        // share one scale.
        private void DrawModel(Rect rect, float halfView)
        {
            EnsurePreview();
            var cam = preview.camera;
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cam.fieldOfView = 2f * Mathf.Atan(halfView / previewDistance) * Mathf.Rad2Deg;
            cam.farClipPlane = previewDistance + 20f;

            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render();
            var tex = preview.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
        }

        private static void DrawBox(Vector2 center, float pxPerMeter)
        {
            var c = new Color(0.3f, 0.5f, 0.85f, 0.25f);
            float w = BoxBodyWidth * pxPerMeter, h = BoxBodyHeight * pxPerMeter;
            EditorGUI.DrawRect(new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h), c);

            Handles.color = new Color(0.3f, 0.5f, 0.85f, 0.45f);
            float headR = BoxHeadDiameter * 0.5f * pxPerMeter;
            Handles.DrawSolidDisc(new Vector2(center.x, center.y - h * 0.5f - headR), Vector3.forward, headR);
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

        // ================================================================ input

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

        // ================================================================ extras

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
                selected = -1;
                EditorUtility.SetDirty(so);
            }
            GUILayout.EndHorizontal();
        }
    }

    // Ray vs collider shapes, in world space, without a physics scene. Returns the
    // distance along the ray to the first intersection (approximate for capsules —
    // closest approach, which is exact enough to rank overlapping hitboxes).
    internal static class ColliderMath
    {
        public static bool Raycast(Collider col, Ray ray, out float t)
        {
            t = 0f;
            if (!col.enabled) return false;

            switch (col)
            {
                case BoxCollider box: return RayBox(box, ray, out t);
                case SphereCollider sphere: return RaySphere(sphere, ray, out t);
                case CapsuleCollider capsule: return RayCapsule(capsule, ray, out t);
                default:
                    // Mesh and others: fall back to the world bounds.
                    return col.bounds.IntersectRay(ray, out t);
            }
        }

        private static bool RayBox(BoxCollider box, Ray ray, out float t)
        {
            var tr = box.transform;
            // Local-space ray WITHOUT renormalising, so t stays a world distance.
            Vector3 o = tr.InverseTransformPoint(ray.origin) - box.center;
            Vector3 d = tr.InverseTransformVector(ray.direction);
            Vector3 half = box.size * 0.5f;

            float tMin = 0f, tMax = float.MaxValue;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(d[a]) < 1e-8f)
                {
                    if (o[a] < -half[a] || o[a] > half[a]) { t = 0f; return false; }
                    continue;
                }
                float inv = 1f / d[a];
                float t1 = (-half[a] - o[a]) * inv;
                float t2 = (half[a] - o[a]) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) { t = 0f; return false; }
            }
            t = tMin;
            return true;
        }

        private static bool RaySphere(SphereCollider s, Ray ray, out float t)
        {
            Vector3 c = s.transform.TransformPoint(s.center);
            Vector3 sc = s.transform.lossyScale;
            float r = s.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
            return RaySphereRaw(c, r, ray, out t);
        }

        private static bool RaySphereRaw(Vector3 c, float r, Ray ray, out float t)
        {
            Vector3 oc = ray.origin - c;
            float b = Vector3.Dot(oc, ray.direction);
            float cc = oc.sqrMagnitude - r * r;
            float disc = b * b - cc;
            t = 0f;
            if (disc < 0f) return false;
            t = -b - Mathf.Sqrt(disc);
            if (t < 0f) t = -b + Mathf.Sqrt(disc);
            return t >= 0f;
        }

        private static bool RayCapsule(CapsuleCollider cap, Ray ray, out float t)
        {
            var tr = cap.transform;
            Vector3 sc = tr.lossyScale;
            Vector3 axis;
            float axisScale, radiusScale;
            switch (cap.direction)
            {
                case 0: axis = Vector3.right; axisScale = Mathf.Abs(sc.x); radiusScale = Mathf.Max(Mathf.Abs(sc.y), Mathf.Abs(sc.z)); break;
                case 2: axis = Vector3.forward; axisScale = Mathf.Abs(sc.z); radiusScale = Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y)); break;
                default: axis = Vector3.up; axisScale = Mathf.Abs(sc.y); radiusScale = Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z)); break;
            }

            float r = cap.radius * radiusScale;
            float halfSeg = Mathf.Max(0f, cap.height * axisScale * 0.5f - r);
            Vector3 c = tr.TransformPoint(cap.center);
            Vector3 dirW = tr.TransformDirection(axis).normalized;
            Vector3 a = c - dirW * halfSeg;
            Vector3 b = c + dirW * halfSeg;

            // Closest approach between the ray and the capsule's core segment.
            ClosestRaySegment(ray, a, b, out float rayT, out Vector3 segPoint);
            Vector3 rayPoint = ray.origin + ray.direction * rayT;
            t = rayT;
            if ((rayPoint - segPoint).sqrMagnitude > r * r) return false;

            // Refine to the actual surface entry: the sphere at the closest segment point.
            if (RaySphereRaw(segPoint, r, ray, out float entry)) t = entry;
            return true;
        }

        private static void ClosestRaySegment(Ray ray, Vector3 a, Vector3 b, out float rayT, out Vector3 segPoint)
        {
            Vector3 d1 = ray.direction;
            Vector3 d2 = b - a;
            Vector3 r = ray.origin - a;
            float aa = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, u;

            if (e < 1e-8f)
            {
                s = Mathf.Max(0f, -Vector3.Dot(d1, r) / aa);
                u = 0f;
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                float bb = Vector3.Dot(d1, d2);
                float denom = aa * e - bb * bb;
                s = denom > 1e-8f ? Mathf.Max(0f, (bb * f - c * e) / denom) : 0f;
                u = Mathf.Clamp01((bb * s + f) / e);
                s = Mathf.Max(0f, (bb * u - c) / aa);
            }

            rayT = s;
            segPoint = a + d2 * u;
        }
    }
}