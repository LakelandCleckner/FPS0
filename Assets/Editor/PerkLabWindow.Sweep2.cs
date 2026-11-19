using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Enemies;
using Combat.Events;
using Combat.Loadout;
using Combat.Perks;
using Combat.Sources;
using Combat.Stats;
using Combat.Status;
using Combat.Weapons;

// Sweep tests for stat, mag-scaled, swap, holster, pool and effect perks, plus the simulated swap used by Equip / Stow.
public partial class PerkLabWindow
{
    // Second half of TestPerk: perk types the first half does not handle
    private IEnumerable<float> TestMore(WeaponFireController active, PerkSO p, WeaponAmmo ammo, WeaponEventBus bus, SweepResult r)
    {
        void Pass(string d) { r.Verdict = "PASS"; r.Detail = d; }
        void Fail(string d) { r.Verdict = "FAIL"; r.Detail = d; }
        void Skip(string d) { r.Verdict = "SKIP"; r.Detail = d; }

        if (p is StatPerkSO sp)
        {
            var entries = sp.modifiers.Where(e => e != null && e.stat != null).ToList();
            if (entries.Count == 0) { Fail("No modifiers with a stat."); yield break; }
            Mount(null); ResetForTest(ammo); yield return 0.05f;
            var before = entries.Select(e => ReadStat(active, e)).ToList();
            Mount(p); yield return 0.05f;
            var after = entries.Select(e => ReadStat(active, e)).ToList();
            var bad = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                string line = $"{entries[i].stat.name} {before[i]:0.###} -> {after[i]:0.###}";
                sb.Append(i > 0 ? ", " : "").Append(line);
                if (!MovedRight(entries[i].value, before[i], after[i])) bad.Add(line);
            }
            if (bad.Count == 0) Pass(sb.ToString() + ".");
            else Fail("Wrong or no change: " + string.Join(", ", bad) + ".");
            yield break;
        }

