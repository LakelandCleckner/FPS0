using UnityEngine;
using Combat.Core;
using Combat.Stats;
using Combat.Spawning;

// Health state + defensive layers for a combatant. Phase: defensive stats.
//
// Pooling: fires OnDeath instead of destroying, so a PooledEnemy can decide when/how
// the instance returns to its pool (immediately now; deferred for a death animation or
// a status-transfer read later). Implements IPoolable so a reused instance resets its
// health and clears IsDying — neither of which happens on their own, since currentHealth
// is set in Start (once per instance) and IsDying is a one-way latch.
public class CombatantHealth : MonoBehaviour, IPoolable
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

    // Fired when this combatant dies (health hits 0). A PooledEnemy subscribes to
    // return the instance to its pool; anything else (score, loot, VFX) can too. Death
    // no longer destroys the object itself — the handler decides its fate. If NOTHING
    // handles this, the object simply persists dying; PooledEnemy's fallback destroys
    // a non-pooled one.
    public event System.Action OnDeath;

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

    // ---- IPoolable ----

    // Reused instance: refill to full and clear the dying latch. Without this a
    // respawned enemy keeps its last life's currentHealth (0 — dead) and its latched
    // IsDying, i.e. it spawns dead. Start does NOT run on reuse, so this is the only
    // reset path.
    public void OnSpawn()
    {
        IsDying = false;
        cachedMaxVersion = int.MinValue;   // force a fresh max resolve
        RefreshMax();
        currentHealth = cachedMax;
        started = true;                    // in case OnSpawn beats Start on first life
    }

    public void OnDespawn()
    {
        // Nothing needed pre-deactivate for health today. IsDying is cleared on the
        // next OnSpawn. Kept for the IPoolable contract.
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
    }

    private float GetTypeMultiplier(DamageTypeSO type)
    {
        var container = Container;
        if (type == null || type.resistanceStat == null || container == null) return 1f;
        float resist = container.Resolve(type.resistanceStat);
        return Mathf.Max(0f, 1f - resist);
    }

    private float GetBodyPartResistance(BodyPart part)
    {
        if (bodyPartResistances != null)
            foreach (var b in bodyPartResistances)
                if (b.part == part) return b.multiplier;
        return 1f;
    }

    private float GetVulnerabilityMultiplier()
    {
        var container = Container;
        if (defenseKeys == null || defenseKeys.damageTaken == null || container == null) return 1f;
        return Mathf.Max(0f, 1f + container.Resolve(defenseKeys.damageTaken));
    }

    public float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart)
    {
        return GetTypeMultiplier(type)
             * GetBodyPartResistance(bodyPart)
             * GetVulnerabilityMultiplier();
    }

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

    // Death no longer destroys directly. It fires OnDeath; a PooledEnemy returns the
    // instance to its pool (or a fallback destroys a non-pooled one). This is what lets
    // the return be DEFERRED — for a death animation, or so a status transfer can read
    // this corpse's still-alive status pools before anything resets or deactivates.
    private void Die()
    {
        OnDeath?.Invoke();
    }
}