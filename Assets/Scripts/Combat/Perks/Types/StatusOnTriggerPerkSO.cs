using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Events;
using Combat.Info;
using Combat.Status;
using Combat.Weapons;

namespace Combat.Perks
{
    // On a counted trigger, apply stacks of a status to the target, or to every enemy within a radius of it.
    [CreateAssetMenu(fileName = "StatusOnTriggerPerk", menuName = "Combat/Perks/Status On Trigger Perk")]
    public class StatusOnTriggerPerkSO : PerkSO
    {
        [Header("Trigger")]
        public WeaponEventType trigger = WeaponEventType.Hit;
        [Tooltip("Hit-family triggers only.")]
        public HitSourceMask sources = HitSourceMask.Direct;
        [Tooltip("Weapon perk: hear every loadout weapon, not just this one.")]
        public bool anyWeapon = false;
        [Tooltip("Hit-family triggers: count each shot once, so pellets and pierce don't multiply it.")]
        public bool oncePerShot = true;
        [Tooltip("Triggers needed per application.")]
        [Min(1)] public int everyN = 1;
        [Tooltip("The N triggers must land within this many seconds. 0 = no window.")]
        [Min(0f)] public float window = 0f;

        [Header("Status")]
        public StatusSO status;
        [Min(1)] public int stacks = 1;

        [Header("Area")]
        [Tooltip("0 = the target only. Above 0 = every enemy within this many metres of the target.")]
        [Min(0f)] public float radius = 0f;
        [Tooltip("Area only: also apply to the target itself (off for kills, the target is dead).")]
        public bool includeTarget = true;
        public LayerMask enemyLayers = 1 << 7;

        private void Reset() => listenScope = PerkListenScope.Active;
        public override string TriggerGroup => Describer.Trigger(trigger);

        public override void CollectElements(List<DamageTypeSO> into)
        {
            if (status != null && status.damageType != null) into.Add(status.damageType);
        }

        protected override void DescribeAuto(Describer d)
        {
            d.Every(everyN, trigger, window, sources, anyWeapon);
            d.Add("apply ");
            if (stacks > 1) d.Count(stacks, "stacks").Add(" stacks of ");
            d.StatusLink(status);
            if (radius > 0f)
            {
                d.Add(includeTarget ? " to the target and every enemy within " : " to every enemy within ").Meters(radius, "radius");
                d.Add(includeTarget ? " of it" : " of the target");
            }
            else d.Add(" to the target");
            d.Add(".");
        }

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private readonly Queue<float> times = new Queue<float>();
            private readonly List<ICombatant> found = new List<ICombatant>(16);
            private readonly HashSet<ICombatant> exclude = new HashSet<ICombatant>();
            private NeighbourFinder finder;
            private WeaponHitResolver resolver;
            private int count;
            private int lastShot = -1;
            private object lastShotSource;

            private StatusOnTriggerPerkSO Def => (StatusOnTriggerPerkSO)Definition;

            public override bool TryGetStatus(out PerkStatus s)
            {
                s = default;
                if (Def.everyN <= 1) return false;
                s.Count = Def.window > 0f ? times.Count : count;
                s.CountMax = Def.everyN;
                s.Active = s.Count > 0;
                return s.Active;
            }

            protected override void OnAttach()
            {
                if (Ctx.IsArmor || Def.status == null) return;
                var fire = Ctx.Weapon.GetComponentInParent<WeaponFireController>();
                if (fire == null) fire = Ctx.Weapon.GetComponentInChildren<WeaponFireController>(true);
                resolver = fire != null ? fire.Resolver : null;
                if (resolver == null)
                {
                    Debug.LogWarning($"[StatusOnTriggerPerk] '{Definition.name}' found no WeaponHitResolver on its weapon.");
                    return;
                }
                if (Def.radius > 0f) finder = new NeighbourFinder(Def.enemyLayers.value);
                Listen(Def.trigger, Def.sources, Def.anyWeapon);
            }

            protected override void OnEvent(in WeaponEvent evt)
            {
                if (!evt.IsHitEvent) return;
                var hit = evt.Hit;

                if (Def.oncePerShot)
                {
                    int shot = hit.Shot.ShotId;
                    if (shot != 0 && shot == lastShot && ReferenceEquals(hit.DamageSource, lastShotSource)) return;
                    lastShot = shot;
                    lastShotSource = hit.DamageSource;
                }

                if (!Counted()) { NotifyStatus(); return; }
                NotifyStatus();

                var target = hit.Target;
                if (Def.radius <= 0f)
                {
                    ApplyTo(target, hit);
                    return;
                }

                var mb = target as MonoBehaviour;
                Vector3 center = mb != null ? mb.transform.position : hit.HitPoint;
                exclude.Clear();
                if (target != null) exclude.Add(target);
                finder.FindInRadius(center, Def.radius, hit.SourceFaction, exclude, found);
                if (Def.includeTarget) ApplyTo(target, hit);
                for (int i = 0; i < found.Count; i++) ApplyTo(found[i], hit);
                SpawnBlast(center);
            }

            private void ApplyTo(ICombatant victim, HitContext hit)
            {
                if (victim == null || victim.IsDying) return;
                var receiver = StatusReceiver.For(victim);
                if (receiver == null) return;

                var status = Def.status;
                var tickType = status.damageType;
                System.Action<float> applyTick = dmg => victim.TakeDamage(dmg, BodyPart.Torso, tickType);
                for (int s = 0; s < Def.stacks; s++)
                    receiver.Apply(status, resolver, hit.Attacker, hit.DamageSource, tickType,
                                   hit.SourceFaction, 0, 1f, applyTick);
            }

            private void SpawnBlast(Vector3 center)
            {
                var type = Def.status.damageType;
                if (type == null || type.blastVfx == null) return;
                float scale = type.blastReferenceRadius > 0f ? Def.radius / type.blastReferenceRadius : 1f;
                VfxPool.Spawn(type.blastVfx, center, Quaternion.identity, null, type.color, type.blastLifetime, scale, type.colorizeVfx);
            }

            // True when this trigger completes a set of N.
            private bool Counted()
            {
                if (Def.everyN <= 1) return true;

                if (Def.window <= 0f)
                {
                    if (++count < Def.everyN) return false;
                    count = 0;
                    return true;
                }

                float now = Time.time;
                times.Enqueue(now);
                while (times.Count > 0 && now - times.Peek() > Def.window) times.Dequeue();
                if (times.Count < Def.everyN) return false;
                times.Clear();
                return true;
            }
        }
    }
}
