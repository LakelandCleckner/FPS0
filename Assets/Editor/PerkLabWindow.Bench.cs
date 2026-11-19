using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Core;
using Combat.Events;
using Combat.Perks;
using Combat.Stats;
using Combat.Status;
using Combat.Weapons;
using Object = UnityEngine.Object;

// Bench tab: fire real events at the nearest enemy and watch the held weapon's perks react.
public partial class PerkLabWindow
{
    private enum BenchAction { Hit, PrecisionHit, Kill, PrecisionKill, ChainHit, Shot, ShotAndHit, TakeDamage }

    private sealed class LogLine { public float Time; public string Text; }

    [SerializeField] private int repeatCount = 1;
    [SerializeField] private float repeatInterval = 0.2f;
    [SerializeField] private bool keepAlive = true;

    private WeaponLoadout loadout;
    private CombatantStats target;
    private WeaponEventBus watchedBus;
    private readonly List<LogLine> log = new List<LogLine>();
    private readonly Queue<(BenchAction action, double at)> queue = new Queue<(BenchAction, double)>();
    private List<StatusSO> allStatuses;
    private float lastDamage = -1f;
    private int benchShotId = 1000000;

    partial void DrawBenchImpl(ref bool drawn)
    {
        drawn = true;
        if (!EditorApplication.isPlaying) { EditorGUILayout.HelpBox("The bench works in Play mode.", MessageType.None); return; }

        if (loadout == null) loadout = Object.FindFirstObjectByType<WeaponLoadout>();
        var active = loadout != null ? loadout.Active : null;
        var src = active != null ? active.DamageSource : null;
        if (src == null) { EditorGUILayout.HelpBox("No weapon in hand.", MessageType.Warning); return; }
        WatchBus(WeaponEventBus.FindFor(active));

        // Target
        using (new EditorGUILayout.HorizontalScope())
        {
            var health = target != null ? target.GetComponent<CombatantHealth>() : null;
            if (target == null || health == null || health.IsDying) target = NearestEnemy(active.transform.position);
            EditorGUILayout.LabelField("Target", target != null ? $"{target.name}  {health?.CurrentHealth:0}/{health?.MaxHealth:0} HP" : "none (spawn one with the debug key)");
            if (GUILayout.Button("Nearest", GUILayout.Width(70))) target = NearestEnemy(active.transform.position);
        }
        keepAlive = EditorGUILayout.ToggleLeft("Keep target alive (heal after non-kill hits)", keepAlive);

        // Actions
        using (new EditorGUILayout.HorizontalScope())
        {
            repeatCount = Mathf.Max(1, EditorGUILayout.IntField("Repeat", repeatCount));
            repeatInterval = Mathf.Max(0f, EditorGUILayout.FloatField("Every (s)", repeatInterval));
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            Button("Hit", BenchAction.Hit); Button("Precision Hit", BenchAction.PrecisionHit);
            Button("Kill", BenchAction.Kill); Button("Precision Kill", BenchAction.PrecisionKill);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            Button("Shot Fired", BenchAction.Shot); Button("Shot + Hit", BenchAction.ShotAndHit);
            Button("Chain Hit", BenchAction.ChainHit); Button("Take Damage", BenchAction.TakeDamage);
        }
        var ammo = active.GetComponent<WeaponAmmo>();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reload") && ammo != null) ammo.BeginReload();
            if (GUILayout.Button("Empty Mag") && ammo != null) while (ammo.Magazine > 0 && ammo.TryConsume()) { }
            GUILayout.Label("Swap weapons in game for Equip / Stow.", EditorStyles.miniLabel);
        }

        // Readout
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"{src.name}   mag {(ammo != null ? ammo.Magazine + "/" + ammo.MagSize : "-")}   reserves {(ammo != null ? ammo.Reserves.ToString() : "-")}   last bench damage {(lastDamage >= 0 ? lastDamage.ToString("0.#") : "-")}", EditorStyles.boldLabel);

        var perks = active.GetComponent<WeaponPerks>();
        EditorGUILayout.LabelField("Weapon perks", EditorStyles.boldLabel);
        if (perks == null || perks.Runtimes.Count == 0) EditorGUILayout.LabelField("  none", EditorStyles.miniLabel);
        else foreach (var rt in perks.Runtimes) RuntimeLine(rt);

