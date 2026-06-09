using UnityEngine;
using Combat.Core;

public class EnemyHitbox : MonoBehaviour
{
    public EnemyHealth enemyHealth;
    public BodyPart bodyPart;
    public float damageMultiplier = 1f;
    [SerializeField] private DamageTypeSO damageType; // assign Physical in inspector

    public void ApplyDamage(float baseDamage)
    {
        float finalDamage = baseDamage * damageMultiplier;
        enemyHealth.TakeDamage(finalDamage, bodyPart, damageType);
    }
}