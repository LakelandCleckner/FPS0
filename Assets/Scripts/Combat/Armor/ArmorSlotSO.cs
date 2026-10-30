using UnityEngine;

namespace Combat.Armor
{
    // One armour slot (Head, Arms, ...). Add a slot by creating an asset and listing it on PlayerLoadout.
    [CreateAssetMenu(fileName = "ArmorSlot", menuName = "Combat/Armor/Armor Slot")]
    public class ArmorSlotSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";
    }
}
