using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Core;
using Combat.Events;
using Combat.Loadout;
using Combat.Perks;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;
using Object = UnityEngine.Object;

// Sweep for armor perks: mounts each one alone on the first worn armor piece, with every other armor and weapon perk switched off.
public partial class PerkLabWindow
{
    [System.NonSerialized] private ArmorPerks sweepArmorPerks;
    [System.NonSerialized] private ArmorInstance sweepArmorHost;
    [System.NonSerialized] private List<PerkSO> sweepArmorSaved;
    [System.NonSerialized] private bool sweepArmorMounted;

    private static readonly Dictionary<PerkSO, bool> armorPerkCache = new Dictionary<PerkSO, bool>();

    private static bool IsArmorPerk(PerkSO p)
    {
        if (p == null) return false;
        if (!armorPerkCache.TryGetValue(p, out bool armor))
            armorPerkCache[p] = armor = AssetDatabase.GetAssetPath(p).Contains("/Perks/Armor/");
        return armor;
    }

    private List<PerkSO> AllSweepPerks()
        => (allWeaponPerks ?? new List<PerkSO>()).Concat(allArmorPerks ?? new List<PerkSO>()).ToList();

    // ------------------------------------------------------------------ host

    // Finds the armor host and switches every armor perk off, so weapon tests run without them
    private void BeginArmorIsolation()
    {
        sweepArmorPerks = Object.FindFirstObjectByType<ArmorPerks>();
        var model = Object.FindFirstObjectByType<PlayerLoadout>();
        sweepArmorHost = null;
        if (model != null)
            for (int i = 0; i < model.ArmorSlotCount && sweepArmorHost == null; i++) sweepArmorHost = model.GetArmor(i);
        sweepArmorSaved = sweepArmorHost != null ? sweepArmorHost.Perks.ToList() : null;
        sweepArmorMounted = false;
        InvokeArmor("DetachAll");
    }

    private void EndArmorIsolation()
    {
        if (sweepArmorHost != null && sweepArmorSaved != null) SetPerks(sweepArmorHost, sweepArmorSaved);
        InvokeArmor("DetachAll");
        InvokeArmor("AttachAll");
        sweepArmorHost = null; sweepArmorSaved = null; sweepArmorPerks = null; sweepArmorMounted = false;
    }

    private void InvokeArmor(string method)
    {
        if (sweepArmorPerks == null) return;
        typeof(ArmorPerks).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(sweepArmorPerks, null);
    }

