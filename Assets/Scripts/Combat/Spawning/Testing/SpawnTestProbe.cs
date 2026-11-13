#if UNITY_EDITOR || STATUS_DEBUG
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Enemies;
using Combat.Spawning;

namespace Combat.Testing
{
    // THROWAWAY spawn tester. Comma spawns one, period spawns a group of the id or variant below.
    // H/J/K/L spawn the current type as Minor/Major/Elite/Miniboss (Shift = group), G/Shift+G cycle the type.
    // B spawns the current squad asset, Shift+B cycles squads.
    // Spawns in front of this object (or the camera) so they land in view.
    public class SpawnTestProbe : MonoBehaviour
    {
        [SerializeField] private EnemySpawner spawner;

        [Tooltip("Type id to spawn. Matches the id registered on the EnemySpawner.")]
        [SerializeField] private string enemyId = "basic";
        [Tooltip("When set, spawns this variant instead of the type id.")]
        [SerializeField] private EnemyVariantSO variant;

        [Header("Rank Keys")]
        [Tooltip("Variants for the rank keys. In the editor every variant in the project is used instead.")]
        [SerializeField] private List<EnemyVariantSO> variants = new List<EnemyVariantSO>();
        [SerializeField] private bool showOverlay = true;

        [Header("Squad Key")]
        [Tooltip("Squads for the B key. In the editor every squad asset in the project is used instead.")]
        [SerializeField] private List<Combat.AI.EnemySquadSO> squads = new List<Combat.AI.EnemySquadSO>();

        [Header("Placement")]
        [Tooltip("How far in front of this object to spawn.")]
        [SerializeField] private float spawnDistance = 6f;
        [Tooltip("Scatter radius for groups.")]
        [SerializeField] private float groupRadius = 3f;
        [SerializeField] private int groupCount = 3;

        // Variants sharing an archetype and non-tier modifiers, one slot per rank.
        private class SpawnType
        {
            public string Label;
            public readonly EnemyVariantSO[] ByRank = new EnemyVariantSO[4];
        }

        private static readonly Key[] RankKeys = { Key.H, Key.J, Key.K, Key.L };
        private static readonly string[] RankNames = { "Minor", "Major", "Elite", "Miniboss" };

        private readonly List<SpawnType> types = new List<SpawnType>();
        private int typeIndex;
        private int squadIndex;
        private string lastMessage = "";
        private PlayerInputRouter input;
        private GUIStyle style;

        private void Start() => BuildTypes();

        private void Update()
        {
            if (input == null) input = PlayerInputRouter.For(this);
            if (input != null)
            {
                if (input.Debug.SpawnOne.WasPressedThisFrame())
                    Report(variant != null ? spawner.Spawn(variant, SpawnPoint()) != null : spawner.Spawn(enemyId, SpawnPoint()) != null, Label, 1);

                if (input.Debug.SpawnGroup.WasPressedThisFrame())
                {
                    var list = variant != null
                        ? spawner.SpawnGroup(variant, groupCount, SpawnPoint(), groupRadius)
                        : spawner.SpawnGroup(enemyId, groupCount, SpawnPoint(), groupRadius);
                    Report(list.Count > 0, Label, list.Count);
                }
            }

            var kb = Keyboard.current;
            if (kb == null) return;
            bool shift = kb.shiftKey.isPressed;

            if (kb[Key.B].wasPressedThisFrame && squads.Count > 0)
            {
                if (shift) { squadIndex = (squadIndex + 1) % squads.Count; lastMessage = "Squad: " + squads[squadIndex].Label; }
                else
                {
                    var s = squads[squadIndex];
                    var list = spawner.SpawnSquad(s, SpawnPoint());
                    Report(list.Count > 0, s.Label, list.Count);
                }
            }

            if (types.Count == 0) return;

            if (kb[Key.G].wasPressedThisFrame)
            {
                typeIndex = (typeIndex + (shift ? types.Count - 1 : 1)) % types.Count;
                lastMessage = "";
            }

            for (int r = 0; r < RankKeys.Length; r++)
                if (kb[RankKeys[r]].wasPressedThisFrame) SpawnRank(r, shift);
        }

