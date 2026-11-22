#if UNITY_EDITOR || STATUS_DEBUG
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Enemies;
using Combat.Spawning;

namespace Combat.Testing
{
    // THROWAWAY spawn tester. G/Shift+G cycle the archetype, N/Shift+N cycle a layer (element, shield).
    // H/J/K/L spawn it as Minor/Major/Elite/Miniboss (Shift = group); comma and period repeat the last rank.
    // B spawns the current squad asset, Shift+B cycles squads.
    // Spawns in front of this object (or the camera) so they land in view.
    public class SpawnTestProbe : MonoBehaviour
    {
        [SerializeField] private EnemySpawner spawner;

        [Header("Enemies")]
        [Tooltip("Archetypes to cycle. In the editor every archetype in the project is used instead.")]
        [SerializeField] private List<EnemyArchetypeSO> archetypes = new List<EnemyArchetypeSO>();
        [Tooltip("Layers to cycle (element, shield). In the editor every non-tier modifier is used instead.")]
        [SerializeField] private List<EnemyModifierSO> layers = new List<EnemyModifierSO>();
        [Tooltip("Tier layers for Major, Elite and Miniboss. In the editor they come from the project.")]
        [SerializeField] private EnemyModifierSO[] tiers = new EnemyModifierSO[3];
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

        private static readonly Key[] RankKeys = { Key.H, Key.J, Key.K, Key.L };

        private int archIndex;
        private int layerIndex;   // 0 = no layer
        private int lastRank;
        private int squadIndex;
        private string lastMessage = "";
        private PlayerInputRouter input;
        private GUIStyle style;

        private void Start() => Load();

        private void Update()
        {
            if (input == null) input = PlayerInputRouter.For(this);
            if (input != null)
            {
                if (input.Debug.ToggleSpawnOverlay.WasPressedThisFrame()) showOverlay = !showOverlay;
                if (input.Debug.SpawnOne.WasPressedThisFrame()) SpawnRank(lastRank, false);
                if (input.Debug.SpawnGroup.WasPressedThisFrame()) SpawnRank(lastRank, true);
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

            if (archetypes.Count == 0) return;

            if (kb[Key.G].wasPressedThisFrame) { archIndex = Step(archIndex, archetypes.Count, shift); lastMessage = ""; }
            if (kb[Key.N].wasPressedThisFrame) { layerIndex = Step(layerIndex, layers.Count + 1, shift); lastMessage = ""; }

            for (int r = 0; r < RankKeys.Length; r++)
                if (kb[RankKeys[r]].wasPressedThisFrame) SpawnRank(r, shift);
        }

        private static int Step(int i, int count, bool back) => count == 0 ? 0 : (i + (back ? count - 1 : 1)) % count;

        private EnemySpawnDef Def(int rank)
        {
            var tier = rank > 0 ? tiers[rank - 1] : null;
            var layer = layerIndex > 0 ? layers[layerIndex - 1] : null;
            return new EnemySpawnDef(null, archetypes[archIndex], EnemyTierLayers.Compose(tier, layer != null ? new[] { layer } : null));
        }

        private void SpawnRank(int rank, bool group)
        {
            if (archetypes.Count == 0) return;
            if (rank > 0 && tiers[rank - 1] == null) { lastMessage = $"No {EnemyTierLayers.RankNames[rank]} tier layer"; return; }
            lastRank = rank;
            var def = Def(rank);
            if (group)
            {
                var list = spawner.SpawnGroup(def, groupCount, SpawnPoint(), groupRadius);
                Report(list.Count > 0, def.DisplayName, list.Count);
            }
            else Report(spawner.Spawn(def, SpawnPoint()) != null, def.DisplayName, 1);
        }

        private void Report(bool ok, string what, int count)
        {
            lastMessage = ok ? $"Spawned {count}x {what}" : $"FAILED to spawn {what}";
            if (!ok) Debug.Log("[SpawnTest] " + lastMessage);
        }

        // ---- lists ----

        private void Load()
        {
#if UNITY_EDITOR
            archetypes = FindAll<EnemyArchetypeSO>();
            layers = FindAll<EnemyModifierSO>().FindAll(m => m.kind != EnemyModifierKind.Tier);
            for (int r = 0; r < tiers.Length; r++) tiers[r] = EnemyTierLayers.For((EnemyRank)(r + 1));
            squads = FindAll<Combat.AI.EnemySquadSO>();
#endif
            archetypes.RemoveAll(a => a == null);
            layers.RemoveAll(m => m == null);
            squads.RemoveAll(s => s == null);
            archIndex = Mathf.Clamp(archIndex, 0, Mathf.Max(0, archetypes.Count - 1));
            layerIndex = Mathf.Clamp(layerIndex, 0, layers.Count);
            squadIndex = Mathf.Clamp(squadIndex, 0, Mathf.Max(0, squads.Count - 1));
        }

#if UNITY_EDITOR
        private static List<T> FindAll<T>() where T : Object
        {
            var list = new List<T>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) list.Add(a);
            }
            list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            return list;
        }
#endif

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
            if (!showOverlay || archetypes.Count == 0) return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };

            var sb = new StringBuilder();
            sb.Append("<b>[G] ").Append(archetypes[archIndex].StatSourceLabel).Append("</b>  (").Append(archIndex + 1).Append('/').Append(archetypes.Count).Append(")\n");
            sb.Append("[N] ").Append(layerIndex > 0 ? layers[layerIndex - 1].name : "No layer").Append('\n');
            for (int r = 0; r < 4; r++)
            {
                sb.Append('[').Append(RankKeys[r]).Append("] ").Append(EnemyTierLayers.RankNames[r]);
                if (r > 0 && tiers[r - 1] == null) sb.Append(" -");
                sb.Append(r < 3 ? "   " : "\n");
            }
            sb.Append("Shift = x").Append(groupCount);
            if (squads.Count > 0) sb.Append("\n[B] ").Append(squads[squadIndex].Label).Append("  (Shift+B next)");
            if (lastMessage.Length > 0) sb.Append("\n").Append(lastMessage);

            float h = 78f + (squads.Count > 0 ? 18f : 0f) + (lastMessage.Length > 0 ? 18f : 0f);
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
