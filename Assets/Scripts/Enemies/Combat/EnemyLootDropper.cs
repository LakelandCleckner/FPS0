using UnityEngine;
using Combat.Loot;

namespace Combat.Enemies
{
    // Rolls this enemy's loot table on death and drops the result at the body.
    [RequireComponent(typeof(CombatantHealth))]
    public class EnemyLootDropper : MonoBehaviour
    {
        [SerializeField] private LootTableSO lootTable;
        [Tooltip("Where the drop spawns. Empty = this object.")]
        [SerializeField] private Transform dropPoint;

        [Tooltip("Spread of extra drops around the drop point (m).")]
        [SerializeField] private float extraDropSpread = 0.75f;

        private CombatantHealth health;
        private LootTableSO tableOverride;
        private bool guaranteedDrop;
        private int extraDrops;

        private void Awake() => health = GetComponent<CombatantHealth>();

        // Re-subscribed each life for pooled enemies.
        private void OnEnable() { if (health != null) health.OnDeath += HandleDeath; }
        private void OnDisable() { if (health != null) health.OnDeath -= HandleDeath; }

        // Table override (null = authored), guaranteed first drop and extra rolls, set by the enemy definition.
        public void Configure(LootTableSO table, bool guaranteed, int extra)
        {
            tableOverride = table;
            guaranteedDrop = guaranteed;
            extraDrops = Mathf.Max(0, extra);
        }

        private void HandleDeath()
        {
            var table = tableOverride != null ? tableOverride : lootTable;
            if (table == null) return;
            var at = dropPoint != null ? dropPoint : transform;
            table.SpawnDrop(at.position, guaranteedDrop);
            for (int i = 0; i < extraDrops; i++)
            {
                Vector2 o = Random.insideUnitCircle * extraDropSpread;
                table.SpawnDrop(at.position + new Vector3(o.x, 0f, o.y));
            }
        }
    }
}
