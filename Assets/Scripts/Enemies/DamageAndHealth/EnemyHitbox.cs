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

    public void ApplyDamage(float baseDamage)
    {
        float finalDamage = baseDamage * damageMultiplier;
        combatant.TakeDamage(finalDamage, bodyPart, damageType);
    }
}