        private void SpawnRank(int rank, bool group)
        {
            var t = types[typeIndex];
            var v = t.ByRank[rank];
            if (v == null) { lastMessage = $"No {RankNames[rank]} {t.Label}"; return; }

            if (group)
            {
                var list = spawner.SpawnGroup(v, groupCount, SpawnPoint(), groupRadius);
                Report(list.Count > 0, v.DisplayName, list.Count);
            }
            else Report(spawner.Spawn(v, SpawnPoint()) != null, v.DisplayName, 1);
        }

        private void Report(bool ok, string what, int count)
        {
            lastMessage = ok ? $"Spawned {count}x {what}" : $"FAILED to spawn {what}";
            if (!ok) Debug.Log("[SpawnTest] " + lastMessage);
        }

        private string Label => variant != null ? variant.DisplayName : enemyId;

        // ---- types ----

        private void BuildTypes()
        {
            var all = new List<EnemyVariantSO>(variants);
#if UNITY_EDITOR
            all.Clear();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:EnemyVariantSO"))
            {
                var v = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyVariantSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (v != null) all.Add(v);
            }
#endif
#if UNITY_EDITOR
            squads.Clear();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:EnemySquadSO"))
            {
                var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Combat.AI.EnemySquadSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) squads.Add(s);
            }
#endif
            squads.RemoveAll(s => s == null);
            squads.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            squadIndex = Mathf.Clamp(squadIndex, 0, Mathf.Max(0, squads.Count - 1));

            all.RemoveAll(v => v == null || v.archetype == null);
            all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            types.Clear();
            var byKey = new Dictionary<string, SpawnType>();
            foreach (var v in all)
            {
                int rank = RankOf(v);
                if (rank < 0 || rank > 3) continue;

                string key = TypeLabel(v);
                if (!byKey.TryGetValue(key, out var t))
                {
                    t = new SpawnType { Label = key };
                    byKey[key] = t;
                    types.Add(t);
                }
                if (t.ByRank[rank] == null) t.ByRank[rank] = v;
            }
            types.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
            typeIndex = Mathf.Clamp(typeIndex, 0, Mathf.Max(0, types.Count - 1));
        }

        private static int RankOf(EnemyVariantSO v)
        {
            int rank = (int)v.archetype.rank;
            foreach (var m in v.modifiers)
                if (m != null && (int)m.raiseRankTo > rank) rank = (int)m.raiseRankTo;
            return rank;
        }

        // Archetype name with every non-tier prefix, e.g. "Volatile Alien Zombie".
        private static string TypeLabel(EnemyVariantSO v)
        {
            var sb = new StringBuilder();
            foreach (var m in v.modifiers)
                if (m != null && m.kind != EnemyModifierKind.Tier && !string.IsNullOrEmpty(m.namePrefix))
                    sb.Append(m.namePrefix).Append(' ');
            sb.Append(v.archetype.StatSourceLabel);
            return sb.ToString();
        }

        // ---- placement ----

        // A point in front of this object on the ground plane. Uses the object's
        // forward; if this is on the player/camera, enemies appear ahead of you.
        private Vector3 SpawnPoint()
        {
            Vector3 p = transform.position + transform.forward * spawnDistance;
            p.y = transform.position.y;   // keep on the caster's height; NavMesh will settle it
            return p;
        }

        private void OnGUI()
        {
            if (!showOverlay || types.Count == 0) return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };

            var t = types[typeIndex];
            var sb = new StringBuilder();
            sb.Append("<b>[G] ").Append(t.Label).Append("</b>  (").Append(typeIndex + 1).Append('/').Append(types.Count).Append(")\n");
            for (int r = 0; r < 4; r++)
            {
                sb.Append('[').Append(RankKeys[r]).Append("] ").Append(RankNames[r]);
                if (t.ByRank[r] == null) sb.Append(" -");
                sb.Append(r < 3 ? "   " : "\n");
            }
            sb.Append("Shift = x").Append(groupCount);
            if (squads.Count > 0) sb.Append("\n[B] ").Append(squads[squadIndex].Label).Append("  (Shift+B next)");
            if (lastMessage.Length > 0) sb.Append("\n").Append(lastMessage);

            float h = 60f + (squads.Count > 0 ? 18f : 0f) + (lastMessage.Length > 0 ? 18f : 0f);
            GUI.Box(new Rect(10, 10, 360, h), sb.ToString(), style);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = transform.position + transform.forward * spawnDistance;
            c.y = transform.position.y;
            Gizmos.DrawWireSphere(c, groupRadius);
        }
    }
}
#endif
