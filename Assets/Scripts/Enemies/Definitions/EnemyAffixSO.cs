using UnityEngine;

namespace Combat.Enemies
{
    // An ability recipe. Shared asset; per-enemy state lives in the runtime it creates.
    public abstract class EnemyAffixSO : ScriptableObject
    {
        public string displayName = "";
        [TextArea] public string description = "";

        public abstract EnemyAffix CreateRuntime();
    }

    // Per-enemy affix state and hooks. Reset in OnSpawn; it is reused across pooled lives.
    public abstract class EnemyAffix
    {
        public virtual void OnSpawn(EnemyDefinitionApplier enemy) { }
        public virtual void OnHealthChanged(EnemyDefinitionApplier enemy, float current, float max) { }
        public virtual void OnDeath(EnemyDefinitionApplier enemy) { }
        public virtual void OnDespawn(EnemyDefinitionApplier enemy) { }
        public virtual void OnRemoved(EnemyDefinitionApplier enemy) { }
    }
}
