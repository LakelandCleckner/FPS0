using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Events;
using Combat.Hazards;
using Combat.Stats;
using Combat.Status;
using Combat.Weapons;

namespace Combat.Perks
{
    // On an event, drop a damaging ground pool. Ticks count as status ticks.
    [CreateAssetMenu(fileName = "PoolPerk", menuName = "Combat/Perks/Pool Perk")]
    public class PoolPerkSO : PerkSO
    {
        [Header("Trigger")]
        public WeaponEventType trigger = WeaponEventType.Kill;
        [Tooltip("Hit-family triggers only. Pool ticks are StatusTick, so leave that out to stop pools spawning pools.")]
        public HitSourceMask sources = HitSourceMask.Direct;
        [Tooltip("Weapon perk: hear every loadout weapon, not just this one.")]
        public bool anyWeapon = false;
        [Tooltip("Pools alive at once from this perk. The oldest fades when a new one spawns.")]
        [Min(1)] public int maxActive = 3;

        [Header("Pool")]
        public HazardPoolSpec pool = new HazardPoolSpec();
        public LayerMask enemyLayers = 1 << 7;

        [Header("Damage per tick")]
        [Tooltip("Weapon stat the damage scales from (weapon_damage). Empty = coefficient is flat damage.")]
        public StatDefinitionSO damageStat;
        public float damageCoefficient = 0.1f;
        [Tooltip("Also sets the pool colour.")]
        public DamageTypeSO damageType;

        [Header("Status (optional)")]
        [Tooltip("Applied on every tick to everyone inside.")]
        public StatusSO applyStatus;

        private void Reset() => listenScope = PerkListenScope.Always;

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private readonly List<HazardHandle> active = new List<HazardHandle>();
            private readonly List<IHitEffect> effects = new List<IHitEffect>(2);
            private WeaponHitResolver resolver;
            private HazardTick tick;

            private PoolPerkSO Def => (PoolPerkSO)Definition;

            protected override void OnAttach()
            {
                if (Ctx.IsArmor)
                {
                    Debug.LogWarning($"[PoolPerk] '{Definition.name}' needs a weapon; it does nothing on armor.");
                    return;
                }

                var fire = Ctx.Weapon.GetComponentInParent<WeaponFireController>();
                if (fire == null) fire = Ctx.Weapon.GetComponentInChildren<WeaponFireController>(true);
                resolver = fire != null ? fire.Resolver : null;
                if (resolver == null)
                {
                    Debug.LogWarning($"[PoolPerk] '{Definition.name}' found no WeaponHitResolver on its weapon.");
                    return;
                }

                var d = Def;
                var spec = d.damageStat != null
                    ? new DamageSpec(d.damageStat, StatScope.Source, d.damageCoefficient, d.damageType)
                    : new DamageSpec(d.damageCoefficient, d.damageType);
                if (d.damageCoefficient > 0f) effects.Add(new DamageHitEffect(spec));
                if (d.applyStatus != null) effects.Add(new ApplyStatusHitEffect(d.applyStatus));

                tick = Tick;
                Listen(d.trigger, d.sources, d.anyWeapon);
            }

            protected override void OnDetach()
            {
                for (int i = 0; i < active.Count; i++) active[i].Expire();
                active.Clear();
                effects.Clear();
            }

            protected override void OnEvent(in WeaponEvent evt)
            {
                if (resolver == null || (effects.Count == 0 && Def.pool.slow <= 0f)) return;

                Vector3 at;
                if (evt.IsHitEvent)
                {
                    var mb = evt.Hit.Target as MonoBehaviour;
                    at = mb != null ? mb.transform.position : evt.Hit.HitPoint;
                }
                else if (Ctx.Player != null) at = Ctx.Player.transform.position;
                else return;

                Prune();
                while (active.Count >= Mathf.Max(1, Def.maxActive))
                {
                    active[0].Expire();
                    active.RemoveAt(0);
                }

                var h = HazardPool.Spawn(Def.pool, at, Def.pool.ColorFor(Def.damageType), 0, Def.enemyLayers.value, Ctx.Player, tick);
                if (h.Ticking) active.Add(h);
            }

            private void Prune()
            {
                for (int i = active.Count - 1; i >= 0; i--)
                    if (!active[i].Ticking) active.RemoveAt(i);
            }

            // Hits like a DoT tick: numbers, no hitmarker, kills count as StatusTick.
            private bool Tick(HazardPool p, List<HazardTargets.Target> inside)
            {
                if (Detached || effects.Count == 0) return false;
                var found = new List<HazardTargets.Target>(inside);

                bool hit = false;
                var weapon = Ctx.Weapon;
                for (int i = 0; i < found.Count; i++)
                {
                    var victim = found[i].Combatant;
                    if (victim.IsDying) continue;
                    var ctx = new HitContext
                    {
                        Target = victim,
                        HitPoint = found[i].Point,
                        SourceFaction = weapon.Faction,
                        DamageType = Def.damageType,
                        Source = HitSource.StatusTick,
                        BodyPartHit = BodyPart.Torso,
                        ApplyStatusTickDamage = (dmg, type) => victim.TakeDamage(dmg, BodyPart.Torso, type),
                        Attacker = weapon.Attacker,
                        DamageSource = weapon,
                        Effects = effects,
                        ShowFloatingNumber = true
                    };
                    resolver.ResolveHit(ctx);
                    if (ctx.DamageDealt > 0f) hit = true;
                }
                return hit;
            }
        }
    }
}