    // Only this perk live on the armor host; null = no armor perks at all
    private PerkRuntime MountArmor(PerkSO perk)
    {
        InvokeArmor("DetachAll");
        sweepArmorMounted = false;
        if (perk == null || sweepArmorHost == null || sweepArmorPerks == null) return null;
        SetPerks(sweepArmorHost, new List<PerkSO> { perk });
        sweepArmorPerks.Reattach(sweepArmorHost);
        sweepArmorMounted = true;

        var attached = typeof(ArmorPerks).GetField("attached", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(sweepArmorPerks) as Dictionary<ArmorInstance, List<PerkRuntime>>;
        if (attached == null || !attached.TryGetValue(sweepArmorHost, out var list)) return null;
        var detach = typeof(PerkRuntime).GetMethod("Detach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        PerkRuntime keep = null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Perk == perk && keep == null) { keep = list[i]; continue; }
            detach?.Invoke(list[i], null);
            list.RemoveAt(i);
        }
        return keep;
    }

    // ------------------------------------------------------------------ tests

    private IEnumerable<float> TestArmor(WeaponFireController active, PerkSO p, WeaponAmmo ammo, WeaponEventBus bus, SweepResult r)
    {
        void Pass(string d) { r.Verdict = "PASS"; r.Detail = d; }
        void Fail(string d) { r.Verdict = "FAIL"; r.Detail = d; }
        void Skip(string d) { r.Verdict = "SKIP"; r.Detail = d; }

        if (sweepArmorHost == null || sweepArmorPerks == null) { Skip("Wear an armor piece to test armor perks."); yield break; }
        Mount(null);   // weapon perks off

        if (p is StatPerkSO sp)
        {
            var entries = sp.modifiers.Where(e => e != null && e.stat != null).ToList();
            if (entries.Count == 0) { Fail("No modifiers with a stat."); yield break; }
            var targets = new List<WeaponDamageSource>();
            foreach (var e in entries)
            {
                var t = ArmorStatWeapon(active, e, out string why);
                if (e.target == ModifierTarget.Weapons && t == null) { Skip(why); yield break; }
                targets.Add(t);
            }
            MountArmor(null); ResetForTest(ammo); yield return 0.05f;
            var before = entries.Select((e, i) => ReadArmorStat(active, e, targets[i])).ToList();
            MountArmor(p); yield return 0.05f;
            var after = entries.Select((e, i) => ReadArmorStat(active, e, targets[i])).ToList();
            MountArmor(null);
            var bad = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                string where = targets[i] != null ? " on " + WeaponName(targets[i]) : "";
                string line = $"{entries[i].stat.name}{where} {before[i]:0.###} -> {after[i]:0.###}";
                sb.Append(i > 0 ? ", " : "").Append(line);
                if (!MovedRight(entries[i].value, before[i], after[i])) bad.Add(line);
            }
            if (bad.Count == 0) Pass(sb.ToString() + ".");
            else Fail("Wrong or no change: " + string.Join(", ", bad) + ".");
            yield break;
        }

        if (p is TriggeredBuffPerkSO tb)
        {
            if (!Supported(tb.trigger, tb.sources, out var why)) { Skip(why); yield break; }
            var rt = MountArmor(p); ResetForTest(ammo); yield return 0.1f;
            float gap = Gap(tb.everyN, tb.window);
            for (int i = 0; i < tb.everyN; i++) { foreach (var w in Fire(active, bus, ammo, tb.trigger, tb.sources, simA)) yield return w; yield return gap; }
            yield return 0.05f;
            bool ok = rt != null && rt.TryGetStatus(out var st) && (st.Active || st.Stacks > 0);
            SetHostHeld(true);
            MountArmor(null);
            if (rt == null) Fail("Runtime did not attach.");
            else if (ok) Pass($"Active after {tb.everyN} x {tb.trigger}.");
            else Fail($"{tb.everyN} x {tb.trigger}, buff never became active.");
            yield break;
        }

        if (p is LowHealthPerkSO lh)
        {
            var health = PlayerHealth(active);
            if (health == null) { Skip("No player health found."); yield break; }
            var entries = lh.modifiers.Where(e => e != null && e.stat != null).ToList();
            var targets = entries.Select(e => ArmorStatWeapon(active, e, out _)).ToList();
            var rt = MountArmor(p); ResetForTest(ammo);
            health.Heal(health.MaxHealth);
            yield return 0.05f;
            var full = entries.Select((e, i) => ReadArmorStat(active, e, targets[i])).ToList();
            bool activeFull = rt != null && rt.TryGetStatus(out var s0) && s0.Active;

            // Down to just under the threshold, never to zero
            float goal = Mathf.Max(1f, health.MaxHealth * lh.threshold * 0.8f);
            for (int tries = 0; tries < 5 && health.CurrentHealth > goal; tries++)
                health.TakeDamage(health.CurrentHealth - goal, BodyPart.Torso, null);
            yield return 0.05f;
            float lowFrac = health.CurrentHealth / health.MaxHealth;
            var low = entries.Select((e, i) => ReadArmorStat(active, e, targets[i])).ToList();
            bool activeLow = rt != null && rt.TryGetStatus(out var s1) && s1.Active;

            health.Heal(health.MaxHealth);
            yield return 0.05f;
            var back = entries.Select((e, i) => ReadArmorStat(active, e, targets[i])).ToList();
            bool activeBack = rt != null && rt.TryGetStatus(out var s2) && s2.Active;
            MountArmor(null);

            var bad = new List<string>();
            if (activeFull) bad.Add("active at full health");
            if (!activeLow) bad.Add($"not active at {lowFrac:P0} health");
            if (activeBack) bad.Add("still active after healing");
            for (int i = 0; i < entries.Count; i++)
            {
                if (!MovedRight(entries[i].value, full[i], low[i])) bad.Add($"{entries[i].stat.name} {full[i]:0.###} -> {low[i]:0.###} when low");
                if (Mathf.Abs(back[i] - full[i]) > 0.0001f) bad.Add($"{entries[i].stat.name} did not return ({back[i]:0.###}, was {full[i]:0.###})");
            }
            if (bad.Count == 0) Pass($"On at {lowFrac:P0} health, off when healed; " + string.Join(", ", entries.Select((e, i) => $"{e.stat.name} {full[i]:0.###} -> {low[i]:0.###}")) + ".");
            else Fail(string.Join("; ", bad) + ".");
            yield break;
        }

        if (p is HealOnKillPerkSO)
        {
            var health = PlayerHealth(active);
            if (health == null) { Skip("No player health found."); yield break; }
            MountArmor(p); ResetForTest(ammo); yield return 0.1f;
            health.TakeDamage(health.MaxHealth * 0.4f, BodyPart.Torso, null);
            float before = health.CurrentHealth;
            SimHit(active, simA, false, true, false, -1f, out _);
            yield return 0.1f;
            float after = health.CurrentHealth;
            health.Heal(health.MaxHealth);
            MountArmor(null);
            if (after > before) Pass($"Kill healed {after - before:0.#}.");
            else Fail("Kill did not heal the player.");
            yield break;
        }

        Skip($"Armor {TypeLabel(p)} perks are not covered by the sweep yet.");
    }

