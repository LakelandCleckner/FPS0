using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Combat.Armor;
using Combat.Loadout;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    // Everything about the hovered item, or the selected slot's item: stats, slot rules and perks.
    public class ItemDetailPanel : InventoryPanel
    {
        private readonly List<ModifierEntry> effects = new List<ModifierEntry>();

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            Surface(root, s);
            var area = UIKit.Stretch(UIKit.Node(root, "Area"));
            var body = UIKit.Scroll(area, s.gap, UIKit.Pad(s.padding + 4f, s.padding + 4f, s.padding, s.padding));

            var item = screen.Focus;
            if (item is WeaponInstance w && w.Definition != null) Weapon(body, screen, w);
            else if (item is ArmorInstance a && a.Definition != null) Armor(body, screen, a);
            else Empty(body, screen);
        }

        // ------------------------------------------------------------------ sections

        private static void Empty(RectTransform body, InventoryScreen screen)
        {
            var s = screen.Style;
            Heading(body, s, "Details");
            UIKit.Text(body, "Hint", s, "This slot is empty. Hover a carried item to see it here, then click it to equip.",
                       s.smallSize, s.textMuted, wrap: true);
        }

        private void Weapon(RectTransform body, InventoryScreen screen, WeaponInstance w)
        {
            var s = screen.Style;
            var def = w.Definition;
            Title(body, s, ItemText.Name(w), ItemText.Kind(w), ItemText.Element(w));

            // Slot rules
            var rules = UIKit.Node(body, "Slots");
            UIKit.HStack(rules, 6f);
            UIKit.Size(UIKit.Label(rules, "Caption", s, "Fits"), width: 44f);
            for (int i = 0; i < LoadoutSlots.Count; i++)
                SlotChip(rules, s, (i + 1).ToString(), LoadoutSlots.Allows(def.AllowedSlots, i));
            UIKit.Spacer(rules);
            UIKit.Label(rules, "Carry", s, "Carried in slot " + (def.InventorySlot + 1), s.textFaint);

            Divider(body, s);
            StatBlock(body, screen, w);

            Divider(body, s);
            Heading(body, s, "Perks");
            foreach (var p in def.intrinsicPerks) if (p != null) PerkRow(body, s, p, "Intrinsic");

            var columns = def.ResolvedPerkColumns;
            bool any = false;
            for (int i = 0; i < w.Perks.Count; i++)
            {
                var p = w.Perks[i];
                if (p == null) continue;
                string col = columns != null && i < columns.Count && !string.IsNullOrWhiteSpace(columns[i].label)
                    ? columns[i].label : "Column " + (i + 1);
                PerkRow(body, s, p, col);
                any = true;
            }
            if (!any && def.intrinsicPerks.Count == 0) UIKit.Text(body, "None", s, "No perks.", s.smallSize, s.textFaint);
        }

        private void Armor(RectTransform body, InventoryScreen screen, ArmorInstance a)
        {
            var s = screen.Style;
            Title(body, s, ItemText.Name(a), ItemText.Kind(a), null);

            effects.Clear();
            foreach (var p in a.AllPerks()) p.CollectStaticModifiers(effects);
            if (effects.Count > 0)
            {
                Divider(body, s);
                Heading(body, s, "Effects");
                foreach (var e in effects)
                {
                    var row = UIKit.Node(body, "Effect");
                    UIKit.HStack(row, 10f);
                    var dot = UIKit.Solid(row, "Dot", ItemText.IsGood(e) ? s.positive : s.negative);
                    UIKit.Size(dot, width: 6f, height: 6f);
                    UIKit.Size(UIKit.Text(row, "Text", s, ItemText.Effect(e), s.bodySize - 2f, s.text), flexWidth: 1f);
                }
            }

            Divider(body, s);
            Heading(body, s, "Perks");
            foreach (var p in a.Definition.intrinsicPerks) if (p != null) PerkRow(body, s, p, "Intrinsic");
            var columns = a.Definition.ResolvedPerkColumns;
            for (int i = 0; i < a.Perks.Count; i++)
            {
                var p = a.Perks[i];
                if (p == null) continue;
                string col = columns != null && i < columns.Count && !string.IsNullOrWhiteSpace(columns[i].label)
                    ? columns[i].label : "Column " + (i + 1);
                PerkRow(body, s, p, col);
            }
        }

        // ------------------------------------------------------------------ stats

        private static void StatBlock(RectTransform body, InventoryScreen screen, WeaponInstance w)
        {
            var s = screen.Style;
            var sheet = screen.WeaponStatSheet;

            var head = Heading(body, s, "Stats");
            Legend(head, s, "Perks", s.barPerk);
            Legend(head, s, "Armour", s.barArmor);

            var preview = screen.PreviewFor(w);
            if (sheet == null || !preview.IsBuilt)
            {
                UIKit.Text(body, "NoStats", s, sheet == null ? "Assign a weapon stat sheet on the inventory screen."
                           : "Stats unavailable: no StatBootstrap or WeaponStatKeys.", s.smallSize, s.textFaint, wrap: true);
                return;
            }

            foreach (var group in sheet.groups)
            {
                if (!string.IsNullOrWhiteSpace(group.label)) UIKit.Label(body, "Group", s, group.label, s.textFaint);
                foreach (var e in group.entries)
                    if (e != null && e.stat != null) StatRow(body, s, e, preview.Get(e.stat));
            }
        }

        private static void StatRow(Transform parent, UIStyleSO s, StatSheetSO.Entry e, StatLayers v)
        {
            var row = UIKit.Node(parent, "Stat_" + e.Label);
            UIKit.HStack(row, 14f);
            UIKit.Size(row, height: s.bodySize + 8f);

            UIKit.Size(UIKit.Text(row, "Label", s, e.Label, s.smallSize, s.textMuted), width: 150f);

            float delta = v.WithArmor - v.Base;
            bool better = delta > 0f != e.LowerIsBetter;

            if (e.display == StatDisplay.Bar)
            {
                var bar = UIKit.Node(row, "Bar");
                UIKit.Size(bar, height: s.barHeight, flexWidth: 1f);
                DrawBar(bar, s, v, e.barMax);
            }
            else UIKit.Spacer(row);

            var value = UIKit.Text(row, "Value", s, StatFormat.Value(e, v.WithArmor), s.bodySize - 1f, s.text, TextAlignmentOptions.MidlineRight);
            UIKit.Size(value, width: 72f);

            var d = UIKit.Text(row, "Delta", s, StatFormat.Delta(e, delta), s.labelSize + 1f, better ? s.positive : s.negative,
                               TextAlignmentOptions.MidlineLeft);
            UIKit.Size(d, width: 52f);
        }

        // Base fill, then perk and armour gains, then any loss carved back out.
        private static void DrawBar(RectTransform bar, UIStyleSO s, StatLayers v, float max)
        {
            var track = UIKit.Box(bar, "Track", s, s.barTrack);
            UIKit.Stretch(track.rectTransform);

            float b = Mathf.Clamp01(v.Base / max);
            float p = Mathf.Clamp01(v.WithPerks / max);
            float f = Mathf.Clamp01(v.WithArmor / max);

            Segment(bar, "Base", s.barBase, 0f, Mathf.Min(b, f));
            if (p > b) Segment(bar, "Perks", s.barPerk, b, Mathf.Min(p, Mathf.Max(f, b)));
            if (f > Mathf.Max(p, b)) Segment(bar, "Armour", s.barArmor, Mathf.Max(p, b), f);
            if (f < b) Segment(bar, "Loss", s.barLoss, f, b);
        }

        private static void Segment(RectTransform bar, string name, Color color, float from, float to)
        {
            if (to - from <= 0.0005f) return;
            var seg = UIKit.Solid(bar, name, color);
            UIKit.Anchor(seg.rectTransform, new Vector2(from, 0f), new Vector2(to, 1f), Vector2.zero, Vector2.zero);
        }

        private static void Legend(RectTransform heading, UIStyleSO s, string label, Color color)
        {
            var swatch = UIKit.Solid(heading, "Swatch_" + label, color);
            UIKit.Size(swatch, width: 10f, height: 4f);
            UIKit.Label(heading, "Legend_" + label, s, label, s.textFaint);
        }

        // ------------------------------------------------------------------ parts

        private static void Title(RectTransform body, UIStyleSO s, string name, string kind, string element)
        {
            var block = UIKit.Node(body, "TitleBlock");
            UIKit.VStack(block, 6f);
            UIKit.Text(block, "Name", s, name, s.titleSize - 4f, s.text, heading: true);

            var line = UIKit.Node(block, "Kind");
            UIKit.HStack(line, 10f);
            UIKit.Text(line, "Text", s, kind, s.smallSize + 1f, s.textMuted);
            if (!string.IsNullOrEmpty(element))
            {
                var pill = UIKit.Box(line, "Element", s, new Color(s.accent.r, s.accent.g, s.accent.b, 0.12f), true,
                                     new Color(s.accent.r, s.accent.g, s.accent.b, 0.5f));
                UIKit.HStack(pill, 0f, UIKit.Pad(10f, 10f, 3f, 3f), TextAnchor.MiddleCenter);
                UIKit.Text(pill.transform, "Text", s, element, s.labelSize, s.accent, TextAlignmentOptions.Center, heading: true);
            }
        }

        private static void SlotChip(Transform parent, UIStyleSO s, string label, bool on)
        {
            var chip = UIKit.Box(parent, "Slot_" + label, s, on ? new Color(s.accent.r, s.accent.g, s.accent.b, 0.16f) : s.tileEmpty,
                                 true, on ? s.accent : s.tileBorder);
            UIKit.Size(chip, width: s.chipSize, height: s.chipSize);
            var t = UIKit.Text(chip.transform, "Text", s, label, s.labelSize, on ? s.accent : s.textFaint, TextAlignmentOptions.Center, heading: true);
            t.characterSpacing = 0f;
            UIKit.Stretch(t.rectTransform);
        }

        private static void PerkRow(Transform parent, UIStyleSO s, PerkSO perk, string column)
        {
            var row = UIKit.Node(parent, "Perk_" + perk.name);
            UIKit.HStack(row, 14f, UIKit.Pad(0f, 0f, 4f, 4f), TextAnchor.UpperLeft);

            var icon = UIKit.Box(row, "Icon", s, s.tileHover, true);
            UIKit.Size(icon, width: s.perkIconSize, height: s.perkIconSize);
            if (perk.icon != null)
            {
                var img = UIKit.Node(icon.transform, "Art").gameObject.AddComponent<UnityEngine.UI.Image>();
                img.sprite = perk.icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
                UIKit.Stretch(img.rectTransform, 6f, 6f, 6f, 6f);
            }
            else
            {
                var t = UIKit.Text(icon.transform, "Short", s, perk.ShortLabel, s.smallSize, s.text, TextAlignmentOptions.Center, heading: true);
                t.characterSpacing = 0f;
                UIKit.Stretch(t.rectTransform);
            }

            var text = UIKit.Node(row, "Text");
            UIKit.VStack(text, 3f);
            UIKit.Size(text, flexWidth: 1f);
            UIKit.Label(text, "Column", s, column, s.textFaint);
            UIKit.Text(text, "Name", s, ItemText.PerkName(perk), s.bodySize, s.text);
            if (!string.IsNullOrWhiteSpace(perk.description))
                UIKit.Text(text, "Description", s, perk.description, s.smallSize, s.textMuted, wrap: true);
        }

        private static void Divider(Transform parent, UIStyleSO s)
        {
            var wrap = UIKit.Node(parent, "Divider");
            UIKit.Size(wrap, height: s.sectionGap);
            var line = UIKit.Solid(wrap, "Line", s.divider);
            UIKit.Anchor(line.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -0.5f), new Vector2(0f, 0.5f));
        }
    }
}
