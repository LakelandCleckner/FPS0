using UnityEngine;
using Combat.Core;

public class EnemyHitbox : MonoBehaviour
{
    public CombatantHealth combatantHealth;
    public BodyPart bodyPart;
    public float damageMultiplier = 1f;
    [SerializeField] private DamageTypeSO damageType; // assign Physical in inspector

    public void ApplyDamage(float baseDamage)
    {
        float finalDamage = baseDamage * damageMultiplier;
        combatantHealth.TakeDamage(finalDamage, bodyPart, damageType);
    }
}