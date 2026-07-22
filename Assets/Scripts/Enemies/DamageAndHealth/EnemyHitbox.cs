using System;
using UnityEngine;
using Combat.Core;
using Combat.Stats;

public class EnemyHitbox : MonoBehaviour
{
    // The combatant this hitbox belongs to (the ICombatant). Used both as the hit
    // context Target AND to deal damage (CombatantStats passes TakeDamage through to
    // its CombatantHealth sibling). One reference for both jobs.
    public CombatantStats combatant;

    public BodyPart bodyPart;
    public float damageMultiplier = 1f;
    [SerializeField] private DamageTypeSO damageType; // assign Physical in inspector

    // ------------------------------------------------------- cached apply delegate

    // HitContext.ApplyStatusTickDamage used to be built as a lambda at every hit
    // site: (dmg, type) => hitbox.combatant.TakeDamage(dmg, hitbox.bodyPart, type).
    // That captured `hitbox`, so every hit in the game allocated a display class and
    // a delegate — on the hitscan path as well as the projectile one.
    //
    // It lives here instead for two reasons, and the second matters more than the
    // first:
    //
    // 1. ALLOCATION. Everything the delegate needs (combatant, bodyPart) is already
    //    hitbox state, and the damage type arrives as a parameter, so there is
    //    nothing left to capture. One delegate per hitbox, created once.
    //
    // 2. LIFETIME. This delegate OUTLIVES THE HIT. StatusReceiver hands it to
    //    EffectStackPool, which retains it for the life of the status so live-linked
    //    DOT damage keeps resolving against the right target. If it were instead
    //    cached on a POOLED projectile and bound to that projectile's mutable
    //    fields, recycling the projectile would silently redirect every burn it had
    //    ever applied to whatever the reused projectile last hit — correct-looking
    //    damage landing on the wrong enemy. Binding to the hitbox makes that
    //    impossible: the hitbox is exactly as long-lived as the thing being damaged.
    private Action<float, DamageTypeSO> applyTickDamage;

    public Action<float, DamageTypeSO> ApplyTickDamage
        => applyTickDamage ??= ApplyTypedDamage;

    private void ApplyTypedDamage(float damage, DamageTypeSO type)
        => combatant.TakeDamage(damage, bodyPart, type);

    // -----------------------------------------------------------------------------

    public void ApplyDamage(float baseDamage)
    {
        float finalDamage = baseDamage * damageMultiplier;
        combatant.TakeDamage(finalDamage, bodyPart, damageType);
    }
}