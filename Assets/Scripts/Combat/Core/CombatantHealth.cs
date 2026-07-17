using UnityEngine;
using Combat.Core;
using Combat.Stats;

// Health state + defensive layers for a combatant. Phase: defensive stats.
//
// DEFENSE is now data-driven where it should be dynamic:
//   - per-TYPE resistance is a STAT resolved from the container, keyed by the
//     DamageTypeSO's resistanceStat (fire_resistance, physical_resistance, ...).
//     A "shatter fire resist" debuff is a negative modifier on fire_resistance.
//   - general vulnerability (damage_taken) is a STAT: +0.3 => takes 30% more.
//   - body-part resistance stays an authored array for now (tied to hitbox setup).
//
// max_health is a stat (from earlier); current_health is authoritative runtime state.
public class CombatantHealth : MonoBehaviour
{
    public enum MaxHealthChangeMode { ClampOnly, Proportional, Additive }

    [Header("Stats")]
    [SerializeField] private CombatantStats combatantStats;
    [SerializeField] private StatDefinitionSO maxHealthStat;
    [Tooltip("General defensive stats (damage_taken). Per-type resistance lives on " +
             "the DamageTypeSO.")]
    [SerializeField] private DefenseStatKeys defenseKeys;

    [Header("Max Health Changes")]
    [SerializeField] private MaxHealthChangeMode maxHealthChangeMode = MaxHealthChangeMode.ClampOnly;

    private float currentHealth;
    private bool started;

    private float cachedMax;
    private int cachedMaxVersion = int.MinValue;

    public bool IsDying { get; private set; }

    public float MaxHealth { get { RefreshMax(); return cachedMax; } }
    public float CurrentHealth => currentHealth;

    // --- Body-part resistance (authored array; positional, tied to hitbox setup) ---
    [System.Serializable]
    public struct BodyPartResistance { public BodyPart part; public float multiplier; }
    [SerializeField] private BodyPartResistance[] bodyPartResistances;

    private StatContainer Container => combatantStats != null ? combatantStats.Container : null;

    private void Awake()
    {
        if (combatantStats == null)
            combatantStats = GetComponent<CombatantStats>();
    }

    private void Start()
    {
        RefreshMax();
        currentHealth = cachedMax;
        started = true;
    }

    private void RefreshMax()
    {
        var container = Container;
        if (container == null || maxHealthStat == null)
        {
            if (cachedMaxVersion == int.MinValue) { cachedMax = 1f; cachedMaxVersion = 0; }
            return;
        }

        int v = container.GetVersion(maxHealthStat);
        if (v == cachedMaxVersion) return;

        float newMax = Mathf.Max(1f, container.Resolve(maxHealthStat));
        float oldMax = cachedMax;

        cachedMax = newMax;
        cachedMaxVersion = v;

        if (!started) return;
        if (oldMax <= 0f) { currentHealth = Mathf.Min(currentHealth, newMax); return; }
        if (Mathf.Approximately(oldMax, newMax)) return;

        switch (maxHealthChangeMode)
        {
            case MaxHealthChangeMode.Proportional:
                currentHealth = Mathf.Clamp((currentHealth / oldMax) * newMax, 0f, newMax);
                break;
            case MaxHealthChangeMode.Additive:
                currentHealth = Mathf.Clamp(currentHealth + (newMax - oldMax), 0f, newMax);
                break;
            case MaxHealthChangeMode.ClampOnly:
            default:
                currentHealth = Mathf.Clamp(currentHealth, 0f, newMax);
                break;
        }

        Debug.Log($"[Health] {gameObject.name} max_health {oldMax:F0} -> {newMax:F0} " +
                  $"({maxHealthChangeMode}) | HP now {currentHealth:F0}/{newMax:F0}");
    }

    // Per-type resistance — resolved from the container via the type's resistanceStat.
    // 0 = no resist (x1), 0.5 = takes half (x0.5), negative (debuff) = takes more (>x1).
    private float GetTypeMultiplier(DamageTypeSO type)
    {
        var container = Container;
        if (type == null || type.resistanceStat == null || container == null) return 1f;
        float resist = container.Resolve(type.resistanceStat);
        return Mathf.Max(0f, 1f - resist);   // floored so a huge resist can't go negative
    }

    private float GetBodyPartResistance(BodyPart part)
    {
        if (bodyPartResistances != null)
            foreach (var b in bodyPartResistances)
                if (b.part == part) return b.multiplier;
        return 1f;
    }

    // General vulnerability — (1 + resolved damage_taken). +0.3 => x1.3 (takes more).
    private float GetVulnerabilityMultiplier()
    {
        var container = Container;
        if (defenseKeys == null || defenseKeys.damageTaken == null || container == null) return 1f;
        return Mathf.Max(0f, 1f + container.Resolve(defenseKeys.damageTaken));
    }

    // Composed defensive multiplier: type resistance x body-part x vulnerability.
    public float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart)
    {
        return GetTypeMultiplier(type)
             * GetBodyPartResistance(bodyPart)
             * GetVulnerabilityMultiplier();
    }

    // Whether this combatant is currently DEBUFFED defensively (vulnerability active),
    // for damage-number styling. True when damage_taken resolves above 0.
    public bool IsDebuffed
    {
        get
        {
            var container = Container;
            if (defenseKeys == null || defenseKeys.damageTaken == null || container == null) return false;
            return container.Resolve(defenseKeys.damageTaken) > 0f;
        }
    }

    public void TakeDamage(float damage, BodyPart partHit, DamageTypeSO type)
    {
        if (IsDying) return;
        RefreshMax();
        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, cachedMax);
        if (currentHealth == 0f)
        {
            IsDying = true;
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (IsDying) return;
        RefreshMax();
        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, cachedMax);
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}