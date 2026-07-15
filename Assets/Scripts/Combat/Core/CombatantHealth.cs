using UnityEngine;
using Combat.Core;
using Combat.Stats;

// Health for ANY combatant — enemies, the player, destructibles. One component so
// damage/death logic lives in one place (and replicates once, for future multiplayer).
//
// Phase 2i-a: max health is now a STAT (max_health) resolved from this entity's
// StatContainer, so buffs/elites/debuffs are just modifiers. Current health stays
// AUTHORITATIVE RUNTIME STATE — it isn't derived from anything; TakeDamage writes it.
//
// Max-health changes are detected lazily (version compare on access — same
// cache-and-invalidate pattern used throughout) and applied per MaxHealthChangeMode.
public class CombatantHealth : MonoBehaviour, ICombatant
{
    // How current health reacts when MAX health changes at runtime.
    public enum MaxHealthChangeMode
    {
        // Current health is untouched (just clamped to the new max). More headroom
        // on an increase; a decrease can lop off the top. Default — the neutral
        // choice for buffs that grant capacity you then have to heal into.
        ClampOnly,

        // Current scales with the max, preserving the health FRACTION. At 50% HP,
        // doubling max keeps you at 50%. Suits aura/elite buffs.
        Proportional,

        // The DIFFERENCE is granted (or removed): +100 max => +100 current.
        // Suits "second phase: double your health" boss moments.
        Additive
    }

    [Header("Stats")]
    [Tooltip("This combatant's stat container. Max health is read from it (max_health).")]
    [SerializeField] private CombatantStats combatantStats;
    [Tooltip("The max_health StatDefinitionSO.")]
    [SerializeField] private StatDefinitionSO maxHealthStat;

    [Header("Max Health Changes")]
    [Tooltip("How CURRENT health reacts when MAX health changes at runtime.")]
    [SerializeField] private MaxHealthChangeMode maxHealthChangeMode = MaxHealthChangeMode.ClampOnly;

    // --- runtime state ---
    private float currentHealth;
    private bool started;

    // cached resolved max + the container version it was resolved at
    private float cachedMax;
    private int cachedMaxVersion = int.MinValue;

    public bool IsDying { get; private set; }

    [Header("Identity")]
    [SerializeField] private int faction = 1;
    public int Faction => faction;

    public StatContainer Stats => combatantStats != null ? combatantStats.Container : null;

    // MAX HEALTH — stat-driven. Resolving is a cached read; when the underlying stat
    // actually changes (a modifier lands), the change mode's side effect fires ONCE.
    public float MaxHealth
    {
        get { RefreshMax(); return cachedMax; }
    }

    public float CurrentHealth => currentHealth;

    // --- Type resistance (per damage type) ---
    [System.Serializable]
    public struct Resistance { public DamageTypeSO type; public float multiplier; }
    [SerializeField] private Resistance[] resistances;

    // --- Body-part resistance (second defensive layer; composes with type) ---
    [System.Serializable]
    public struct BodyPartResistance { public BodyPart part; public float multiplier; }
    [SerializeField] private BodyPartResistance[] bodyPartResistances;

    private void Awake()
    {
        // fallback: find the stats component on this object if unassigned
        if (combatantStats == null)
            combatantStats = GetComponent<CombatantStats>();
    }

    private void Start()
    {
        RefreshMax();          // resolve the initial max from the container
        currentHealth = cachedMax;  // start full
        started = true;
    }

    // Resolve max_health if the stat changed since we last looked, and apply the
    // change mode's side effect to CURRENT health. Lazy: called on access, so no
    // per-frame polling. (A stat-change EVENT, when the trigger system lands, can
    // drive this eagerly for anything that needs frame-accurate reactions.)
    private void RefreshMax()
    {
        var container = Stats;
        if (container == null || maxHealthStat == null)
        {
            // no stat backing — fall back to whatever we last had (or 1 to avoid /0)
            if (cachedMaxVersion == int.MinValue) { cachedMax = 1f; cachedMaxVersion = 0; }
            return;
        }

        int v = container.GetVersion(maxHealthStat);
        if (v == cachedMaxVersion) return;   // unchanged — cached value stands

        float newMax = Mathf.Max(1f, container.Resolve(maxHealthStat));
        float oldMax = cachedMax;

        cachedMax = newMax;
        cachedMaxVersion = v;

        // before Start() there's no current health to reconcile
        if (!started) return;
        if (oldMax <= 0f) { currentHealth = Mathf.Min(currentHealth, newMax); return; }
        if (Mathf.Approximately(oldMax, newMax)) return;

        switch (maxHealthChangeMode)
        {
            case MaxHealthChangeMode.Proportional:
                // preserve the health fraction
                float frac = currentHealth / oldMax;
                currentHealth = Mathf.Clamp(frac * newMax, 0f, newMax);
                break;

            case MaxHealthChangeMode.Additive:
                // grant (or remove) the difference
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

    // Compose ALL defensive layers into one multiplier. Future layers (armor,
    // vulnerability/damage_taken) fold in here and callers need no changes.
    public float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart)
    {
        return GetTypeResistance(type) * GetBodyPartResistance(bodyPart);
    }

    // Plain subtract — all multipliers (offensive AND defensive) are applied
    // upstream, so the value passed here is exactly what lands and the damage
    // number that read ctx.DamageDealt always matches.
    public void TakeDamage(float damage, BodyPart partHit, DamageTypeSO type)
    {
        if (IsDying) return;

        RefreshMax();   // pick up any max change before clamping against it

        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, cachedMax);

        if (currentHealth == 0f)
        {
            IsDying = true;
            Die();
        }
    }

    // Heal (used by pickups/regen later; also handy for testing the change modes).
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