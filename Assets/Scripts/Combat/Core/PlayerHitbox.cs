using System;
using UnityEngine;
using Combat.Stats;

namespace Combat.Core
{
    // One player hit zone on a third-person body bone; routes hits to the PlayerHurtbox above it
    public class PlayerHitbox : MonoBehaviour
    {
        public BodyPart bodyPart = BodyPart.Torso;
        public float damageMultiplier = 1f;

        [Tooltip("Found in parents if empty.")]
        [SerializeField] private PlayerHurtbox hurtbox;

        // Cached once; retained by EffectStackPool for the life of a status, so it binds to the hitbox, not the hit
        private Action<float, DamageTypeSO> applyTickDamage;

        public PlayerHurtbox Hurtbox => hurtbox != null ? hurtbox : (hurtbox = GetComponentInParent<PlayerHurtbox>());

        public Action<float, DamageTypeSO> ApplyTickDamage => applyTickDamage ??= ApplyTypedDamage;

        private void ApplyTypedDamage(float damage, DamageTypeSO type)
        {
            var h = Hurtbox;
            if (h != null) h.TakeDamage(damage, bodyPart, type);
        }
    }
}