        if (p is MagScaledPerkSO ms)
        {
            if (ammo == null) { Skip("Held weapon has no ammo component."); yield break; }
            var entries = ms.modifiers.Where(e => e != null && e.stat != null).ToList();
            if (entries.Count == 0) { Fail("No modifiers with a stat."); yield break; }
            Mount(null); ResetForTest(ammo); yield return 0.05f;
            var baseVals = entries.Select(e => ReadStat(active, e)).ToList();
            Mount(p); ResetForTest(ammo); yield return 0.05f;
            var full = entries.Select(e => ReadStat(active, e)).ToList();
            while (ammo.Magazine > 0 && ammo.TryConsume()) { }
            yield return 0.05f;
            var empty = entries.Select(e => ReadStat(active, e)).ToList();
            var bad = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                float strong = ms.invert ? empty[i] : full[i], weak = ms.invert ? full[i] : empty[i];
                string line = $"{entries[i].stat.name} base {baseVals[i]:0.###}, full {full[i]:0.###}, empty {empty[i]:0.###}";
                sb.Append(i > 0 ? "; " : "").Append(line);
                bool ok = MovedRight(entries[i].value, baseVals[i], strong) && Mathf.Abs(weak - baseVals[i]) < Mathf.Abs(strong - baseVals[i]);
                if (!ok) bad.Add(line);
            }
            if (bad.Count == 0) Pass(sb.ToString() + ".");
            else Fail("Does not scale as set: " + string.Join("; ", bad) + ".");
            yield break;
        }

        if (p is SwapSynergyPerkSO)
        {
            var others = new List<WeaponDamageSource>();
            if (loadout != null)
                for (int i = 0; i < LoadoutSlots.Count; i++)
                {
                    var c = loadout.ControllerIn(i);
                    if (c != null && c != active && c.DamageSource != null) others.Add(c.DamageSource);
                }
            if (others.Count == 0) { Skip("Needs a second weapon in the loadout."); yield break; }
            if (bus == null) { Skip("No event bus."); yield break; }
            var rt = Mount(p); ResetForTest(ammo); yield return 0.1f;
            foreach (var src in others) { bus.Publish(WeaponEvent.ForHit(WeaponEventType.Hit, src, OtherHit(src))); yield return 0.05f; }
            yield return 0.05f;
            if (rt == null || !rt.TryGetStatus(out var st)) { Fail("Runtime did not attach or exposes no status."); yield break; }
            if (st.Active || st.Stacks > 0) Pass($"Active after hits from {others.Count} other weapon(s).");
            else Fail($"Hits from {others.Count} other weapon(s), buff never became active.");
            yield break;
        }

        if (p is HolsterReloadPerkSO hr)
        {
            if (ammo == null) { Skip("Held weapon has no ammo component."); yield break; }
            var rt = Mount(p); ResetForTest(ammo); yield return 0.05f;
            while (ammo.Magazine > 0 && ammo.TryConsume()) { }
            SetHostHeld(false);
            yield return hr.delay + 0.3f;
            int mag = ammo.Magazine;
            bool heldDuring = rt != null && rt.InHand;
            SetHostHeld(true);
            if (mag >= ammo.MagSize) Pass($"Stowed {hr.delay:0.#} s, magazine refilled ({mag}/{ammo.MagSize}).");
            else Fail($"Stowed {hr.delay:0.#} s, magazine still {mag}/{ammo.MagSize} (perk saw held {heldDuring}, reserves {(ammo.InfiniteReserves ? "infinite" : ammo.Reserves.ToString())}).");
            yield break;
        }

        if (p is PoolPerkSO pp)
        {
            if (!Supported(pp.trigger, pp.sources, out var why)) { Skip(why); yield break; }
            bool deals = pp.damageCoefficient > 0f, applies = pp.applyStatus != null;
            bool slows = pp.pool.slow > 0f && pp.pool.slowStat != null;
            if (!deals && !applies && !slows) { Fail("Pool has no damage, status or slow."); yield break; }
            Mount(p); ResetForTest(ammo); yield return 0.1f;
            foreach (var w in Fire(active, bus, ammo, pp.trigger, pp.sources, simA)) yield return w;
            var a0 = Vitals(simA); var b0 = Vitals(simB);
            float slowA0 = slows ? simA.Resolve(pp.pool.slowStat) : 0f;
            Physics.SyncTransforms();
            yield return pp.pool.growTime + pp.pool.tickInterval * 2.5f + 0.1f;
            var seen = new List<string>();
            if (deals && (Vitals(simA) < a0 || Vitals(simB) < b0)) seen.Add("damage");
            if (applies)
            {
                int n = StackCount(simA, pp.applyStatus) + StackCount(simB, pp.applyStatus);
                if (n > 0) seen.Add(pp.applyStatus.name + " x" + n);
            }
            if (slows && simA.Resolve(pp.pool.slowStat) < slowA0) seen.Add("slow");
            Mount(null);
            if (seen.Count > 0) Pass($"{pp.trigger} pool: {string.Join(", ", seen)}.");
            else Fail($"{pp.trigger} pool, nothing seen on the sims (check enemy layers {pp.enemyLayers.value}).");
            yield break;
        }

        if (p is EffectPerkSO ep)
        {
            if (ep.effects == null || ep.effects.All(e => e == null)) { Fail("No effects assigned."); yield break; }
            // Element status of the perk, put on the target first so transfer and consume effects have something to use
            var elements = new List<DamageTypeSO>();
            ep.CollectElements(elements);
            // Fresh load, matched by element name so a stale or copied asset still matches
            allStatuses = LoadStatuses();
            var elementNames = elements.Where(e => e != null).Select(e => e.name).ToList();
            var primer = allStatuses.FirstOrDefault(s => s != null && s.damageType != null && elementNames.Contains(s.damageType.name) && s.convertsTo == null);

            // Statuses the perk applies on hit; these must show on the target
            var hitStatuses = ep.effects.OfType<ApplyStatusEffectSO>().Select(e => e.status).Where(s => s != null).ToList();

            var off = new List<EffectSnap>(); var on = new List<EffectSnap>();
            for (int pass = 0; pass < 2; pass++)
            {
                var into = pass == 0 ? off : on;
                Mount(pass == 0 ? null : p); ResetForTest(ammo); yield return 0.1f;
                foreach (var w in EffectRun(active, "hit", false, null, into)) yield return w;
                foreach (var w in EffectRun(active, "kill", true, null, into)) yield return w;
                if (primer != null) foreach (var w in EffectRun(active, "primed kill", true, primer, into)) yield return w;
            }
            Mount(null); ResetForTest(ammo);
            string both = $"off [{string.Join("; ", off)}] on [{string.Join("; ", on)}]";
            if (hitStatuses.Count > 0)
            {
                var hit = on.FirstOrDefault(s => s.Label == "hit");
                var missing = hitStatuses.Where(s => hit == null || !(hit.TargetNow + " " + hit.TargetStatuses).Contains(s.name)).Select(s => s.name).ToList();
                if (missing.Count == 0) Pass($"Hit applies {string.Join(", ", hitStatuses.Select(s => s.name))}. {both}");
                else Fail($"Hit did not apply {string.Join(", ", missing)}. {both}");
                yield break;
            }
            bool differs = false;
            for (int i = 0; i < on.Count && i < off.Count; i++) differs |= on[i].DiffersFrom(off[i]);
            if (differs) Pass("Differs with the perk: " + both);
            else Fail("Same with and without the perk: " + both);
            yield break;
        }

        Skip($"{p.GetType().Name.Replace("PerkSO", "")} perks are not covered by the sweep yet.");
    }

    // One scenario for effect perks; adds a summary line per scenario
    // What one effect scenario left behind
    private sealed class EffectSnap
    {
        public string Label, TargetNow, TargetStatuses, NeighbourStatuses;
        public float Dealt;
        public bool NeighbourHurt;

        // Dealt damage needs a 2% gap so float noise does not count
        public bool DiffersFrom(EffectSnap o)
            => TargetStatuses != o.TargetStatuses || NeighbourStatuses != o.NeighbourStatuses || NeighbourHurt != o.NeighbourHurt
               || Mathf.Abs(Dealt - o.Dealt) > 0.02f * Mathf.Max(1f, Mathf.Abs(o.Dealt));

        public override string ToString()
            => $"{Label}: dealt {Dealt:0.#} | A at hit {TargetNow}, 0.6 s {TargetStatuses} | B {NeighbourStatuses}{(NeighbourHurt ? " damaged" : "")}";
    }

    private IEnumerable<float> EffectRun(WeaponFireController active, string label, bool kill, StatusSO primer, List<EffectSnap> into)
    {
        ReviveSim(simA); ReviveSim(simB);
        if (primer != null) ApplyStatus(active, primer);
        float b0 = Vitals(simB);
        float dealt = SimHit(active, simA, false, kill, false, -1f, out _);
        string now = StatusList(simA);
        Physics.SyncTransforms();
        yield return 0.6f;
        into.Add(new EffectSnap
        {
            Label = label,
            TargetNow = now,
            Dealt = dealt,
            TargetStatuses = StatusList(simA),
            NeighbourStatuses = StatusList(simB),
            NeighbourHurt = Vitals(simB) < b0
        });
        ReviveSim(simA); ReviveSim(simB);
    }

    // ------------------------------------------------------------------ helpers

    // Every status asset; a type search missed some status subclasses, so this checks every ScriptableObject
    private static List<StatusSO> LoadStatuses()
    {
        var list = new List<StatusSO>();
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
        {
            var s = AssetDatabase.LoadAssetAtPath<StatusSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null && !list.Contains(s)) list.Add(s);
        }
        return list.OrderBy(s => s.name).ToList();
    }

    private float ReadStat(WeaponFireController active, ModifierEntry e)
    {
        var src = active.DamageSource;
        if (e.target == ModifierTarget.Weapons) return src.Container != null ? src.Container.Resolve(e.stat) : 0f;
        var player = src.Attacker as CombatantStats;
        return player != null ? player.Resolve(e.stat) : 0f;
    }

    // Positive values must raise the stat, negative must lower it
    private static bool MovedRight(float value, float before, float after)
    {
        const float eps = 0.0001f;
        if (value > 0f) return after > before + eps;
        if (value < 0f) return after < before - eps;
        return Mathf.Abs(after - before) <= eps;
    }

    // Health plus shield, so damage soaked by a shield still shows
    private static float Vitals(CombatantStats c)
    {
        if (c == null) return 0f;
        var h = c.GetComponent<CombatantHealth>();
        var s = c.GetComponent<EnemyShield>();
        return (h != null ? h.CurrentHealth : 0f) + (s != null ? s.CurrentShield : 0f);
    }

    private int StackCount(CombatantStats c, StatusSO status)
    {
        var rec = c != null ? StatusReceiver.For(c) : null;
        return rec != null ? rec.Stacks(status) : 0;
    }

    // Reads the receiver's own pools, so no asset list is needed; flags a second receiver if hits would find a different one
    private string StatusList(CombatantStats c)
    {
        if (c == null) return "-";
        var rec = StatusReceiver.For(c);
        var onObject = c.GetComponent<StatusReceiver>();
        string list = Pools(rec);
        if (onObject != rec) list += $" (receiver on object {(onObject == null ? "missing" : Pools(onObject))})";
        return list;
    }

    private static string Pools(StatusReceiver rec)
    {
        if (rec == null) return "none";
        var pools = typeof(StatusReceiver).GetField("pools", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rec) as System.Collections.IDictionary;
        if (pools == null) return "?";
        var parts = new List<string>();
        foreach (var key in pools.Keys)
            if (key is StatusSO s && rec.Stacks(s) > 0) parts.Add(s.name + " x" + rec.Stacks(s));
        return parts.Count > 0 ? string.Join(" ", parts) : "-";
    }

    private HitContext OtherHit(WeaponDamageSource src)
    {
        var hb = simA.GetComponentsInChildren<EnemyHitbox>().FirstOrDefault();
        return new HitContext
        {
            Target = simA,
            HitPoint = hb != null ? hb.transform.position : simA.transform.position,
            Source = HitSource.Direct,
            DamageSource = src,
            Attacker = src.Attacker,
            SourceFaction = src.Faction,
            DamageType = src.BaseDamageType,
            BodyPartHit = BodyPart.Torso,
            DamageDealt = 1f,
            Shot = new ShotInfo(benchShotId++),
            ShowFloatingNumber = false
        };
    }

    // Drives the host's held state the way the loadout does on a swap
    private void SetHostHeld(bool held)
    {
        if (sweepHostPerks == null) return;
        typeof(WeaponPerks).GetMethod("SetHeld", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(sweepHostPerks, new object[] { held });
    }

    // Same order as a real swap: held off at stow start, Stow, then Equip, then held on
    private void SimSwap(WeaponFireController active, WeaponEventBus bus, bool reEquip)
    {
        SetHostHeld(false);
        if (bus != null) bus.Publish(WeaponEvent.ForWeapon(WeaponEventType.Stow, active.DamageSource));
        if (!reEquip) return;
        if (bus != null) bus.Publish(WeaponEvent.ForWeapon(WeaponEventType.Equip, active.DamageSource));
        SetHostHeld(true);
    }
}
