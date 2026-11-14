using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Loot;
using Combat.Stats;
using Combat.Spawning;

namespace Combat.Enemies
{
    // Applies an archetype and its modifier layers to this enemy every life.
    // Runs before other Starts so health and AI read the final stats on the first frame.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatantStats))]
    public class EnemyDefinitionApplier : MonoBehaviour, IPoolable
    {
        [Tooltip("Variant to apply. Takes priority over Archetype.")]
        [SerializeField] private EnemyVariantSO variant;
        [Tooltip("Used when no variant is set (archetype with no layers).")]
        [SerializeField] private EnemyArchetypeSO archetype;

        private CombatantStats stats;
        private CombatantHealth health;
        private EnemyMeleeAttack melee;
        private EnemyLootDropper loot;
        private WeaponHitResolver resolver;
        private EnemyShield shield;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private Vector3 authoredScale;
        private bool started;
        private bool overridden;

        private readonly List<EnemyModifierSO> layers = new List<EnemyModifierSO>();
        private readonly List<object> modifierOwners = new List<object>();
        private readonly Dictionary<StatDefinitionSO, float> authoredBases = new Dictionary<StatDefinitionSO, float>();
        private readonly List<IHitEffect> meleeEffects = new List<IHitEffect>();
        private readonly List<EnemyDeathEffectSO> deathEffects = new List<EnemyDeathEffectSO>();
        private readonly List<EnemyAffix> affixes = new List<EnemyAffix>();
        private EnemyVariantSO affixVariant;
        private EnemyArchetypeSO affixArchetype;

        public EnemyVariantSO Variant => variant;
        public EnemyArchetypeSO Archetype => variant != null ? variant.archetype : archetype;
        public bool HasDefinition => Archetype != null;
        public string DisplayName { get; private set; } = "";
        public EnemyRank Rank { get; private set; }
        public IShieldSource Shield { get; private set; }

        // Fired after every apply, so the AI can rebind to a new behaviour profile.
        public event System.Action Applied;

        // Archetype's profile, overridden by the last layer that sets one of the same kind (a melee
        // modifier profile doesn't turn a ranged enemy melee). Read straight from the definition so
        // it is valid before the first apply.
        public Combat.AI.EnemyBehaviourProfileSO BehaviourProfile
        {
            get
            {
                var arch = Archetype;
                if (arch == null) return null;
                var p = arch.behaviourProfile;
                if (variant != null)
                    foreach (var m in variant.modifiers)
                        if (m != null && m.behaviourProfile != null && (p == null || p.ranged == m.behaviourProfile.ranged))
                            p = m.behaviourProfile;
                return p;
            }
        }

        // Every live definition-driven enemy, for HUD readers like the health bars.
        public static readonly List<EnemyDefinitionApplier> Active = new List<EnemyDefinitionApplier>();

        // Top of the visible body, for anchoring overhead UI.
        public Vector3 HeadPosition
        {
            get
            {
                bool any = false;
                float top = 0f;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled) continue;
                    float y = r.bounds.max.y;
                    if (!any || y > top) { top = y; any = true; }
                }
                var p = transform.position;
                return new Vector3(p.x, any ? top : p.y + 2f, p.z);
            }
        }
        public IReadOnlyList<EnemyModifierSO> Layers => layers;
        public IReadOnlyList<EnemyDeathEffectSO> DeathEffects => deathEffects;
        public CombatantStats Stats => stats;
        public CombatantHealth Health => health;

        private void Awake()
        {
            stats = GetComponent<CombatantStats>();
            health = GetComponent<CombatantHealth>();
            melee = GetComponent<EnemyMeleeAttack>();
            loot = GetComponent<EnemyLootDropper>();
            resolver = GetComponentInChildren<WeaponHitResolver>();
            authoredScale = transform.localScale;
            block = new MaterialPropertyBlock();
            renderers = EnemyLook.FindRenderers(gameObject);
            shield = GetComponent<EnemyShield>();
        }

        private void OnEnable()
        {
            Active.Add(this);
            if (stats != null) stats.ModifiersCleared += Apply;
            if (health != null) { health.HealthChanged += HandleHealthChanged; health.OnDeath += HandleDeath; }
        }

        private void OnDisable()
        {
            int i = Active.IndexOf(this);
            if (i >= 0) { Active[i] = Active[Active.Count - 1]; Active.RemoveAt(Active.Count - 1); }
            if (stats != null) stats.ModifiersCleared -= Apply;
            if (health != null) { health.HealthChanged -= HandleHealthChanged; health.OnDeath -= HandleDeath; }
        }

        private void Start()
        {
            started = true;
            Apply();
        }

