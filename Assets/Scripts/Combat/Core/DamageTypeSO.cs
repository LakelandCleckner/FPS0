using UnityEngine;

namespace Combat.Core
{
    // A damage type as a ScriptableObject asset. Replaces the old DamageType
    // enum. Owns presentation data (color, sound) and is the identity used for
    // resistance lookups and future type interactions.
    [CreateAssetMenu(fileName = "DamageType", menuName = "Combat/Damage Type")]
    public class DamageTypeSO : ScriptableObject
    {
        [Tooltip("Stable lookup id. Set once and DO NOT change — saves/lookups key on this. Display name can change freely.")]
        public string id = "physical";

        [Tooltip("Display name shown in UI. Safe to rename.")]
        public string displayName = "Physical";

        [Tooltip("Used for hitmarkers and (later) floating damage numbers.")]
        public Color color = Color.white;

        [Tooltip("Optional hit/tick sound for this type. May be left empty for now.")]
        public AudioClip hitSound;
    }
}
