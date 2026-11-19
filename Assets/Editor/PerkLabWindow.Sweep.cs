using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Combat.Core;
using Combat.Events;
using Combat.Perks;
using Combat.Stats;
using Combat.Status;
using Combat.Weapons;
using Object = UnityEngine.Object;

// Sweep tab: mounts each weapon perk alone on the held weapon, runs a scenario from its own settings against the sims, and records PASS / FAIL / SKIP.
public partial class PerkLabWindow
{
    private sealed class SweepResult
    {
        public PerkSO Perk;
        public string Verdict;   // PASS, FAIL, SKIP
        public string Detail;
        public bool Changed;
    }

    private const string SweepPrefsKey = "FPS0.PerkLab.Sweep";

    [SerializeField] private string sweepSearch = "";
    [SerializeField] private string sweepType = "All types";
    [SerializeField] private bool showPass = true, showFail = true, showSkip = true;

    private readonly List<SweepResult> sweepResults = new List<SweepResult>();
    private IEnumerator<float> sweep;
    private double sweepNextAt;
    private bool sweepRunning;
    private string sweepStatus = "";

    // Saved host state, put back when the sweep ends
    private WeaponInstance sweepHost;
    private WeaponPerks sweepHostPerks;
    private List<PerkSO> sweepSaved;

    // ------------------------------------------------------------------ GUI

