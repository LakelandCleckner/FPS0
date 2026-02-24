using UnityEngine;

public class EnemyHitbox : MonoBehaviour
{
    public EnemyHealth enemyHealth;
    public BodyPart bodyPart;
    public float damageMultiplier = 1f;

    public void ApplyDamage(float baseDamage)
    {
        float finalDamage = baseDamage * damageMultiplier;
        enemyHealth.TakeDamage(finalDamage, bodyPart);
    }
}