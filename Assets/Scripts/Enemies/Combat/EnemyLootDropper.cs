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

        private CombatantHealth health;

        private void Awake() => health = GetComponent<CombatantHealth>();

        // Re-subscribed each life for pooled enemies.
        private void OnEnable() { if (health != null) health.OnDeath += HandleDeath; }
        private void OnDisable() { if (health != null) health.OnDeath -= HandleDeath; }

        private void HandleDeath()
        {
            if (lootTable == null) return;
            var at = dropPoint != null ? dropPoint : transform;
            lootTable.SpawnDrop(at.position);
        }
    }
}