    // ------------------------------------------------------------------ helpers

    private CombatantHealth PlayerHealth(WeaponFireController active)
    {
        var player = active.DamageSource.Attacker as CombatantStats;
        return player != null ? player.GetComponent<CombatantHealth>() : null;
    }

    // The weapon a weapon-target armor modifier should show on; null for player stats
    private WeaponDamageSource ArmorStatWeapon(WeaponFireController active, ModifierEntry e, out string why)
    {
        why = null;
        if (e.target != ModifierTarget.Weapons) return null;
        string types = e.weaponTypes != null && e.weaponTypes.Count > 0 ? string.Join(" or ", e.weaponTypes.Where(t => t != null).Select(t => t.name)) : "any weapon";
        if (TypeMatches(e, active.DamageSource)) return active.DamageSource;
        if (e.scope.ToString() == "InHands") { why = $"Hold a {types} to test (applies only in hands)."; return null; }
        if (loadout != null)
            for (int i = 0; i < LoadoutSlots.Count; i++)
            {
                var c = loadout.ControllerIn(i);
                if (c != null && c.DamageSource != null && TypeMatches(e, c.DamageSource)) return c.DamageSource;
            }
        why = $"Needs a {types} in the loadout.";
        return null;
    }

    private static bool TypeMatches(ModifierEntry e, WeaponDamageSource src)
    {
        if (e.weaponTypes == null || e.weaponTypes.Count == 0) return true;
        var arch = src != null && src.Instance != null && src.Instance.Definition != null ? src.Instance.Definition.archetype : null;
        return arch != null && e.weaponTypes.Contains(arch.weaponType);
    }

    private float ReadArmorStat(WeaponFireController active, ModifierEntry e, WeaponDamageSource weapon)
    {
        if (e.target == ModifierTarget.Weapons)
            return weapon != null && weapon.Container != null ? weapon.Container.Resolve(e.stat) : 0f;
        var player = active.DamageSource.Attacker as CombatantStats;
        return player != null ? player.Resolve(e.stat) : 0f;
    }

    private static string WeaponName(WeaponDamageSource src)
        => src != null && src.Instance != null && src.Instance.Definition != null ? src.Instance.Definition.name : "weapon";
}
