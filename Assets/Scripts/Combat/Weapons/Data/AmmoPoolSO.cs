using UnityEngine;

namespace Combat.Weapons
{
    // A shared reserve pool on the player. Archetypes that reference one draw reserves from it.
    [CreateAssetMenu(fileName = "AmmoPool", menuName = "Combat/Weapons/Ammo Pool")]
    public class AmmoPoolSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";

        [Tooltip("Most reserves the pool can hold. 0 = no cap.")]
        [Min(0)] public int capacity = 0;
    }
}
