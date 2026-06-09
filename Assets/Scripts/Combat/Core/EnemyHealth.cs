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
    
    
    // Per-type resistance. Default neutral (1.0). A fire-immune object would
    // return 0 for Fire; an armored unit <1 for Physical; etc. This is the
    // single chokepoint every damage path passes through.
    [System.Serializable]
    public struct Resistance { public DamageTypeSO type; public float multiplier; }
    [SerializeField] private Resistance[] resistances;

    public float GetResistanceMultiplier(DamageTypeSO type)
    {
        if ( type!= null && resistances != null)
            foreach (var r in resistances)
                if (r.type == type) return r.multiplier;
        return 1f;
    }

    void Start() { currentHealth = maxHealth; }

    // Damage entry with type, so resistance applies. The old (damage, BodyPart)
    // path forwards to this with the source's type.
    public void TakeDamage(float damage, BodyPart partHit, DamageTypeSO type)
    {
        if (IsDying) return;

        float mult = type != null ? GetResistanceMultiplier(type) : 1f;
        float resisted = damage * mult;
        currentHealth = Mathf.Clamp(currentHealth - resisted, 0f, maxHealth);

        string typeName = type != null ? type.displayName : "Untyped";
        Debug.Log($"{gameObject.name} took {resisted:F1} {typeName} damage to {partHit} | HP: {currentHealth:F1}/{maxHealth:F1}");

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
