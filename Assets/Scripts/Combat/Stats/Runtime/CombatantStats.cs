using UnityEngine;
using Combat.Stats;

namespace Combat.Stats
{
    // Owns an entity's PLAYER-SCOPE StatContainer (crit_chance, crit_damage,
    // global_damage, and other character-wide stats). Lives on the player now, and
    // on any entity that can crit / carry player-style stats later (enemies, allies)
    // — hence "Combatant", not "Player".
    //
    // The container is fully dynamic: perks/buffs/affixes push modifiers onto it at
    // runtime; reads are cached + version-invalidated. Its BASE values (inherent
    // crit chance/damage, etc.) are authored here.
    //
    // Weapon affixes that buff the wielder's crit register modifiers onto THIS
    // container (with the affix as owner, removed on unequip) — the omnidirectional
    // model. That authoring is a later phase; the container is ready for it now.
    public class CombatantStats : MonoBehaviour
    {
        [System.Serializable]
        public struct BaseStat
        {
            public StatDefinitionSO stat;
            public float baseValue;
        }

        [Tooltip("Inherent base values for this combatant's player-scope stats " +
                 "(e.g. crit_chance 0.05, crit_damage 0.5). Modifiers layer on top.")]
        [SerializeField] private BaseStat[] baseStats;

        private StatContainer container;
        public StatContainer Container => container;

        private void Awake()
        {
            container = new StatContainer();
            if (baseStats != null)
                foreach (var b in baseStats)
                    if (b.stat != null)
                        container.SetBase(b.stat, b.baseValue);
        }

        // Convenience pass-throughs (optional).
        public float Resolve(StatDefinitionSO stat) => container != null ? container.Resolve(stat) : (stat != null ? stat.defaultValue : 0f);
    }
}