        EditorGUILayout.LabelField("Armor perks", EditorStyles.boldLabel);
        foreach (var rt in PerkRuntime.Live.Where(r => r.SourceItem is ArmorInstance)) RuntimeLine(rt);

        EditorGUILayout.LabelField("Target statuses", EditorStyles.boldLabel);
        var receiver = target != null ? StatusReceiver.For(target) : null;
        if (allStatuses == null) allStatuses = Load<StatusSO>();
        bool anyStatus = false;
        if (receiver != null)
            foreach (var s in allStatuses)
            {
                int n = receiver.Stacks(s);
                if (n <= 0) continue;
                anyStatus = true;
                EditorGUILayout.LabelField($"  {s.name}  x{n}", EditorStyles.miniLabel);
            }
        if (!anyStatus) EditorGUILayout.LabelField("  none", EditorStyles.miniLabel);

        EditorGUILayout.LabelField("Events (newest first)", EditorStyles.boldLabel);
        for (int i = log.Count - 1; i >= 0; i--) EditorGUILayout.LabelField($"  {log[i].Time:0.00}  {log[i].Text}", EditorStyles.miniLabel);
    }

    private void Button(string label, BenchAction action)
    {
        if (!GUILayout.Button(label)) return;
        double t = EditorApplication.timeSinceStartup;
        for (int i = 0; i < repeatCount; i++) queue.Enqueue((action, t + i * repeatInterval));
    }

    private void RuntimeLine(PerkRuntime rt)
    {
        var p = rt.Perk;
        string name = p == null ? "?" : string.IsNullOrWhiteSpace(p.displayName) ? p.name : p.displayName;
        string hand = rt.SourceItem is ArmorInstance ? "" : rt.InHand ? "held" : "stowed";
        string state = "-";
        if (rt.TryGetStatus(out var st))
        {
            var parts = new List<string> { st.Active ? "ACTIVE" : "idle" };
            if (st.Stacks > 1) parts.Add("x" + st.Stacks);
            if (st.ExpiresAt > 0f) parts.Add(Mathf.Max(0f, st.ExpiresAt - Time.time).ToString("0.0") + "s");
            if (st.CountMax > 0) parts.Add(st.Count + "/" + st.CountMax);
            state = string.Join(" ", parts);
        }
        string scope = p != null ? p.listenScope.ToString() : "";
        EditorGUILayout.LabelField($"  {name}", $"{state}   [{scope}{(hand.Length > 0 ? ", " + hand : "")}]");
    }

    // ------------------------------------------------------------------ running

    private void OnInspectorUpdate()
    {
        if (tab == Tab.Bench && EditorApplication.isPlaying) Repaint();
    }

    private void Update()
    {
        if (!EditorApplication.isPlaying) { queue.Clear(); return; }
        double now = EditorApplication.timeSinceStartup;
        while (queue.Count > 0 && queue.Peek().at <= now) Run(queue.Dequeue().action);
    }

    private void Run(BenchAction action)
    {
        var active = loadout != null ? loadout.Active : null;
        var src = active != null ? active.DamageSource : null;
        if (src == null) return;
        var bus = WeaponEventBus.FindFor(active);

        switch (action)
        {
            case BenchAction.Shot: Shot(active, bus); return;
            case BenchAction.ShotAndHit: Shot(active, bus); Hit(active, false, false, false); return;
            case BenchAction.TakeDamage: TakeDamage(bus); return;
            case BenchAction.Hit: Hit(active, false, false, false); return;
            case BenchAction.PrecisionHit: Hit(active, true, false, false); return;
            case BenchAction.Kill: Hit(active, false, true, false); return;
            case BenchAction.PrecisionKill: Hit(active, true, true, false); return;
            case BenchAction.ChainHit: Hit(active, false, false, true); return;
        }
    }

    // A real round off the magazine plus the ShotFired event, without aiming or delivery
    private void Shot(WeaponFireController active, WeaponEventBus bus)
    {
        var ammo = active.GetComponent<WeaponAmmo>();
        if (ammo != null && !ammo.TryConsume()) { Log("bench: magazine empty"); return; }
        if (bus != null) bus.Publish(WeaponEvent.ForWeapon(WeaponEventType.ShotFired, active.DamageSource));
    }

    // Resolves a real hit from the held weapon on the target through its resolver
    private void Hit(WeaponFireController active, bool precision, bool kill, bool chain)
    {
        var src = active.DamageSource;
        if (target == null) target = NearestEnemy(active.transform.position);
        if (target == null) { Log("bench: no target"); return; }
        var health = target.GetComponent<CombatantHealth>();
        var hitboxes = target.GetComponentsInChildren<EnemyHitbox>();
        var hb = precision ? hitboxes.FirstOrDefault(h => h.bodyPart == BodyPart.Head) : hitboxes.FirstOrDefault(h => h.bodyPart == BodyPart.Torso);
        if (hb == null) hb = hitboxes.FirstOrDefault();
        if (hb == null || active.Resolver == null) { Log("bench: target has no hitbox or weapon has no resolver"); return; }
        if (precision && hb.bodyPart != BodyPart.Head) Log("bench: target has no head hitbox, hit torso instead");

        if (kill && health != null && health.CurrentHealth > 1f) health.TakeDamage(health.CurrentHealth - 1f, BodyPart.Torso, null);

        var ctx = new HitContext
        {
            Target = hb.combatant,
            HitPoint = hb.transform.position,
            Source = chain ? HitSource.Chain : HitSource.Direct,
            ChainDepth = chain ? 1 : 0,
            DamageSource = src,
            Attacker = src.Attacker,
            SourceFaction = src.Faction,
            DamageType = src.BaseDamageType,
            HitboxMultiplier = hb.damageMultiplier,
            BodyPartHit = hb.bodyPart,
            Shot = new ShotInfo(benchShotId++),
            ApplyStatusTickDamage = hb.ApplyTickDamage,
            Effects = src.GetEffects(),
            MaxChainDepth = src.MaxChainDepth,
            ChainFalloff = src.ChainFalloff,
            ChainGrowth = src.ChainGrowth,
            DedupMode = src.DedupMode
        };
        active.Resolver.ResolveHit(ctx);
        lastDamage = ctx.DamageDealt;

        if (keepAlive && !kill && health != null && !health.IsDying) health.Heal(health.MaxHealth);
    }

    // Publishes DamageTaken as if an enemy hit you, without hurting you
    private void TakeDamage(WeaponEventBus bus)
    {
        if (bus == null) return;
        var player = loadout != null ? loadout.GetComponentInParent<CombatantStats>() : null;
        var ctx = new HitContext
        {
            Target = player,
            Attacker = target,
            Source = HitSource.Direct,
            BodyPartHit = BodyPart.Torso,
            DamageDealt = 10f,
            Shot = new ShotInfo(benchShotId++)
        };
        bus.Publish(WeaponEvent.ForHit(WeaponEventType.DamageTaken, null, ctx));
    }

    private static CombatantStats NearestEnemy(Vector3 from)
    {
        CombatantStats best = null;
        float bestD = float.MaxValue;
        foreach (var hb in Object.FindObjectsByType<EnemyHitbox>(FindObjectsSortMode.None))
        {
            var c = hb.combatant;
            if (c == null) continue;
            var h = c.GetComponent<CombatantHealth>();
            if (h != null && h.IsDying) continue;
            float d = (c.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    // ------------------------------------------------------------------ event log

    private static readonly WeaponEventType[] Logged = (WeaponEventType[])System.Enum.GetValues(typeof(WeaponEventType));

    private void WatchBus(WeaponEventBus bus)
    {
        if (bus == watchedBus) return;
        Unwatch();
        watchedBus = bus;
        if (bus == null) return;
        foreach (var t in Logged)
            if (t != WeaponEventType.AmmoChanged) bus.Subscribe(t, OnBusEvent, null, HitSourceMask.All);
    }

    private void Unwatch()
    {
        if (watchedBus != null)
            foreach (var t in Logged) watchedBus.Unsubscribe(t, OnBusEvent);
        watchedBus = null;
    }

    private void OnBusEvent(WeaponEvent e)
    {
        string text = e.Type.ToString();
        if (e.Weapon != null) text += "  " + e.Weapon.name;
        if (e.IsHitEvent) text += $"  [{e.Hit.Source}] {e.Hit.DamageDealt:0.#}";
        Log(text);
    }

    private void Log(string text)
    {
        log.Add(new LogLine { Time = Time.time, Text = text });
        if (log.Count > 20) log.RemoveAt(0);
    }

    private void OnDisable() => Unwatch();
}
