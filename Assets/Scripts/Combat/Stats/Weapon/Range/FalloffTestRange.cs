using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using Combat.Stats;
using Combat.Weapons;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Combat.Diagnostics
{
    // Test lane: T-posed dummies auto-placed around the held weapon's falloff band
    [ExecuteAlways]
    public class FalloffTestRange : MonoBehaviour
    {
        [Header("Dummy")]
        [Tooltip("Enemy prefab. AI, navigation and animation are disabled on spawn.")]
        [SerializeField] private GameObject dummyPrefab;
        [Tooltip("max_health stat, scaled up so dummies never die.")]
        [SerializeField] private StatDefinitionSO maxHealthStat;
        [SerializeField] private float healthMultiplier = 1000f;

        [Header("Weapon")]
        [Tooltip("Band source in Edit mode, or in Play when nothing is held.")]
        [SerializeField] private WeaponSO previewWeapon;

        [Header("Layout")]
        [SerializeField] private float lateralSpacing = 1.25f;
        [SerializeField] private float minGap = 0.75f;
        [SerializeField] private float labelHeight = 2.4f;
        [SerializeField] private float markerWidth = 8f;
        [SerializeField] private bool showMarkers = true;
        [Tooltip("World label size per metre of camera distance (keeps screen size constant).")]
        [SerializeField] private float worldLabelScale = 0.06f;

        [Header("HUD")]
        [SerializeField] private bool showHud = true;
        [SerializeField] private Key hudToggleKey = Key.F3;
        [SerializeField] private float hitFlashTime = 0.5f;

        private const string LaneName = "__FalloffLane";

        private sealed class Dummy
        {
            public CombatantStats Stats;
            public float Distance;
            public float LastHealth;
            public float LastHit = -1f;
            public float HitTime = -10f;
            public TextMeshPro Label;
        }

        private readonly List<Dummy> dummies = new List<Dummy>();
        private readonly List<float> distances = new List<float>();
        private Transform lane;
        private Material lineMaterial;
        private WeaponLoadout loadout;
        private WeaponArchetypeSO builtArchetype;
        private float builtRange = -1f;
        private bool rebuildRequested;
        private string weaponName = "";
        private float liveRange;
        private WeaponArchetypeSO liveArchetype;
        private GUIStyle cellStyle, headerStyle, boxStyle;

        private void OnEnable() => RequestRebuild();
        private void OnDisable()
        {
            Clear();
            if (outlineMaterial != null) DestroyObj(outlineMaterial);
            if (lineMaterial != null) DestroyObj(lineMaterial);
            outlineMaterial = null;
            lineMaterial = null;
        }

        private void OnValidate() => RequestRebuild();

        [ContextMenu("Rebuild Lane")]
        public void RequestRebuild()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorApplication.delayCall -= EditorRebuild;
                EditorApplication.delayCall += EditorRebuild;
                return;
            }
#endif
            rebuildRequested = true;
        }

#if UNITY_EDITOR
        private void EditorRebuild()
        {
            if (this == null || !isActiveAndEnabled || Application.isPlaying) return;
            Rebuild();
        }
