using UnityEngine;
using Combat.Stats;

namespace Combat.Weapons
{
    // Central reference to the StatDefinitionSO assets the weapon system uses, so
    // code can name stats without hard string lookups. Assign the assets once on a
    // single instance (a ScriptableObject) and reference it where weapon stats are
    // built/queried.
    //
    // Phase 2e: used by the parallel-comparison harness to map StatBlock fields to
    // their StatDefinitionSO. Later phases use it wherever the weapon populates its
    // container.
    [CreateAssetMenu(fileName = "WeaponStatKeys", menuName = "Combat/Stats/Weapon Stat Keys")]
    public class WeaponStatKeys : ScriptableObject
    {
        public StatDefinitionSO weaponDamage;   // id "weapon_damage"  (ScaledBase)
        public StatDefinitionSO critDamage;     // id "crit_damage"    (Pool)
        public StatDefinitionSO critChance;     // id "crit_chance"    (Pool)
        public StatDefinitionSO globalDamage;   // id "global_damage"  (see note)
        public StatDefinitionSO rpm;            // id "rpm"            (ScaledBase)
        public StatDefinitionSO magazineSize;   // id "magazine_size"  (ScaledBase)
        public StatDefinitionSO reloadTime;     // id "reload_time"    (ScaledBase)
        [Header("Handling")]
        public StatDefinitionSO handling;    // "handling"
        public StatDefinitionSO equipTime;   // "equip_time"
        public StatDefinitionSO stowTime;    // "stow_time"


        // NOTE (global_damage representation): the legacy StatBlock stores this as a
        // raw MULTIPLIER (1.0 = neutral). For a 1:1 parallel with the StatBlock in
        // Phase 2e, global_damage should be ScaledBase with base 1.0 so it compares
        // equal. Whether it BECOMES a bonus Pool (base 0) is a 2f/2g damage-math
        // decision, not a 2e concern.
    }
}
