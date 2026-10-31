using System;
using UnityEngine;
using Combat.Events;
using Combat.Stats;

namespace Combat.Core
{
    // Player-side receiver for enemy hits: applies damage, publishes DamageTaken, raises Damaged for HUD/camera.
    public class PlayerHurtbox : MonoBehaviour
    {
        [SerializeField] private CombatantStats combatant;
        [SerializeField] private BodyPart bodyPart = BodyPart.Torso;

        // attacker (may be null), attacker position, damage dealt
        public event Action<Transform, Vector3, float> Damaged;

        public ICombatant Target => combatant;

        private WeaponEventBus bus;
        private Action<float, DamageTypeSO> applyDamage;

        public Action<float, DamageTypeSO> ApplyDamage
        {
            get
            {
                if (applyDamage == null) applyDamage = ApplyTypedDamage;
                return applyDamage;
            }
        }

        public BodyPart BodyPart => bodyPart;

        private void Awake()
        {
            if (combatant == null) combatant = GetComponent<CombatantStats>();
            bus = GetComponent<WeaponEventBus>();
        }

        private void ApplyTypedDamage(float damage, DamageTypeSO type)
        {
            if (combatant != null) combatant.TakeDamage(damage, bodyPart, type);
        }

        // Resolves ctx through the attacker's resolver, then reports the result on the player side.
        public void Receive(IHitResolver resolver, HitContext ctx, Transform attacker)
        {
            if (resolver == null || ctx == null || combatant == null || combatant.IsDying) return;

            Vector3 from = attacker != null ? attacker.position : ctx.HitPoint;
            resolver.ResolveHit(ctx);
            if (ctx.DamageDealt <= 0f) return;

            if (bus != null)
                bus.Publish(WeaponEvent.ForHit(WeaponEventType.DamageTaken, null, ctx));

            Damaged?.Invoke(attacker, from, ctx.DamageDealt);
        }
    }
}