#endif

        private void Update()
        {
            if (!Application.isPlaying) return;

            GetBand(out var archetype, out float range);
            if (rebuildRequested || archetype != builtArchetype || Mathf.Abs(range - builtRange) > 0.01f)
            {
                rebuildRequested = false;
                Rebuild();
            }

            if (Keyboard.current != null && Keyboard.current[hudToggleKey].wasPressedThisFrame)
                showHud = !showHud;

            liveArchetype = archetype;
            liveRange = range;

            TrackHits();
            UpdateLabels(archetype, range);
        }

        // Live held weapon in Play, preview weapon otherwise
        private void GetBand(out WeaponArchetypeSO archetype, out float range)
        {
            if (Application.isPlaying)
            {
                if (loadout == null) loadout = FindFirstObjectByType<WeaponLoadout>();
                var ds = loadout != null && loadout.Active != null ? loadout.Active.DamageSource : null;
                if (ds != null && ds.Weapon != null && ds.Weapon.archetype != null)
                {
                    archetype = ds.Weapon.archetype;
                    range = ds.ResolvedRange;
                    weaponName = ds.Weapon.displayName;
                    return;
                }
            }

            weaponName = previewWeapon != null ? previewWeapon.displayName + " (preview)" : "";
            archetype = previewWeapon != null ? previewWeapon.archetype : null;
            range = archetype != null ? Mathf.Clamp(archetype.range + previewWeapon.rangeDelta, 0f, 100f) : 0f;
        }

        // ------------------------------------------------------------ build

        private void Rebuild()
        {
            Clear();
            GetBand(out var a, out float range);
            builtArchetype = a;
            builtRange = range;
            if (a == null || dummyPrefab == null) return;

            BuildDistances(a, range);

            var laneGo = new GameObject(LaneName) { hideFlags = HideFlags.DontSave };
            laneGo.SetActive(false);   // no Awake on spawned AI until stripped
            lane = laneGo.transform;
            lane.SetParent(transform, false);

            Vector3 fwd = transform.forward, right = transform.right;
            for (int i = 0; i < distances.Count; i++)
            {
                float d = distances[i];
                float side = (i % 2 == 0 ? -1f : 1f) * lateralSpacing;
                Vector3 pos = transform.position + fwd * d + right * side;
                var go = Instantiate(dummyPrefab, pos, Quaternion.LookRotation(-fwd), lane);
                go.name = $"Dummy_{d:0.0}m";
                StripToDummy(go);

                dummies.Add(new Dummy
                {
                    Stats = go.GetComponent<CombatantStats>(),
                    Distance = d,
                    Label = MakeLabel(go.transform, i == 0)
                });
            }

            if (showMarkers)
            {
                Marker(WeaponFalloff.Start(a, range, 0f), new Color(1f, 0.6f, 0.2f), "hip start");
                Marker(WeaponFalloff.End(a, range, 0f), new Color(1f, 0.3f, 0.2f), "hip end");
                Marker(WeaponFalloff.Start(a, range, 1f), new Color(0.3f, 0.8f, 1f), "ADS start");
                Marker(WeaponFalloff.End(a, range, 1f), new Color(0.3f, 0.4f, 1f), "ADS end");
            }

            laneGo.SetActive(true);

            // One shared outlined material; per-label outline would clone and leak
            foreach (var d in dummies)
            {
                if (d.Label == null) continue;
                var mat = GetOutlineMaterial(d.Label);
                if (mat != null) d.Label.fontSharedMaterial = mat;
            }

            if (Application.isPlaying) MakeImmortal();
            UpdateLabels(a, range);
        }

        // Reference before any falloff, then hip/ADS start, mid-drop, end, past end
        private void BuildDistances(WeaponArchetypeSO a, float range)
        {
            distances.Clear();
            float hs = WeaponFalloff.Start(a, range, 0f), he = WeaponFalloff.End(a, range, 0f);
            float s = WeaponFalloff.Start(a, range, 1f), e = WeaponFalloff.End(a, range, 1f);

            var raw = new List<float>
            {
                0.5f * Mathf.Min(hs, s),
                hs, (hs + he) * 0.5f, he,
                s, (s + e) * 0.5f, e,
                1.25f * Mathf.Max(he, e)
            };
            raw.Sort();

            foreach (float d in raw)
                if (d > 0.5f && (distances.Count == 0 || d - distances[distances.Count - 1] >= minGap))
                    distances.Add(d);
        }

        // Keeps hitboxes, stats, health and statuses; everything else off
        private static void StripToDummy(GameObject go)
        {
            foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
            {
                if (b is EnemyHitbox || b is CombatantStats || b is CombatantHealth
                    || b is Combat.Status.StatusReceiver)
                    continue;
                b.enabled = false;
            }
        }

        private void MakeImmortal()
        {
            foreach (var d in dummies)
            {
                if (d.Stats == null || d.Stats.Container == null) continue;
                if (maxHealthStat != null)
                    d.Stats.Container.AddModifier(new StatModifier(
                        maxHealthStat, StatResolver.MULTIPLICATIVE, healthMultiplier - 1f), this);
                d.Stats.Heal(d.Stats.MaxHealth);
                d.LastHealth = d.Stats.CurrentHealth;
            }
        }

        private TextMeshPro MakeLabel(Transform parent, bool reference)
        {
            var go = new GameObject("Label") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * labelHeight;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = 4f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = reference ? new Color(0.6f, 1f, 0.6f) : Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
            return tmp;
        }

        private void Marker(float distance, Color color, string text)
        {
            if (distance <= 0f || float.IsInfinity(distance)) return;

            if (lineMaterial == null)
                lineMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };

            var go = new GameObject($"Marker_{text}") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(lane, false);
            Vector3 c = transform.position + transform.forward * distance + Vector3.up * 0.02f;
            Vector3 half = transform.right * (markerWidth * 0.5f);

            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, c - half);
            lr.SetPosition(1, c + half);
            lr.startWidth = lr.endWidth = 0.06f;
            lr.startColor = lr.endColor = color;

            var label = new GameObject("Text") { hideFlags = HideFlags.DontSave };
            label.transform.SetParent(go.transform, false);
            label.transform.position = c + half + Vector3.up * 0.3f;
            var tmp = label.AddComponent<TextMeshPro>();
            tmp.text = $"{text} {distance:0.0}m";
            tmp.fontSize = 2f;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
        }

        private Material outlineMaterial;

        private Material GetOutlineMaterial(TextMeshPro label)
        {
            if (outlineMaterial != null) return outlineMaterial;
            var font = label.font != null ? label.font : TMP_Settings.defaultFontAsset;
            if (font == null || font.material == null) return null;

            outlineMaterial = new Material(font.material) { hideFlags = HideFlags.DontSave };
            outlineMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f);
            outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            return outlineMaterial;
        }

        private void Clear()
        {
            dummies.Clear();
            if (lane != null) DestroyObj(lane.gameObject);
            lane = null;

            // Leftovers from a previous session or domain reload
            for (int i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name == LaneName) DestroyObj(transform.GetChild(i).gameObject);
        }

        private static void DestroyObj(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // ------------------------------------------------------------ runtime

        // Health delta per frame = damage that frame (pellets of one shot combine)
        private void TrackHits()
        {
            foreach (var d in dummies)
            {
                if (d.Stats == null) continue;
                float hp = d.Stats.CurrentHealth;
                float taken = d.LastHealth - hp;
                if (taken > 0.001f)
                {
                    d.LastHit = taken;
                    d.HitTime = Time.time;
                    d.Stats.Heal(d.Stats.MaxHealth);
                    hp = d.Stats.CurrentHealth;
                }
                d.LastHealth = hp;
            }
        }

        // World labels: distance only, constant screen size
        private void UpdateLabels(WeaponArchetypeSO a, float range)
        {
            var cam = Camera.main;
            bool live = cam != null && Application.isPlaying;
            Vector3 eye = live ? cam.transform.position : transform.position;

            for (int i = 0; i < dummies.Count; i++)
            {
                var d = dummies[i];
                if (d.Label == null) continue;

                d.Label.text = i == 0 ? $"{d.Distance:0.0} m  REF" : $"{d.Distance:0.0} m";

                var t = d.Label.transform;
                t.rotation = live ? cam.transform.rotation : Quaternion.LookRotation(transform.forward);
                float dist = Vector3.Distance(eye, t.position);
                t.localScale = Vector3.one * Mathf.Max(0.3f, dist * worldLabelScale);
            }
        }

        // ------------------------------------------------------------ HUD

        private void OnGUI()
        {
            if (!Application.isPlaying || !showHud || dummies.Count == 0) return;
            EnsureStyles();

            const float rowH = 22f, pad = 10f;
            float[] cols = { 80f, 80f, 80f, 90f, 90f };
            float width = pad * 2f;
            foreach (float c in cols) width += c;
            float height = pad * 2f + rowH * (dummies.Count + 3);

            var area = new Rect(Screen.width - width - 16f, 16f, width, height);
            GUI.Box(area, GUIContent.none, boxStyle);

            float x0 = area.x + pad, y = area.y + pad;
            var a = liveArchetype;

            GUI.Label(new Rect(x0, y, width, rowH),
                $"{weaponName}   range {liveRange:0}   [{hudToggleKey}] hide", headerStyle);
            y += rowH;
            GUI.Label(new Rect(x0, y, width, rowH),
                $"hip {WeaponFalloff.Start(a, liveRange, 0f):0.0}-{WeaponFalloff.End(a, liveRange, 0f):0.0}m   " +
                $"ADS {WeaponFalloff.Start(a, liveRange, 1f):0.0}-{WeaponFalloff.End(a, liveRange, 1f):0.0}m   " +
                $"floor {(a != null ? a.minDamagePercent : 0f):P0}", cellStyle);
            y += rowH;

            string[] head = { "dist", "exp hip", "exp ADS", "last hit", "of ref" };
            Row(x0, y, cols, head, headerStyle);
            y += rowH;

            float reference = dummies[0].LastHit;
            for (int i = 0; i < dummies.Count; i++)
            {
                var d = dummies[i];
                bool flash = Time.time - d.HitTime < hitFlashTime;
                if (flash)
                    GUI.DrawTexture(new Rect(area.x + 2f, y, area.width - 4f, rowH), Texture2D.whiteTexture,
                        ScaleMode.StretchToFill, true, 0f, new Color(1f, 0.85f, 0.2f, 0.25f), 0f, 0f);

                string ofRef = i == 0 ? "REF" : (d.LastHit >= 0f && reference > 0f ? $"{d.LastHit / reference:P0}" : "-");
                Row(x0, y, cols, new[]
                {
                    $"{d.Distance:0.0} m",
                    $"{WeaponFalloff.Multiplier(a, liveRange, d.Distance, 0f):P0}",
                    $"{WeaponFalloff.Multiplier(a, liveRange, d.Distance, 1f):P0}",
                    d.LastHit >= 0f ? $"{d.LastHit:0.0}" : "-",
                    ofRef
                }, cellStyle);
                y += rowH;
            }
        }

        private static void Row(float x, float y, float[] cols, string[] cells, GUIStyle style)
        {
            for (int c = 0; c < cols.Length; c++)
            {
                GUI.Label(new Rect(x, y, cols[c], 22f), cells[c], style);
                x += cols[c];
            }
        }

        private void EnsureStyles()
        {
            if (cellStyle != null) return;

            cellStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            cellStyle.normal.textColor = Color.white;
            headerStyle = new GUIStyle(cellStyle) { fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);

            var bg = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            bg.Apply();
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = bg;
        }
    }
}