using UnityEngine;
using Combat.Stats;

namespace Combat.Weapons
{
    // Stat definitions the loadout resolves off a weapon's own container.
    //
    // These are WEAPON-home stats: a weapon's equip and stow times belong to the
    // weapon, so a stowed weapon still has them and armour that modifies them
    // registers onto each weapon's container (with the armour piece as the modifier
    // Owner, so RemoveAllFromOwner cleans up on unequip).
    [CreateAssetMenu(fileName = "LoadoutStatKeys", menuName = "Combat/Stats/Loadout Stat Keys")]
    public class LoadoutStatKeys : ScriptableObject
    {
        [Tooltip("Seconds to bring this weapon up. Also drives sprint-exit ready time, " +
                 "which is the same transition triggered by a different cause.")]
        public StatDefinitionSO equipTime;   // "equip_time"

        [Tooltip("Seconds to put this weapon away.")]
        public StatDefinitionSO stowTime;    // "stow_time"

        [Tooltip("The source stat equip/stow (and later ADS) derive from. Referenced " +
                 "here so a display can show it; the derivation itself is registered " +
                 "as a derived modifier when the weapon's container is built.")]
        public StatDefinitionSO handling;    // "handling"
    }
}