    private void DrawSweep()
    {
        if (!EditorApplication.isPlaying) { EditorGUILayout.HelpBox("The sweep runs in Play mode with a weapon in hand.", MessageType.None); return; }
        DrawSimPicker();
        EditorGUILayout.LabelField("Tests weapon perks one at a time on the weapon in your hand, against the simulated enemy.", EditorStyles.wordWrappedMiniLabel);

        bool canRun = !sweepRunning && SimReady;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button(new GUIContent("Run All", "Test every weapon perk."), GUILayout.Width(70))) StartSweep(SweepList(false));
                int failed = sweepResults.Count(r => r.Verdict == "FAIL");
                using (new EditorGUI.DisabledScope(failed == 0))
                    if (GUILayout.Button(new GUIContent($"Rerun Failed ({failed})", "Test only the perks that failed last time."), GUILayout.Width(120)))
                        StartSweep(sweepResults.Where(r => r.Verdict == "FAIL" && r.Perk != null).Select(r => r.Perk).ToList());
                var pickRect = GUILayoutUtility.GetRect(new GUIContent("Run One Perk..."), EditorStyles.popup, GUILayout.Width(130));
                if (GUI.Button(pickRect, new GUIContent("Run One Perk...", "Search for a perk by name and test just that one."), EditorStyles.popup))
                    new PerkDropdown(allWeaponPerks ?? new List<PerkSO>(), p => { if (!sweepRunning && SimReady) StartSweep(new List<PerkSO> { p }); }).Show(pickRect);
            }
            using (new EditorGUI.DisabledScope(!sweepRunning))
                if (GUILayout.Button(new GUIContent("Stop", "Stop the run and put your weapon's perks back."), GUILayout.Width(50))) StopSweep();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            sweepSearch = EditorGUILayout.TextField(new GUIContent("Filter", "Part of a perk name or type. Separate several with commas, e.g. fresh, holster."), sweepSearch);
            var types = SweepTypes();
            int cur = Mathf.Max(0, Array.IndexOf(types, sweepType));
            sweepType = types[EditorGUILayout.Popup(cur, types, GUILayout.Width(110))];
            var matches = SweepList(true);
            using (new EditorGUI.DisabledScope(!canRun || matches.Count == 0))
                if (GUILayout.Button(new GUIContent($"Run Filtered ({matches.Count})", "Test the perks matching the filter and type."), GUILayout.Width(120))) StartSweep(matches);
        }

        if (!string.IsNullOrEmpty(sweepStatus)) EditorGUILayout.LabelField(sweepStatus, EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            showPass = GUILayout.Toggle(showPass, new GUIContent($"PASS {sweepResults.Count(r => r.Verdict == "PASS")}", "Show or hide passing perks."), EditorStyles.toolbarButton, GUILayout.Width(70));
            showFail = GUILayout.Toggle(showFail, new GUIContent($"FAIL {sweepResults.Count(r => r.Verdict == "FAIL")}", "Show or hide failing perks."), EditorStyles.toolbarButton, GUILayout.Width(70));
            showSkip = GUILayout.Toggle(showSkip, new GUIContent($"SKIP {sweepResults.Count(r => r.Verdict == "SKIP")}", "Show or hide skipped perks."), EditorStyles.toolbarButton, GUILayout.Width(70));
            GUILayout.Label(new GUIContent($"changed {sweepResults.Count(r => r.Changed)}", "Results that differ from the run before (marked *)."), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Copy Report", "Copy every FAIL and SKIP line to the clipboard."), EditorStyles.toolbarButton, GUILayout.Width(90))) CopySweep();
        }

        PerkSO rerun = null;
        foreach (var r in sweepResults)
        {
            if ((r.Verdict == "PASS" && !showPass) || (r.Verdict == "FAIL" && !showFail) || (r.Verdict == "SKIP" && !showSkip)) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canRun || r.Perk == null))
                    if (GUILayout.Button(new GUIContent("Run", "Test this perk again."), EditorStyles.miniButton, GUILayout.Width(36))) rerun = r.Perk;
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = r.Verdict == "PASS" ? new Color(0.45f, 0.8f, 0.45f) : r.Verdict == "FAIL" ? new Color(0.95f, 0.4f, 0.35f) : Color.gray;
                GUILayout.Label(r.Verdict + (r.Changed ? "*" : ""), style, GUILayout.Width(48));
                if (GUILayout.Button(new GUIContent(r.Perk != null ? r.Perk.name : "?", "Select the perk asset."), EditorStyles.linkLabel, GUILayout.Width(160)) && r.Perk != null)
                {
                    EditorGUIUtility.PingObject(r.Perk);
                    Selection.activeObject = r.Perk;
                }
                GUILayout.Label(r.Perk != null ? TypeLabel(r.Perk) : "", EditorStyles.miniLabel, GUILayout.Width(110));
                GUILayout.Label(r.Detail, EditorStyles.wordWrappedMiniLabel);
            }
        }
        // Started after the loop so the results list is not changed while it is drawn
        if (rerun != null) StartSweep(new List<PerkSO> { rerun });
    }

    private static string TypeLabel(PerkSO p) => p.GetType().Name.Replace("PerkSO", "");

    private string[] SweepTypes()
        => new[] { "All types" }.Concat((allWeaponPerks ?? new List<PerkSO>()).Select(TypeLabel).Distinct().OrderBy(t => t)).ToArray();

    // Filter terms are comma separated, any one matching the name or type counts; the type popup narrows further
    private List<PerkSO> SweepList(bool filtered)
    {
        var list = allWeaponPerks ?? new List<PerkSO>();
        if (!filtered) return list.ToList();
        var terms = (sweepSearch ?? "").Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList();
        return list.Where(p =>
            (string.IsNullOrEmpty(sweepType) || sweepType == "All types" || TypeLabel(p) == sweepType) &&
            (terms.Count == 0 || terms.Any(t => p.name.ToLowerInvariant().Contains(t) || TypeLabel(p).ToLowerInvariant().Contains(t)))).ToList();
    }

    // Searchable perk picker, grouped by perk type
    private sealed class PerkDropdown : AdvancedDropdown
    {
        private readonly List<PerkSO> perks;
        private readonly Action<PerkSO> pick;

        public PerkDropdown(List<PerkSO> perks, Action<PerkSO> pick) : base(new AdvancedDropdownState())
        {
            this.perks = perks;
            this.pick = pick;
            minimumSize = new Vector2(280f, 400f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Weapon perks");
            foreach (var g in perks.Where(p => p != null).GroupBy(TypeLabel).OrderBy(g => g.Key))
            {
                var folder = new AdvancedDropdownItem($"{g.Key} ({g.Count()})");
                foreach (var p in g.OrderBy(p => p.name)) folder.AddChild(new PerkItem(p));
                root.AddChild(folder);
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is PerkItem pi) pick(pi.Perk);
        }

        private sealed class PerkItem : AdvancedDropdownItem
        {
            public readonly PerkSO Perk;
            public PerkItem(PerkSO p) : base(p.name) { Perk = p; }
        }
    }

    private void CopySweep()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Perk Sweep: {sweepResults.Count(r => r.Verdict == "PASS")} pass, {sweepResults.Count(r => r.Verdict == "FAIL")} fail, {sweepResults.Count(r => r.Verdict == "SKIP")} skip");
        foreach (var r in sweepResults)
            if (r.Verdict != "PASS") sb.AppendLine($"[{r.Verdict}{(r.Changed ? " changed" : "")}] {r.Perk?.name} ({r.Perk?.GetType().Name.Replace("PerkSO", "")}): {r.Detail}");
        EditorGUIUtility.systemCopyBuffer = sb.ToString();
        ShowNotification(new GUIContent("Copied"), 1.0);
    }

    // ------------------------------------------------------------------ run loop

    private void StartSweep(List<PerkSO> perks)
    {
        var active = loadout != null ? loadout.Active : Object.FindFirstObjectByType<WeaponLoadout>()?.Active;
        if (loadout == null) loadout = Object.FindFirstObjectByType<WeaponLoadout>();
        if (active == null || active.DamageSource == null || active.DamageSource.Instance == null) { sweepStatus = "No weapon in hand."; return; }

        sweepHost = active.DamageSource.Instance;
        sweepHostPerks = active.GetComponent<WeaponPerks>();
        if (sweepHostPerks == null) { sweepStatus = "Held weapon has no WeaponPerks."; return; }
        sweepSaved = sweepHost.Perks.ToList();

        // Keep results for perks not in this run
        foreach (var p in perks) sweepResults.RemoveAll(r => r.Perk == p);
        sweep = SweepRoutine(active, perks);
        sweepRunning = true;
        sweepNextAt = 0;
    }

    private void StopSweep()
    {
        if (!sweepRunning && sweepHost == null) return;
        sweepRunning = false;
        sweep = null;
        RestoreHost();
        sweepStatus = "Stopped.";
    }

    private void RestoreHost()
    {
        if (sweepHost != null && sweepSaved != null) SetPerks(sweepHost, sweepSaved);
        if (sweepHostPerks != null) sweepHostPerks.Reattach();
        sweepHost = null; sweepSaved = null; sweepHostPerks = null;
    }

    private void SweepTick()
    {
        if (!sweepRunning || sweep == null) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < sweepNextAt) return;
        bool more;
        try { more = sweep.MoveNext(); }
        catch (Exception e) { Debug.LogError("[PerkLab] Sweep stopped: " + e); more = false; }
        if (!more)
        {
            sweepRunning = false;
            sweep = null;
            RestoreHost();
            SaveSweep();
            sweepStatus = $"Done: {sweepResults.Count(r => r.Verdict == "PASS")} pass, {sweepResults.Count(r => r.Verdict == "FAIL")} fail, {sweepResults.Count(r => r.Verdict == "SKIP")} skip.";
            return;
        }
        sweepNextAt = now + Mathf.Max(0f, sweep.Current);
    }

    private IEnumerator<float> SweepRoutine(WeaponFireController active, List<PerkSO> perks)
    {
        var previous = LoadSweep();
        for (int i = 0; i < perks.Count; i++)
        {
            var p = perks[i];
            sweepStatus = $"{i + 1}/{perks.Count}: {p.name}";
            var result = new SweepResult { Perk = p };
            var test = TestPerk(active, p, result);
            while (true)
            {
                bool more;
                try { more = test.MoveNext(); }
                catch (Exception e) { result.Verdict = "FAIL"; result.Detail = "Threw: " + e.Message; break; }
                if (!more) break;
                yield return test.Current;
            }
            if (result.Verdict == null) { result.Verdict = "SKIP"; result.Detail = "No verdict."; }
            string key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
            result.Changed = previous.TryGetValue(key, out var old) && old != result.Verdict;
            sweepResults.Add(result);
            Repaint();
        }
        sweepResults.Sort((a, b) => a.Verdict != b.Verdict ? Rank(a.Verdict).CompareTo(Rank(b.Verdict)) : string.Compare(a.Perk?.name, b.Perk?.name, StringComparison.OrdinalIgnoreCase));
    }

    private static int Rank(string v) => v == "FAIL" ? 0 : v == "SKIP" ? 2 : 1;

    // ------------------------------------------------------------------ one perk

    private IEnumerator<float> TestPerk(WeaponFireController active, PerkSO p, SweepResult r)
    {
        void Pass(string d) { r.Verdict = "PASS"; r.Detail = d; }
        void Fail(string d) { r.Verdict = "FAIL"; r.Detail = d; }
        void Skip(string d) { r.Verdict = "SKIP"; r.Detail = d; }

        var ammo = active.GetComponent<WeaponAmmo>();
        var bus = WeaponEventBus.FindFor(active);

        if (p is TriggeredBuffPerkSO tb)
        {
            if (!Supported(tb.trigger, tb.sources, out var why)) { Skip(why); yield break; }
            var rt = Mount(p); ResetForTest(ammo); yield return 0.1f;
            float gap = Gap(tb.everyN, tb.window);
            for (int i = 0; i < tb.everyN; i++) { foreach (var w in Fire(active, bus, ammo, tb.trigger, tb.sources, simA)) yield return w; yield return gap; }
            yield return 0.05f;
            if (rt == null) { Fail("Runtime did not attach."); yield break; }
            if (!rt.TryGetStatus(out var st)) { Skip("Perk exposes no status to read."); yield break; }
            if (st.Active || st.Stacks > 0) Pass($"Active after {tb.everyN} x {tb.trigger}{(st.Stacks > 1 ? $", x{st.Stacks}" : "")}.");
            else Fail($"{tb.everyN} x {tb.trigger}, buff never became active{(st.CountMax > 0 ? $" (counter {st.Count}/{st.CountMax})" : "")}.");
            yield break;
        }

        if (p is ArmedBuffPerkSO ab)
        {
            if (!Supported(ab.armTrigger, ab.armSources, out var w1)) { Skip("Arm: " + w1); yield break; }
            if (!Supported(ab.spendTrigger, ab.spendSources, out var w2)) { Skip("Spend: " + w2); yield break; }
            var rt = Mount(p); ResetForTest(ammo); yield return 0.1f;
            foreach (var w in Fire(active, bus, ammo, ab.armTrigger, ab.armSources, simA)) yield return w;
            yield return 0.05f;
            foreach (var w in Fire(active, bus, ammo, ab.spendTrigger, ab.spendSources, simA)) yield return w;
            yield return 0.05f;
            if (rt == null || !rt.TryGetStatus(out var st)) { Skip("Perk exposes no status to read."); yield break; }
            if (st.Active || st.Stacks > 0) Pass($"Armed by {ab.armTrigger}, granted by {ab.spendTrigger}.");
            else Fail($"{ab.armTrigger} then {ab.spendTrigger}, buff never became active.");
            yield break;
        }

        if (p is MagRefillPerkSO mr)
        {
            if (IsReloadEvent(mr.trigger)) { Skip("Reload triggers refill the magazine anyway."); yield break; }
            if (!Supported(mr.trigger, mr.sources, out var why)) { Skip(why); yield break; }
            if (ammo == null) { Skip("Held weapon has no ammo component."); yield break; }
            Mount(p); ResetForTest(ammo); yield return 0.1f;
            int spend = Mathf.Max(2, ammo.MagSize / 2);
            for (int i = 0; i < spend && ammo.Magazine > 1; i++) ammo.TryConsume();
            int before = ammo.Magazine;
            int shots = 0;
            for (int i = 0; i < mr.everyN; i++)
            {
                if (mr.trigger == WeaponEventType.ShotFired) shots++;
                foreach (var w in Fire(active, bus, ammo, mr.trigger, mr.sources, simA)) yield return w;
                yield return Gap(mr.everyN, mr.window);
            }
            yield return 0.05f;
            int gained = ammo.Magazine - before + shots;
            if (gained > 0) Pass($"{mr.everyN} x {mr.trigger} put {gained} round(s) back.");
            else Fail($"{mr.everyN} x {mr.trigger}, magazine did not go up ({before} -> {ammo.Magazine}).");
            yield break;
        }

        if (p is StatusOnTriggerPerkSO so)
        {
            if (so.status == null) { Fail("No status assigned."); yield break; }
            if (!Supported(so.trigger, so.sources, out var why)) { Skip(why); yield break; }
            bool kill = so.trigger == WeaponEventType.Kill || so.trigger == WeaponEventType.PrecisionKill;
            bool onNeighbour = so.radius > 0f && (kill || !so.includeTarget);
            if (onNeighbour && so.radius < SimSpacing) { Skip($"Radius {so.radius} m is under the sim spacing ({SimSpacing} m)."); yield break; }
            var check = onNeighbour ? simB : simA;
            Mount(p); ResetForTest(ammo); yield return 0.1f;
            for (int i = 0; i < so.everyN; i++) { foreach (var w in Fire(active, bus, ammo, so.trigger, so.sources, simA)) yield return w; yield return Gap(so.everyN, so.window); }
            yield return 0.1f;
            var rec = StatusReceiver.For(check);
            int n = rec != null ? rec.Stacks(so.status) : 0;
            int conv = rec != null && so.status.convertsTo != null ? rec.Stacks(so.status.convertsTo) : 0;
            if (n > 0 || conv > 0) Pass($"{(onNeighbour ? "Neighbour" : "Target")} has {(n > 0 ? so.status.name + " x" + n : so.status.convertsTo.name)} after {so.everyN} x {so.trigger}.");
            else Fail($"{so.everyN} x {so.trigger}, no {so.status.name} on the {(onNeighbour ? "neighbour" : "target")}.");
            yield break;
        }

        if (p is HitConditionPerkSO hc)
        {
            string cond = hc.condition.ToString();
            var offHits = new List<float>(); var onHits = new List<float>();
            ResetForTest(ammo);
            // Off and on hits alternate so timed buffs from other sources hit both sides equally
            for (int s = 0; s < 8; s++)
            {
                bool on = (s & 1) == 1;
                Mount(on ? p : null);
                ReviveSim(simA);
                float origin = SetUpCondition(active, hc, cond);
                float d = SimHit(active, simA, cond == "Precision", false, false, origin, out _);
                (on ? onHits : offHits).Add(d);
                yield return 0.02f;
            }
            ReviveSim(simA);
            if (offHits.Any(d => d <= 0f)) { Skip("A base hit did no damage (immune or shielded?)."); yield break; }
            // Each on hit against the off hit just before it, median of the pairs
            var ratios = offHits.Select((d, i) => onHits[i] / d).OrderBy(x => x).ToList();
            float ratio = (ratios[1] + ratios[2]) * 0.5f, want = 1f + hc.bonus;
            string hits = $"off {string.Join("/", offHits.Select(d => d.ToString("0.#")))}, on {string.Join("/", onHits.Select(d => d.ToString("0.#")))}";
            if (Mathf.Abs(ratio - want) <= 0.03f * want) Pass($"{cond}: x{ratio:0.00} damage (expected x{want:0.00}).");
            else Fail($"{cond}: x{ratio:0.00} damage, expected x{want:0.00} ({hits}).");
            yield break;
        }

        if (p is PrecisionRefundPerkSO pr)
        {
            if (ammo == null) { Skip("Held weapon has no ammo component."); yield break; }
            Mount(p); ResetForTest(ammo); yield return 0.1f;
            for (int i = 0; i < pr.rounds * 2 + 1 && ammo.Magazine > 1; i++) ammo.TryConsume();
            int before = ammo.Magazine;
            for (int i = 0; i < pr.everyN; i++) { SimHit(active, simA, true, false, false, -1f, out _); yield return 0.05f; }
            yield return 0.05f;
            if (ammo.Magazine > before) Pass($"{pr.everyN} precision hits returned {ammo.Magazine - before} round(s).");
            else Fail($"{pr.everyN} precision hits, magazine did not go up.");
            yield break;
        }

        if (p is HealOnKillPerkSO)
        {
            var health = loadout != null ? loadout.GetComponentInParent<CombatantHealth>() : null;
            if (health == null) { Skip("No player health found."); yield break; }
            Mount(p); ResetForTest(ammo); yield return 0.1f;
            health.TakeDamage(health.MaxHealth * 0.4f, BodyPart.Torso, null);
            float before = health.CurrentHealth;
            SimHit(active, simA, false, true, false, -1f, out _);
            yield return 0.1f;
            float after = health.CurrentHealth;
            health.Heal(health.MaxHealth);
            if (after > before) Pass($"Kill healed {after - before:0.#}.");
            else Fail("Kill did not heal the player.");
            yield break;
        }

        foreach (var w in TestMore(active, p, ammo, bus, r)) yield return w;
    }

    // ------------------------------------------------------------------ helpers

    private static float Gap(int everyN, float window) => window > 0f ? Mathf.Min(0.1f, window / (everyN + 1)) : 0.05f;

    private static bool IsReloadEvent(WeaponEventType t)
        => t == WeaponEventType.ReloadStart || t == WeaponEventType.ReloadComplete || t == WeaponEventType.ReloadCancelled || t == WeaponEventType.MagFull;

    private static bool Supported(WeaponEventType t, HitSourceMask mask, out string why)
    {
        why = null;
        bool hitFamily = t == WeaponEventType.Hit || t == WeaponEventType.PrecisionHit || t == WeaponEventType.Kill || t == WeaponEventType.PrecisionKill;
        if (hitFamily && (mask & (HitSourceMask.Direct | HitSourceMask.Chain)) == 0) { why = $"{t} from status ticks only; the sweep fires direct and chain hits."; return false; }
        return true;
    }

    // One trigger of this type; reload events wait for the reload to finish
    private IEnumerable<float> Fire(WeaponFireController active, WeaponEventBus bus, WeaponAmmo ammo, WeaponEventType t, HitSourceMask mask, CombatantStats victim)
    {
        bool chainOnly = (mask & HitSourceMask.Direct) == 0 && (mask & HitSourceMask.Chain) != 0;
        switch (t)
        {
            case WeaponEventType.Hit: SimHit(active, victim, false, false, chainOnly, -1f, out _); break;
            case WeaponEventType.PrecisionHit: SimHit(active, victim, true, false, chainOnly, -1f, out _); break;
            case WeaponEventType.Kill: SimHit(active, victim, false, true, chainOnly, -1f, out _); ReviveSim(victim); break;
            case WeaponEventType.PrecisionKill: SimHit(active, victim, true, true, chainOnly, -1f, out _); ReviveSim(victim); break;
            case WeaponEventType.ShotFired: Shot(active, bus); break;
            case WeaponEventType.DryFire: if (bus != null) bus.Publish(WeaponEvent.ForWeapon(WeaponEventType.DryFire, active.DamageSource)); break;
            case WeaponEventType.DamageTaken: TakeDamage(bus); break;
            case WeaponEventType.Equip: SimSwap(active, bus, true); break;
            case WeaponEventType.Stow: SimSwap(active, bus, false); break;
            case WeaponEventType.AmmoChanged: if (ammo != null) ammo.TryConsume(); break;
            case WeaponEventType.MagEmpty: if (ammo != null) while (ammo.Magazine > 0 && ammo.TryConsume()) { } break;
            case WeaponEventType.MagFull: if (ammo != null) { ammo.TryConsume(); ammo.AddToMagazine(1, false); } break;
            case WeaponEventType.ReloadStart:
            case WeaponEventType.ReloadCancelled:
            case WeaponEventType.ReloadComplete:
                if (ammo == null) break;
                if (ammo.Magazine >= ammo.MagSize) ammo.TryConsume();
                ammo.BeginReload();
                if (t == WeaponEventType.ReloadCancelled) { ammo.CancelReload(); break; }
                if (t == WeaponEventType.ReloadComplete)
                    for (float waited = 0f; ammo.IsReloading && waited < 8f; waited += 0.1f) yield return 0.1f;
                break;
        }
        yield return 0f;
    }

    // A real hit from the held weapon; originDistance >= 0 places the shot origin that far from the hit
    private float SimHit(WeaponFireController active, CombatantStats victim, bool precision, bool kill, bool chain, float originDistance, out bool killed)
    {
        killed = false;
        var src = active.DamageSource;
        if (victim == null || active.Resolver == null) return 0f;
        var health = victim.GetComponent<CombatantHealth>();
        var hitboxes = victim.GetComponentsInChildren<EnemyHitbox>();
        var hb = precision ? hitboxes.FirstOrDefault(h => h.bodyPart == BodyPart.Head) : hitboxes.FirstOrDefault(h => h.bodyPart == BodyPart.Torso);
        if (hb == null) hb = hitboxes.FirstOrDefault();
        if (hb == null) return 0f;

        if (kill && health != null && health.CurrentHealth > 1f) health.TakeDamage(health.CurrentHealth - 1f, BodyPart.Torso, null);

        Vector3 point = hb.transform.position;
        var shot = new ShotInfo(benchShotId++);
        if (originDistance >= 0f) shot = shot.WithOrigin(point - Vector3.forward * originDistance, 0f);

        var ctx = new HitContext
        {
            Target = hb.combatant,
            HitPoint = point,
            Source = chain ? HitSource.Chain : HitSource.Direct,
            ChainDepth = chain ? 1 : 0,
            DamageSource = src,
            Attacker = src.Attacker,
            SourceFaction = src.Faction,
            DamageType = src.BaseDamageType,
            HitboxMultiplier = hb.damageMultiplier,
            BodyPartHit = hb.bodyPart,
            Shot = shot,
            ApplyStatusTickDamage = hb.ApplyTickDamage,
            Effects = src.GetEffects(),
            MaxChainDepth = src.MaxChainDepth,
            ChainFalloff = src.ChainFalloff,
            ChainGrowth = src.ChainGrowth,
            DedupMode = src.DedupMode,
            ShowFloatingNumber = false
        };
        active.Resolver.ResolveHit(ctx);
        killed = ctx.WasKill;
        if (!kill && health != null && !health.IsDying) health.Heal(health.MaxHealth);
        // Crit is a random roll, so it is divided out to keep comparisons stable
        return ctx.CritMultiplier > 0f ? ctx.DamageDealt / ctx.CritMultiplier : ctx.DamageDealt;
    }

    // Puts the target into the perk's condition; returns the shot origin distance to use (-1 = none)
    private float SetUpCondition(WeaponFireController active, HitConditionPerkSO hc, string cond)
    {
        var health = simA.GetComponent<CombatantHealth>();
        switch (cond)
        {
            case "WithinDistance": return Mathf.Max(0.5f, hc.distance * 0.5f);
            case "BeyondDistance": return hc.distance + 5f;
            case "TargetBelowHealth":
                if (health != null) health.TakeDamage(Mathf.Max(0f, health.CurrentHealth - health.MaxHealth * hc.healthFraction * 0.8f), BodyPart.Torso, null);
                return -1f;
            case "TargetHasStatus":
                ApplyStatus(active, hc.statuses != null ? hc.statuses.FirstOrDefault(s => s != null) : null);
                return -1f;
            case "TargetDebuffed":
                if (allStatuses == null) allStatuses = LoadStatuses();
                ApplyStatus(active, allStatuses.FirstOrDefault(s => s != null && s.damageType != null));
                return -1f;
        }
        return -1f;
    }

    private void ApplyStatus(WeaponFireController active, StatusSO status)
    {
        if (status == null) return;
        var rec = StatusReceiver.For(simA);
        var hb = simA.GetComponentsInChildren<EnemyHitbox>().FirstOrDefault();
        if (rec == null || hb == null) return;
        var src = active.DamageSource;
        rec.Apply(status, active.Resolver, src.Attacker, src, status.damageType, src.Faction, 0, 1f, d => hb.ApplyDamage(d));
    }

    // Only this perk on the weapon: rolled slots replaced, every other runtime (intrinsics too) detached
    private PerkRuntime Mount(PerkSO perk)
    {
        SetPerks(sweepHost, perk != null ? new List<PerkSO> { perk } : new List<PerkSO>());
        sweepHostPerks.Reattach();

        var list = typeof(WeaponPerks).GetField("runtimes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(sweepHostPerks) as List<PerkRuntime>;
        var detach = typeof(PerkRuntime).GetMethod("Detach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        PerkRuntime keep = null;
        if (list != null)
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var rt = list[i];
                if (perk != null && rt.Perk == perk && keep == null) { keep = rt; continue; }
                detach?.Invoke(rt, null);
                list.RemoveAt(i);
            }
        return keep;
    }

    private void ResetForTest(WeaponAmmo ammo)
    {
        SetHostHeld(true);
        ReviveSim(simA);
        ReviveSim(simB);
        if (ammo != null)
        {
            if (ammo.IsReloading) ammo.CancelReload();
            ammo.AddReserves(ammo.MagSize * 4);
            ammo.AddToMagazine(ammo.MagSize, false);
        }
    }

    // ------------------------------------------------------------------ saved results

    private void SaveSweep()
    {
        var map = LoadSweep();
        foreach (var r in sweepResults)
            if (r.Perk != null) map[AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(r.Perk))] = r.Verdict;
        EditorPrefs.SetString(SweepPrefsKey, string.Join(";", map.Select(kv => kv.Key + "=" + kv.Value)));
    }

    private static Dictionary<string, string> LoadSweep()
    {
        var map = new Dictionary<string, string>();
        foreach (var part in EditorPrefs.GetString(SweepPrefsKey, "").Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) map[part.Substring(0, eq)] = part.Substring(eq + 1);
        }
        return map;
    }
}