#if UNITY_EDITOR
        // Variant or archetype edited in the Inspector during Play re-applies straight away.
        private void OnValidate()
        {
            if (Application.isPlaying && started && isActiveAndEnabled) UnityEditor.EditorApplication.delayCall += ReapplyIfAlive;
        }

        private void ReapplyIfAlive()
        {
            if (this != null && started) Apply();
        }
#endif

        // ---- IPoolable ----

        // Reuse re-applies through CombatantStats.ModifiersCleared, so order on the prefab doesn't matter.
        public void OnSpawn() { }

        public void OnDespawn()
        {
            for (int i = 0; i < affixes.Count; i++) affixes[i].OnDespawn(this);
        }

        // ---- API ----

        public void SetVariant(EnemyVariantSO v)
        {
            variant = v;
            if (started) Apply();
        }

        public void SetArchetype(EnemyArchetypeSO a)
        {
            variant = null;
            archetype = a;
            if (started) Apply();
        }

        // True when the definition owns this stat's base, so components skip pushing their own.
        public bool DefinesBase(StatDefinitionSO stat)
        {
            if (stat == null) return false;
            var arch = Archetype;
            if (arch == null) return false;
            if (arch.DefinesBase(stat)) return true;
            if (variant != null)
                foreach (var m in variant.modifiers)
                    if (m != null && m.SetsBase(stat)) return true;
            return false;
        }

        // Full, idempotent apply: bases, modifiers, melee, loot, visuals, affixes.
        public void Apply()
        {
            var c = stats != null ? stats.Container : null;
            if (c == null) return;

            RemoveModifiers(c);
            RestoreBases(c);

            layers.Clear();
            var arch = Archetype;
            if (arch == null)
            {
                DisplayName = name;
                Rank = EnemyRank.Minor;
                if (melee != null) melee.Configure(null, null);
                if (loot != null) loot.Configure(null, false, 0);
                deathEffects.Clear();
                ApplyShield(null);
                ApplyLook(EnemyLook.Plain);
                RebuildAffixes(null);
                Applied?.Invoke();
                return;
            }

            if (variant != null)
                foreach (var m in variant.modifiers)
                    if (m != null) layers.Add(m);

            foreach (var b in arch.baseStats)
                if (b.stat != null) WriteBase(c, b.stat, b.baseValue);

            for (int i = 0; i < layers.Count; i++)
            {
                var m = layers[i];
                bool added = false;
                foreach (var e in m.stats)
                {
                    if (e.stat == null) continue;
                    if (e.op == EnemyStatOp.SetBase) { WriteBase(c, e.stat, e.value); continue; }
                    e.AddTo(c, m);
                    added = true;
                }
                foreach (var a in m.affinities)
                {
                    var res = a != null && a.type != null ? a.type.resistanceStat : null;
                    if (res == null || a.resist == 0f) continue;
                    c.AddModifier(new StatModifier(res, StatResolver.FLAT, a.resist), m);
                    added = true;
                }
                if (added) modifierOwners.Add(m);
            }

            ApplyMelee(arch);
            ApplyLoot(arch);
            CollectDeathEffects(arch);
            ApplyShield(arch);
            ApplyPresentation(arch);
            RebuildAffixes(arch);

            for (int i = 0; i < affixes.Count; i++) affixes[i].OnSpawn(this);
            Applied?.Invoke();
        }

        // ---- stats ----

        private void RemoveModifiers(StatContainer c)
        {
            for (int i = 0; i < modifierOwners.Count; i++) c.RemoveAllFromOwner(modifierOwners[i]);
            modifierOwners.Clear();
        }

        // Puts back bases a previous definition overwrote, so a variant swap leaves no residue.
        private void RestoreBases(StatContainer c)
        {
            foreach (var kv in authoredBases) c.SetBase(kv.Key, kv.Value);
        }

        private void WriteBase(StatContainer c, StatDefinitionSO stat, float value)
        {
            if (!authoredBases.ContainsKey(stat)) authoredBases[stat] = c.GetBase(stat);
            c.SetBase(stat, value);
        }

        // ---- melee / loot / death ----

        private void ApplyMelee(EnemyArchetypeSO arch)
        {
            if (melee == null) return;

            DamageTypeSO type = arch.meleeDamageType;
            meleeEffects.Clear();
            AddEffects(arch.meleeHitEffects);
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].meleeDamageType != null) type = layers[i].meleeDamageType;
                AddEffects(layers[i].meleeHitEffects);
            }
            melee.Configure(type, meleeEffects);
        }

        private void AddEffects(List<HitEffectSO> list)
        {
            foreach (var so in list)
                if (so != null) meleeEffects.Add(so.GetInstance());
        }

        private void ApplyLoot(EnemyArchetypeSO arch)
        {
            if (loot == null) return;
            LootTableSO table = arch.lootTable;
            bool guaranteed = false;
            int extra = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                var m = layers[i];
                if (m.lootTable != null) table = m.lootTable;
                guaranteed |= m.guaranteedDrop;
                extra += m.extraDrops;
            }
            loot.Configure(table, guaranteed, extra);
        }

        private void CollectDeathEffects(EnemyArchetypeSO arch)
        {
            deathEffects.Clear();
            foreach (var so in arch.deathEffects) if (so != null) deathEffects.Add(so);
            for (int i = 0; i < layers.Count; i++)
                foreach (var so in layers[i].deathEffects) if (so != null) deathEffects.Add(so);
        }

        // ---- shield ----

        // Adds the shield component the first time a layer needs one; keeps it (off) afterwards.
        private void ApplyShield(EnemyArchetypeSO arch)
        {
            EnemyShieldSO so = null;
            for (int i = 0; arch != null && i < layers.Count; i++)
                if (layers[i].shield != null) so = layers[i].shield;

            if (so != null && shield == null) shield = gameObject.AddComponent<EnemyShield>();
            if (shield != null) shield.Configure(so, health, stats, resolver);
            if (health != null) health.Absorber = so != null ? shield : null;
            Shield = so != null ? shield : null;
        }

        // ---- presentation ----

        private void ApplyPresentation(EnemyArchetypeSO arch)
        {
            DisplayName = variant != null ? variant.DisplayName : arch.StatSourceLabel;

            var rank = arch.rank;
            for (int i = 0; i < layers.Count; i++)
                if ((int)layers[i].raiseRankTo > (int)rank) rank = (EnemyRank)layers[i].raiseRankTo;
            Rank = rank;
            ApplyLook(EnemyLook.Resolve(layers));
        }

        private void ApplyLook(EnemyLook look)
        {
            transform.localScale = authoredScale * look.Scale;
            if (look.IsPlain && !overridden) return;
            overridden = !look.IsPlain;
            look.ApplyTo(renderers, block);
        }

        // ---- affixes ----

        // Same definition as last life keeps its runtimes; they reset in OnSpawn.
        private void RebuildAffixes(EnemyArchetypeSO arch)
        {
            if (arch != null && variant == affixVariant && arch == affixArchetype) return;

            for (int i = 0; i < affixes.Count; i++) affixes[i].OnRemoved(this);
            affixes.Clear();
            affixVariant = variant;
            affixArchetype = arch;
            if (arch == null) return;

            AddAffixes(arch.affixes);
            for (int i = 0; i < layers.Count; i++) AddAffixes(layers[i].affixes);
        }

        private void AddAffixes(List<EnemyAffixSO> list)
        {
            foreach (var so in list)
            {
                if (so == null) continue;
                var rt = so.CreateRuntime();
                if (rt != null) affixes.Add(rt);
            }
        }

        private void HandleHealthChanged(float current, float max)
        {
            for (int i = 0; i < affixes.Count; i++) affixes[i].OnHealthChanged(this, current, max);
        }

        private void HandleDeath()
        {
            for (int i = 0; i < affixes.Count; i++) affixes[i].OnDeath(this);
            FireDeathEffects();
        }

        private void FireDeathEffects()
        {
            if (deathEffects.Count == 0) return;
            if (resolver == null) { Debug.LogWarning($"[EnemyDefinitionApplier] '{name}' has death effects but no WeaponHitResolver."); return; }

            bool precision = health != null && health.LastHitPart == BodyPart.Head;
            var ctx = new DeathEffectContext(transform.position, stats, resolver);
            for (int i = 0; i < deathEffects.Count; i++)
            {
                var fx = deathEffects[i];
                if (precision && fx.cancelOnPrecisionKill) continue;
                fx.Trigger(ctx);
            }
        }
    }
}
