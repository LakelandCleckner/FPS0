using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Armor;
using Combat.Core;
using Combat.Info;
using Combat.Loadout;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    // Character tab: player stats, equipped armor with active perks, and a build summary. Values update in place while open.
    public class CharacterPanel : InventoryPanel
    {
        [Header("Column widths (relative)")]
        [SerializeField, Min(0.1f)] private float statsWidth = 1.2f;
        [SerializeField, Min(0.1f)] private float armorWidth = 1f;
        [SerializeField, Min(0.1f)] private float overviewWidth = 0.9f;
        [SerializeField] private float columnGap = 24f;

        [Header("Active perks")]
        [Range(0f, 1f)] [SerializeField] private float stowedAlpha = 0.45f;

        private sealed class StatRow
        {
            public StatSheetSO.Entry Entry;
            public TMP_Text Value, Delta;
            public RectTransform Fill;
            public float Last = float.NaN;
        }

        private sealed class ActiveRow
        {
            public PerkStatus Status;
            public TMP_Text Text;
            public int LastTenths = int.MinValue;
        }

        private readonly List<StatRow> rows = new List<StatRow>();
        private readonly List<ActiveRow> active = new List<ActiveRow>();
        private readonly List<ModifierEntry> effects = new List<ModifierEntry>();
        private RectTransform activeRoot;
        private TMP_Text healthText;
        private RectTransform healthFill;
        private float lastHealth = -1f, lastMax = -1f;
        private bool activeDirty;

        private void OnEnable() => PerkRuntime.AnyStatusChanged += MarkActive;
        private void OnDisable() => PerkRuntime.AnyStatusChanged -= MarkActive;
        private void MarkActive(PerkRuntime _) => activeDirty = true;

#if UNITY_EDITOR
        private void OnValidate()
        {
            var screen = GetComponentInParent<InventoryScreen>(true);
            if (screen != null) screen.RequestRebuild();
        }
#endif

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            rows.Clear();
            active.Clear();
            activeRoot = null;
            healthText = null;
            healthFill = null;
            lastHealth = lastMax = -1f;

            var s = screen.Style;
            var layout = UIKit.HStack(root, columnGap, null, TextAnchor.UpperLeft);
            layout.childForceExpandHeight = true;

            Stats(Column(root, s, "Stats", statsWidth), screen);
            Armor(Column(root, s, "Armor", armorWidth), screen);
            Overview(Column(root, s, "Overview", overviewWidth), screen);

            Tick(screen);
        }

        // Called every frame while the tab is open.
        public void Tick(InventoryScreen screen)
        {
            if (screen == null || screen.Style == null) return;
            if (activeDirty) RebuildActive(screen);

            var stats = screen.PlayerStats;
            var c = stats != null ? stats.Container : null;
            for (int i = 0; i < rows.Count; i++) UpdateStat(rows[i], c, screen.Style);

            UpdateHealth(screen);
            UpdateTimers();
        }

        private static RectTransform Column(RectTransform root, UIStyleSO s, string name, float flex)
        {
            var col = UIKit.Node(root, name);
            UIKit.Size(col, width: 0f, flexWidth: flex, flexHeight: 1f);
            Surface(col, s);
            var area = UIKit.Stretch(UIKit.Node(col, "Area"));
            return UIKit.Scroll(area, s.gap, UIKit.Pad(s.padding + 4f, s.padding + 4f, s.padding, s.padding));
        }

        // ------------------------------------------------------------------ stats

        private void Stats(RectTransform body, InventoryScreen screen)
        {
            var s = screen.Style;
            Heading(body, s, "Stats", "Click to pin");

            var sheet = screen.CharacterStatSheet;
            if (sheet == null)
            {
                UIKit.Text(body, "NoSheet", s, "Assign a character stat sheet on the inventory screen.", s.smallSize, s.textFaint, wrap: true);
                return;
            }

            foreach (var group in sheet.groups)
            {
                if (!string.IsNullOrWhiteSpace(group.label)) GroupLabel(body, s, group.label);
                foreach (var e in group.entries)
                    if (e != null && e.stat != null) StatLine(body, screen, e);
            }

            WeaponEffects(body, screen);
        }

        private void StatLine(Transform parent, InventoryScreen screen, StatSheetSO.Entry e)
        {
            var s = screen.Style;
            var row = UIKit.Node(parent, "Stat_" + e.Label);
            UIKit.HStack(row, 14f, UIKit.Pad(8f, 8f, 0f, 0f));
            UIKit.Size(row, height: s.bodySize + 10f);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            UIKit.Size(UIKit.Text(row, "Label", s, e.Label, s.smallSize, s.textMuted), flexWidth: 1f);

            var r = new StatRow { Entry = e };
            if (e.display == StatDisplay.Bar)
            {
                var bar = UIKit.Node(row, "Bar");
                UIKit.Size(bar, width: 110f, height: s.barHeight);
                UIKit.Stretch(UIKit.Box(bar, "Track", s, s.barTrack, small: true).rectTransform);
                r.Fill = UIKit.Solid(bar, "Fill", s.barBase).rectTransform;
                UIKit.Anchor(r.Fill, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            }

            r.Value = UIKit.Text(row, "Value", s, "", s.bodySize - 1f, s.text, TextAlignmentOptions.MidlineRight, role: TextRole.Number);
            UIKit.Size(r.Value, width: 132f);
            r.Delta = UIKit.Text(row, "Delta", s, "", s.labelSize + 1f, s.textMuted, TextAlignmentOptions.MidlineLeft, role: TextRole.Number);
            UIKit.Size(r.Delta, width: 60f);
            rows.Add(r);

            var tile = row.gameObject.AddComponent<UITile>();
            tile.navigation = new Navigation { mode = Navigation.Mode.None };   // a click must not leave it selected and kill hover; revisit with gamepad nav
            tile.Setup(hit, null, null, hit.color, new Color(1f, 1f, 1f, 0.035f), Color.clear, s.accent);
            tile.HoverChanged += (_, on) => { if (screen.Tooltip != null) screen.Tooltip.Hover(screen, e, on); };
            tile.LeftClick += _ => { if (screen.Tooltip != null) screen.Tooltip.TogglePin(screen, e); };
        }

        private static void UpdateStat(StatRow r, Combat.Stats.StatContainer c, UIStyleSO s)
        {
            var stat = r.Entry.stat;
            float v = c != null ? c.Resolve(stat) : stat.Clamp(stat.defaultValue);
            if (v == r.Last) return;
            r.Last = v;

            float b = c != null ? c.GetBase(stat) : stat.defaultValue;
            float d = v - b;
            r.Value.text = StatFormat.Value(r.Entry, v);
            r.Delta.text = StatFormat.Delta(r.Entry, d);
            r.Delta.color = d > 0f != r.Entry.LowerIsBetter ? s.positive : s.negative;

            if (r.Fill != null)
            {
                var max = r.Fill.anchorMax;
                max.x = Mathf.Clamp01(v / r.Entry.barMax);
                r.Fill.anchorMax = max;
            }
        }

        // Armor effects that land on weapons, not the player.
        private void WeaponEffects(RectTransform body, InventoryScreen screen)
        {
            var s = screen.Style;
            effects.Clear();
            foreach (var a in screen.View.EquippedArmor())
                foreach (var p in a.AllPerks()) p.CollectStaticModifiers(effects);

            bool header = false;
            foreach (var e in effects)
            {
                if (e == null || e.target != ModifierTarget.Weapons) continue;
                if (!header) { GroupLabel(body, s, "Affects weapons"); header = true; }
                EffectLine(body, s, e);
            }
        }

        // ------------------------------------------------------------------ armor

        private void Armor(RectTransform body, InventoryScreen screen)
        {
            var s = screen.Style;
            var view = screen.View;
            Heading(body, s, "Armor");
            for (int i = 0; i < view.ArmorSlots.Count; i++) ArmorCard(body, screen, view.ArmorSlots[i], view.Armor[i]);

            Gap(body, s);
            Heading(body, s, "Active now");
            activeRoot = UIKit.Node(body, "Active");
            UIKit.VStack(activeRoot, 6f);

            if (Application.isPlaying) RebuildActive(screen);
            else UIKit.Text(activeRoot, "Hint", s, "Shown in play mode.", s.smallSize, s.textFaint);
        }

        private void ArmorCard(Transform parent, InventoryScreen owner, ArmorSlotSO slot, ArmorInstance piece)
        {
            var s = owner.Style;
            var card = Card(parent, "Armor_" + ItemText.SlotName(slot), s, piece);
            UIKit.VStack(card, 6f, UIKit.Pad(14f, 14f, 10f, 10f));

            var top = UIKit.Node(card.transform, "Top");
            UIKit.HStack(top, 12f);
            LoadoutSlotsPanel.Icon(top, s, piece, 40f);
            var info = UIKit.Node(top, "Info");
            UIKit.VStack(info, 2f, null, TextAnchor.MiddleLeft);
            UIKit.Size(info, flexWidth: 1f);
            UIKit.Label(info, "Slot", s, ItemText.SlotName(slot));
            UIKit.Text(info, "Name", s, piece != null ? ItemText.Name(piece) : "Empty", s.bodySize - 1f, piece != null ? ItemText.NameColor(s, piece, s.text) : s.textFaint,
                       role: piece != null ? TextRole.Title : TextRole.Body);
            if (piece == null) return;

            effects.Clear();
            foreach (var p in piece.AllPerks())
            {
                var row = UIKit.Node(card.transform, "Perk_" + p.name);
                UIKit.HStack(row, 10f);
                LoadoutSlotsPanel.Chip(row, s, p, owner, owner.ContextFor(piece));
                UIKit.Size(UIKit.Text(row, "Name", s, ItemText.PerkName(p), s.smallSize, s.text), flexWidth: 1f);
                p.CollectStaticModifiers(effects);
            }
            foreach (var e in effects) EffectLine(card.transform, s, e);
        }

        private void RebuildActive(InventoryScreen screen)
        {
            activeDirty = false;
            active.Clear();
            if (activeRoot == null) return;

            for (int i = activeRoot.childCount - 1; i >= 0; i--)
            {
                var c = activeRoot.GetChild(i);
                c.SetParent(null, false);   // out of the layout now, destroyed at frame end
                UIKit.Destroy(c.gameObject);
            }

            var s = screen.Style;
            var live = PerkRuntime.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var rt = live[i];
                if (rt.Detached || !rt.TryGetStatus(out var st) || !st.Active) continue;

                var row = UIKit.Node(activeRoot, "Active_" + (rt.Perk != null ? rt.Perk.name : "?"));
                UIKit.HStack(row, 10f, UIKit.Pad(0f, 0f, 2f, 2f));
                row.gameObject.AddComponent<CanvasGroup>().alpha = rt.InHand ? 1f : stowedAlpha;

                if (rt.Perk != null) LoadoutSlotsPanel.Chip(row, s, rt.Perk, screen, screen.ContextFor(rt.SourceItem));
                var info = UIKit.Node(row, "Info");
                UIKit.VStack(info, 1f, null, TextAnchor.MiddleLeft);
                UIKit.Size(info, flexWidth: 1f);
                UIKit.Text(info, "Name", s, ItemText.PerkName(rt.Perk), s.smallSize, s.text);
                UIKit.Text(info, "Source", s, ItemText.Name(rt.SourceItem), s.smallSize - 2f, s.textFaint);

                var text = UIKit.Text(row, "Status", s, "", s.smallSize, s.accent, TextAlignmentOptions.MidlineRight, role: TextRole.Number);
                UIKit.Size(text, width: 72f);
                var a = new ActiveRow { Status = st, Text = text };
                active.Add(a);
                Format(a);
            }

            if (active.Count == 0) UIKit.Text(activeRoot, "None", s, "Nothing active.", s.smallSize, s.textFaint);
        }

        private void UpdateTimers()
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i].Status.ExpiresAt > 0f) Format(active[i]);
        }

        private static void Format(ActiveRow a)
        {
            var st = a.Status;
            float left = st.ExpiresAt > 0f ? Mathf.Max(0f, st.ExpiresAt - Time.time) : 0f;
            int tenths = Mathf.CeilToInt(left * 10f);
            if (tenths == a.LastTenths) return;
            a.LastTenths = tenths;

            string t = "";
            if (st.Stacks > 1) t = "x" + st.Stacks;
            else if (st.CountMax > 0 && st.Stacks <= 0) t = st.Count + "/" + st.CountMax;
            if (st.ExpiresAt > 0f) t += (t.Length > 0 ? "  " : "") + (tenths / 10f).ToString("0.0") + "s";
            a.Text.text = t;
        }

        // ------------------------------------------------------------------ overview

        private static readonly string[] GroupOrder =
            { "Always", "On hit", "On precision hit", "On kill", "On precision kill", "Low health", "Magazine", "Weapon swap", "While stowed" };

        // Build summary: health, elements and keywords across the gear, and every perk grouped by when it acts.
        private void Overview(RectTransform body, InventoryScreen screen)
        {
            var s = screen.Style;
            var view = screen.View;
            Heading(body, s, "Build");

            var top = UIKit.Node(body, "HealthRow");
            UIKit.HStack(top, 10f);
            UIKit.Label(top, "Caption", s, "Health");
            healthText = UIKit.Text(top, "Health", s, "", s.bodySize, s.text, role: TextRole.Number);
            UIKit.Size(healthText, flexWidth: 1f);
            UIKit.Text(top, "Lock", s, view.Locked ? "Loadout locked" : "Loadout unlocked", s.smallSize, view.Locked ? s.negative : s.textFaint,
                       TextAlignmentOptions.MidlineRight);
            var bar = UIKit.Node(body, "HealthBar");
            UIKit.Size(bar, height: s.barHeight + 2f);
            UIKit.Stretch(UIKit.Box(bar, "Track", s, s.barTrack, small: true).rectTransform);
            healthFill = UIKit.Solid(bar, "Fill", s.positive).rectTransform;
            UIKit.Anchor(healthFill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var gear = new List<object>();
            foreach (var w in view.Weapons) if (w != null) gear.Add(w);
            foreach (var a in view.EquippedArmor()) gear.Add(a);

            var elements = Elements(body, screen, gear);
            Keywords(body, screen, gear, elements);
            PerkGroups(body, screen, gear);
        }

        // Every element the gear deals or applies, with how many sources.
        private static HashSet<KeywordSO> Elements(RectTransform body, InventoryScreen screen, List<object> gear)
        {
            var s = screen.Style;
            var counts = new Dictionary<DamageTypeSO, int>();
            var order = new List<DamageTypeSO>();
            var scratch = new List<DamageTypeSO>();
            void Count(DamageTypeSO t)
            {
                if (t == null) return;
                if (!counts.ContainsKey(t)) { counts[t] = 0; order.Add(t); }
                counts[t]++;
            }

            foreach (var item in gear)
            {
                if (item is WeaponInstance w && w.Definition != null) Count(w.Definition.baseDamageType);
                foreach (var p in PerksOf(item))
                {
                    scratch.Clear();
                    p.CollectElements(scratch);
                    foreach (var t in scratch) Count(t);
                }
            }

            Gap(body, s);
            GroupLabel(body, s, "Elements");
            var keywords = new HashSet<KeywordSO>();
            if (order.Count == 0) UIKit.Text(body, "None", s, "No elemental damage.", s.smallSize, s.textFaint);
            var ctx = screen.ContextFor(null);
            foreach (var t in order)
            {
                if (t.keyword != null) keywords.Add(t.keyword);
                string name = string.IsNullOrWhiteSpace(t.displayName) ? t.name : t.displayName;
                var row = InfoRow(body, s, name, t.color, "x" + counts[t]);
                InfoHover.Attach(row, screen, t, name, t.color, t.keyword != null ? t.keyword.description : null, null, ctx);
            }
            return keywords;
        }

        // Each keyword the equipped perks use, once, with its popup.
        private static void Keywords(RectTransform body, InventoryScreen screen, List<object> gear, HashSet<KeywordSO> skip)
        {
            var s = screen.Style;
            var found = new List<(KeywordLink link, DescribeContext ctx)>();
            var seen = new HashSet<KeywordSO>(skip);
            var links = new List<KeywordLink>();
            foreach (var item in gear)
            {
                var ctx = screen.ContextFor(item);
                foreach (var p in PerksOf(item))
                {
                    links.Clear();
                    Describer.Build(p, ctx, links);
                    foreach (var l in links)
                        if (l.Keyword != null && seen.Add(l.Keyword)) found.Add((l, ctx));
                }
            }

            Gap(body, s);
            GroupLabel(body, s, "Keywords");
            if (found.Count == 0) UIKit.Text(body, "None", s, "No keywords on your gear yet.", s.smallSize, s.textFaint);
            foreach (var (link, ctx) in found)
            {
                var k = link.Keyword;
                var row = InfoRow(body, s, k.Name, k.color, null);
                InfoHover.Attach(row, screen, k, k.Name, k.color, k.description, link.Detail, ctx);
            }
        }

        // Every equipped perk, grouped by trigger, with the item it comes from.
        private static void PerkGroups(RectTransform body, InventoryScreen screen, List<object> gear)
        {
            var s = screen.Style;
            var groups = new Dictionary<string, List<(PerkSO perk, object source)>>();
            var order = new List<string>(GroupOrder);
            foreach (var item in gear)
                foreach (var p in PerksOf(item))
                {
                    string g = string.IsNullOrWhiteSpace(p.TriggerGroup) ? "Always" : p.TriggerGroup;
                    if (!groups.TryGetValue(g, out var list)) { list = new List<(PerkSO, object)>(); groups[g] = list; }
                    list.Add((p, item));
                    if (!order.Contains(g)) order.Add(g);
                }

            Gap(body, s);
            Heading(body, s, "Perks");
            if (groups.Count == 0) UIKit.Text(body, "None", s, "No perks equipped.", s.smallSize, s.textFaint);
            foreach (var g in order)
            {
                if (!groups.TryGetValue(g, out var list)) continue;
                GroupLabel(body, s, g);
                foreach (var (perk, source) in list)
                {
                    var row = UIKit.Node(body, "Perk_" + perk.name);
                    UIKit.HStack(row, 10f, UIKit.Pad(8f, 0f, 1f, 1f));
                    LoadoutSlotsPanel.Chip(row, s, perk, screen, screen.ContextFor(source));
                    var text = UIKit.Node(row, "Text");
                    UIKit.VStack(text, 0f, null, TextAnchor.MiddleLeft);
                    UIKit.Size(text, flexWidth: 1f);
                    UIKit.Text(text, "Name", s, ItemText.PerkName(perk), s.smallSize, s.text);
                    UIKit.Text(text, "Source", s, ItemText.Name(source), s.smallSize - 2f, s.textFaint);
                }
            }
        }

        // Dot, name and an optional note on the right; returns the row's hit area for a hover popup.
        private static Image InfoRow(Transform parent, UIStyleSO s, string name, Color color, string note)
        {
            var row = UIKit.Node(parent, "Info_" + name);
            UIKit.HStack(row, 10f, UIKit.Pad(8f, 8f, 2f, 2f));
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            UIKit.Size(UIKit.Solid(row, "Dot", color), width: 8f, height: 8f);
            UIKit.Size(UIKit.Text(row, "Name", s, name, s.smallSize, color), flexWidth: 1f);
            if (!string.IsNullOrEmpty(note)) UIKit.Text(row, "Note", s, note, s.smallSize, s.textMuted, TextAlignmentOptions.MidlineRight, role: TextRole.Number);
            return hit;
        }

        private static IEnumerable<PerkSO> PerksOf(object item)
        {
            if (item is WeaponInstance w) { foreach (var p in w.AllPerks()) if (p != null) yield return p; }
            else if (item is ArmorInstance a) { foreach (var p in a.AllPerks()) if (p != null) yield return p; }
        }

        private void UpdateHealth(InventoryScreen screen)
        {
            if (healthText == null) return;
            var h = screen.PlayerHealth;
            if (h == null)
            {
                if (lastMax != 0f) { healthText.text = "-- / --"; lastMax = 0f; }
                return;
            }

            float cur = h.CurrentHealth, max = h.MaxHealth;
            if (cur == lastHealth && max == lastMax) return;
            lastHealth = cur;
            lastMax = max;

            healthText.text = Mathf.CeilToInt(cur) + " / " + Mathf.CeilToInt(max);
            var a = healthFill.anchorMax;
            a.x = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
            healthFill.anchorMax = a;
        }

        // ------------------------------------------------------------------ parts

        // Shaded item card with its rarity wash from the left; empty slots stay flat.
        private static Image Card(Transform parent, string name, UIStyleSO s, object item)
        {
            var card = UIKit.Box(parent, name, s, item != null ? s.tile : s.tileEmpty);
            if (item != null)
            {
                UIKit.Shade(card, s);
                UIKit.RarityWash(card.rectTransform, s, ItemText.RarityOf(item), horizontal: true);
            }
            UIKit.Outline(card.rectTransform, s, ItemText.BorderColor(s, item));
            return card;
        }

        private static void GroupLabel(Transform parent, UIStyleSO s, string label)
        {
            var wrap = UIKit.Node(parent, "Group");
            UIKit.VStack(wrap, 0f, UIKit.Pad(0f, 0f, 8f, 0f));
            UIKit.Label(wrap, "Text", s, label, s.textFaint);
        }

        private static void EffectLine(Transform parent, UIStyleSO s, ModifierEntry e)
        {
            var row = UIKit.Node(parent, "Effect");
            UIKit.HStack(row, 10f, UIKit.Pad(8f, 0f, 0f, 0f));
            UIKit.Size(UIKit.Solid(row, "Dot", ItemText.IsGood(e) ? s.positive : s.negative), width: 6f, height: 6f);
            UIKit.Size(UIKit.Text(row, "Text", s, ItemText.Effect(e), s.smallSize - 1f, s.textMuted, wrap: true), flexWidth: 1f);
        }

        private static void Gap(Transform parent, UIStyleSO s)
        {
            var gap = UIKit.Node(parent, "Gap");
            UIKit.Size(gap, height: s.sectionGap - s.gap);
        }
    }
}
