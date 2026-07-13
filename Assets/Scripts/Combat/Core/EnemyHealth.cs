using UnityEngine;
using Combat.Core;

public class EnemyHealth : MonoBehaviour, ICombatant
{
    public float maxHealth = 100f;
    private float currentHealth;
    public float CurrentHealth => currentHealth;

    public float MaxHealth => maxHealth;
    public bool IsDying { get; private set; }
    public int Faction => 1;

    // --- Type resistance (per damage type) ---
    [System.Serializable]
    public struct Resistance { public DamageTypeSO type; public float multiplier; }
    [SerializeField] private Resistance[] resistances;

    // --- Body-part resistance (per body part) — inert-ready: leave empty for
    //     neutral. This is the second defensive layer; composes with type. ---
    [System.Serializable]
    public struct BodyPartResistance { public BodyPart part; public float multiplier; }
    [SerializeField] private BodyPartResistance[] bodyPartResistances;

    void Start() { currentHealth = maxHealth; }

    private float GetTypeResistance(DamageTypeSO type)
    {
        if (type != null && resistances != null)
            foreach (var r in resistances)
                if (r.type == type) return r.multiplier;
        return 1f;
    }

    private float GetBodyPartResistance(BodyPart part)
    {
        if (bodyPartResistances != null)
            foreach (var b in bodyPartResistances)
                if (b.part == part) return b.multiplier;
        return 1f;
    }

    // Compose ALL defensive layers into one multiplier. Add future layers here
    // (armor, vulnerability windows, etc.) and callers need no changes.
    public float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart)
    {
        return GetTypeResistance(type) * GetBodyPartResistance(bodyPart);
    }

    // Plain subtract — all multipliers (offensive AND defensive) are already
    // applied upstream so the value passed here is exactly what lands, and the
    // damage number that read ctx.DamageDealt always matches.
    public void TakeDamage(float damage, BodyPart partHit, DamageTypeSO type)
    {
        if (IsDying) return;

        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, maxHealth);

        string typeName = type != null ? type.displayName : "Untyped";
        //Debug.Log($"{gameObject.name} took {damage:F1} {typeName} to {partHit} | HP: {currentHealth:F1}/{maxHealth:F1}");

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