using UnityEngine;
using Combat.Core;
using Combat.Stats;

// Health state + defensive layers for a combatant. Phase 2i-b refactor: NO LONGER
// implements ICombatant — the combatant identity is CombatantStats, which delegates
// health to this sibling. This keeps stat reads (crit) off the health path entirely.
//
// max_health is still a STAT resolved from the CombatantStats container (buffs/elites
// are modifiers); current_health stays authoritative runtime state. Max changes are
// detected lazily (version compare on access) and applied per MaxHealthChangeMode.
public class CombatantHealth : MonoBehaviour
{
    public enum MaxHealthChangeMode { ClampOnly, Proportional, Additive }

    [Header("Stats")]
    [Tooltip("The combatant's stats (max_health lives here). Auto-found if empty.")]
    [SerializeField] private CombatantStats combatantStats;
    [Tooltip("The max_health StatDefinitionSO.")]
    [SerializeField] private StatDefinitionSO maxHealthStat;

    [Header("Max Health Changes")]
    [SerializeField] private MaxHealthChangeMode maxHealthChangeMode = MaxHealthChangeMode.ClampOnly;

    private float currentHealth;
    private bool started;

    private float cachedMax;
    private int cachedMaxVersion = int.MinValue;

    public bool IsDying { get; private set; }

    public float MaxHealth { get { RefreshMax(); return cachedMax; } }
    public float CurrentHealth => currentHealth;

    // --- resistances ---
    [System.Serializable]
    public struct Resistance { public DamageTypeSO type; public float multiplier; }
    [SerializeField] private Resistance[] resistances;

    [System.Serializable]
    public struct BodyPartResistance { public BodyPart part; public float multiplier; }
    [SerializeField] private BodyPartResistance[] bodyPartResistances;

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
        var container = combatantStats != null ? combatantStats.Container : null;
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

    public float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart)
    {
        return GetTypeResistance(type) * GetBodyPartResistance(bodyPart);
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