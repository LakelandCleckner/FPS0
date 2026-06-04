using UnityEngine;
using Combat.Core;

// EnemyHealth, with ITargetInfo added so the combat system can
// additions: the interface, Faction, IsDying flag.
public class EnemyHealth : MonoBehaviour, ITargetInfo
{
    public float maxHealth = 100f;
    private float currentHealth;
    public float CurrentHealth => currentHealth;

    // ---- ITargetInfo ----
    public float MaxHealth => maxHealth;
    public bool IsDying { get; private set; }   // set true the instant we hit 0
    public int Faction => 1;                      // 0 = player, 1 = enemies (placeholder)

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage, BodyPart partHit)
    {
        if (IsDying) return;

        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, maxHealth);

        Debug.Log($"{gameObject.name} took {damage:F1} to {partHit} | HP: {currentHealth:F1}/{maxHealth:F1}");

        if (currentHealth == 0f)
        {
            IsDying = true;
            Die();
        }
    }

    void Die()
    {
        Destroy(gameObject);
    }
}