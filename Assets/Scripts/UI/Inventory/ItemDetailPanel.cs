using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Combat.Armor;
using Combat.Info;
using Combat.Loadout;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    // Everything about the focused item: stats, slot rules and perks. Keeps its scroll while the same item is shown.
    public class ItemDetailPanel : InventoryPanel
    {
        private readonly List<ModifierEntry> effects = new List<ModifierEntry>();
        private UnityEngine.UI.ScrollRect scroll;
        private object shownItem, savedItem;
        private float savedScroll = 1f;

        protected override void BeforeClear()
        {
            savedItem = shownItem;
            savedScroll = scroll != null ? scroll.verticalNormalizedPosition : 1f;
        }

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            Surface(root, s);
            var area = UIKit.Stretch(UIKit.Node(root, "Area"));
            var body = UIKit.Scroll(area, s.gap, UIKit.Pad(s.padding + 4f, s.padding + 4f, s.padding, s.padding));
            scroll = area.GetComponent<UnityEngine.UI.ScrollRect>();

            var item = screen.Focus;
            shownItem = item;
            if (item is WeaponInstance w && w.Definition != null) Weapon(body, screen, w);
            else if (item is ArmorInstance a && a.Definition != null) Armor(body, screen, a);
            else Empty(body, screen);

            if (item != null && item == savedItem && scroll != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(body);
                scroll.verticalNormalizedPosition = savedScroll;
            }
        }

        // ------------------------------------------------------------------ sections

        private static void Empty(RectTransform body, InventoryScreen screen)
        {
            var s = screen.Style;
            Heading(body, s, "Details");
            string hint = screen.Tab == InventoryTab.Vault ? "Hover or click an item to see it here. Double-click or press Store to move it."
                : "This slot is empty. Click a carried item to equip it.";
            UIKit.Text(body, "Hint", s, hint, s.smallSize, s.textMuted, wrap: true);
        }

        private void Weapon(RectTransform body, InventoryScreen screen, WeaponInstance w)
        {
            var s = screen.Style;
            var def = w.Definition;
            var type = ItemText.ElementType(w);
            Title(body, s, w, ItemText.Kind(w), ItemText.Element(w), ItemText.ElementColor(w, s.accent), type != null ? type.icon : null);

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

            Affixes(body, s, w.Affixes, w.Rarity, w.ItemLevel);

            var ctx = screen.ContextFor(w);
            Divider(body, s);
            Heading(body, s, "Perks");
            var intrinsics = def.ResolvedIntrinsics;
            foreach (var p in intrinsics) PerkRow(body, screen, p, "Intrinsic", ctx);

            var columns = def.ResolvedPerkColumns;
            bool any = false;
            for (int i = 0; i < w.Perks.Count; i++)
            {
                var p = w.Perks[i];
                if (p == null) continue;
                string col = columns != null && i < columns.Count && !string.IsNullOrWhiteSpace(columns[i].label)
                    ? columns[i].label : "Column " + (i + 1);
                PerkRow(body, screen, p, col, ctx);
                any = true;
            }
            if (!any && intrinsics.Count == 0) UIKit.Text(body, "None", s, "No perks.", s.smallSize, s.textFaint);
        }

        private void Armor(RectTransform body, InventoryScreen screen, ArmorInstance a)
        {
            var s = screen.Style;
            Title(body, s, a, ItemText.Kind(a), null);

            Affixes(body, s, a.Affixes, a.Rarity, a.ItemLevel);
            Effects(body, s, a, "Effects", CompareChip(screen));

            if (screen.Compare)
            {
                var other = screen.CompareTargetFor(a);
                if (other != null) Effects(body, s, other, "Equipped: " + ItemText.Name(other), null);
                else UIKit.Label(body, "CompareNote", s, "Nothing else equipped in this slot", s.textFaint);
            }

            var ctx = screen.ContextFor(a);
            Divider(body, s);
            Heading(body, s, "Perks");
            foreach (var p in a.Definition.intrinsicPerks) if (p != null) PerkRow(body, screen, p, "Intrinsic", ctx);
            var columns = a.Definition.ResolvedPerkColumns;
            for (int i = 0; i < a.Perks.Count; i++)
            {
                var p = a.Perks[i];
                if (p == null) continue;
                string col = columns != null && i < columns.Count && !string.IsNullOrWhiteSpace(columns[i].label)
                    ? columns[i].label : "Column " + (i + 1);
                PerkRow(body, screen, p, col, ctx);
            }
        }

        // Rolled stat lines, styled apart from perks: a bar in the affix colour, the stat line, and a Greater tag.
        private static void Affixes(RectTransform body, UIStyleSO s, IReadOnlyList<Combat.Affixes.AffixRoll> rolls, Rarity rarity, int level)
        {
            if (rolls == null || rolls.Count == 0) return;
            Divider(body, s);
            Heading(body, s, "Affixes");
            Combat.Affixes.AffixGroup? group = null;
            foreach (var roll in rolls)
            {
                if (roll == null || roll.affix == null) continue;
                if (group != roll.affix.group)
                {
                    group = roll.affix.group;
                    UIKit.Label(body, "Group", s, group == Combat.Affixes.AffixGroup.Damage ? "Damage"
                                : group == Combat.Affixes.AffixGroup.Element ? "Element"
                                : group == Combat.Affixes.AffixGroup.Defense ? "Defense" : "Stats", s.textFaint);
                }
                var e = roll.affix.Entry(roll.Value(rarity, level));
                var row = UIKit.Node(body, "Affix");
                UIKit.HStack(row, 10f);
                UIKit.Size(row, height: s.bodySize + 6f);
                var mark = UIKit.Solid(row, "Mark", s.barAffix);
                UIKit.Size(mark, width: 3f, height: s.bodySize);
                bool weaponLine = roll.affix.target == ModifierTarget.Weapons && roll.affix.targetWeaponTypes.Count == 0;
                UIKit.Size(UIKit.Text(row, "Text", s, ItemText.Effect(e, !weaponLine), s.bodySize - 1f, ItemText.IsGood(e) ? s.text : s.negative),
                           flexWidth: 1f);
                if (roll.greater)
                {
                    var tag = UIKit.Text(row, "Greater", s, "Greater", s.labelSize, s.accent, TextAlignmentOptions.MidlineRight, heading: true);
                    UIKit.Size(tag, width: 70f);
                }
            }
        }

        // Static stat changes from an armor piece's perks.
        private void Effects(RectTransform body, UIStyleSO s, ArmorInstance a, string title, System.Action<RectTransform> decorate)
        {
            effects.Clear();
            foreach (var p in a.AllPerks()) p.CollectStaticModifiers(effects);
            Divider(body, s);
            var head = Heading(body, s, title);
            decorate?.Invoke(head);
            if (effects.Count == 0) { UIKit.Text(body, "None", s, "No stat changes.", s.smallSize, s.textFaint); return; }
            foreach (var e in effects)
            {
                var row = UIKit.Node(body, "Effect");
                UIKit.HStack(row, 10f);
                var dot = UIKit.Solid(row, "Dot", ItemText.IsGood(e) ? s.positive : s.negative);
                UIKit.Size(dot, width: 6f, height: 6f);
                UIKit.Size(UIKit.Text(row, "Text", s, ItemText.Effect(e), s.bodySize - 2f, s.text), flexWidth: 1f);
            }
        }

        // Toggle on a heading row: compare against the equipped item. Off by default.
        private static System.Action<RectTransform> CompareChip(InventoryScreen screen) => head =>
        {
            var s = screen.Style;
            bool on = screen.Compare;
            var fill = UIKit.Box(head, "Compare", s, on ? new Color(s.accent.r, s.accent.g, s.accent.b, 0.18f) : s.tileEmpty, small: true);
            var border = UIKit.Outline(fill.rectTransform, s, on ? s.accent : s.tileBorder, small: true);
            UIKit.HStack(fill, 0f, UIKit.Pad(10f, 10f, 3f, 3f), TextAnchor.MiddleCenter);
            UIKit.Size(fill, height: s.chipSize);
            var t = UIKit.Text(fill.transform, "Text", s, "Compare", s.labelSize, on ? s.accent : s.textMuted, TextAlignmentOptions.Center, heading: true);
            t.characterSpacing = 1f;
            var tile = fill.gameObject.AddComponent<UITile>();
            tile.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            tile.Setup(fill, border, null, fill.color, s.tileHover, border.color, s.accent);
            tile.LeftClick += _ => screen.ToggleCompare();
        };

        // ------------------------------------------------------------------ stats

        private static void StatBlock(RectTransform body, InventoryScreen screen, WeaponInstance w)
        {
            var s = screen.Style;
            var sheet = screen.WeaponStatSheet;

            var head = Heading(body, s, "Stats");
            Legend(head, s, "Affixes", s.barAffix);
            Legend(head, s, "Perks", s.barPerk);
            Legend(head, s, "Armor", s.barArmor);
            Legend(head, s, "Attributes", s.barAttribute);
            CompareChip(screen)(head);

            WeaponStatPreview compare = null;
            if (screen.Compare)
            {
                var other = screen.CompareTargetFor(w);
                if (other != null)
                {
                    compare = screen.ComparePreviewFor(other);
                    UIKit.Label(body, "CompareNote", s, "Last column: vs " + ItemText.Name(other), s.accent);
                }
                else UIKit.Label(body, "CompareNote", s, "Nothing else equipped to compare with", s.textFaint);
            }

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
                    if (e != null && e.stat != null)
                        StatRow(body, s, e, preview.Get(e.stat), compare != null && compare.IsBuilt ? compare.Get(e.stat) : (StatLayers?)null);
            }
        }

        private static void StatRow(Transform parent, UIStyleSO s, StatSheetSO.Entry e, StatLayers v, StatLayers? other)
        {
            var row = UIKit.Node(parent, "Stat_" + e.Label);
            UIKit.HStack(row, 14f);
            UIKit.Size(row, height: s.bodySize + 8f);

            UIKit.Size(UIKit.Text(row, "Label", s, e.Label, s.smallSize, s.textMuted), width: 150f);

            float delta = v.Final - v.Base;
            bool better = delta > 0f != e.LowerIsBetter;

            if (e.display == StatDisplay.Bar)
            {
                var bar = UIKit.Node(row, "Bar");
                UIKit.Size(bar, height: s.barHeight, flexWidth: 1f);
                DrawBar(bar, s, v, e.barMax);
            }
            else UIKit.Spacer(row);

            var value = UIKit.Text(row, "Value", s, StatFormat.Value(e, v.Final), s.bodySize - 1f, s.text, TextAlignmentOptions.MidlineRight,
                                   role: TextRole.Number);
            UIKit.Size(value, width: 72f);

            var d = UIKit.Text(row, "Delta", s, StatFormat.Delta(e, delta), s.labelSize + 1f, better ? s.positive : s.negative,
                               TextAlignmentOptions.MidlineLeft, role: TextRole.Number);
            UIKit.Size(d, width: 60f);

            if (!other.HasValue) return;
            float vs = v.Final - other.Value.Final;
            bool wins = vs > 0f != e.LowerIsBetter;
            var c = UIKit.Text(row, "Compare", s, Mathf.Abs(vs) < 0.0005f ? "=" : StatFormat.Delta(e, vs), s.smallSize,
                               Mathf.Abs(vs) < 0.0005f ? s.textFaint : wins ? s.positive : s.negative, TextAlignmentOptions.MidlineRight, role: TextRole.Number);
            UIKit.Size(c, width: 70f);
        }

        // Base fill, then affix, perk, armor and attribute gains, then any loss carved back out.
        private static void DrawBar(RectTransform bar, UIStyleSO s, StatLayers v, float max)
        {
            var track = UIKit.Box(bar, "Track", s, s.barTrack, small: true);
            UIKit.Stretch(track.rectTransform);

            float b = Mathf.Clamp01(v.Base / max);
            float f = Mathf.Clamp01(v.Final / max);
            float top = Mathf.Max(f, b);

            Segment(bar, "Base", s.barBase, 0f, Mathf.Min(b, f));
            float reached = b;
            reached = Gain(bar, "Affixes", s.barAffix, reached, Mathf.Clamp01(v.WithAffixes / max), top);
            reached = Gain(bar, "Perks", s.barPerk, reached, Mathf.Clamp01(v.WithPerks / max), top);
            reached = Gain(bar, "Armor", s.barArmor, reached, Mathf.Clamp01(v.WithArmor / max), top);
            Gain(bar, "Attributes", s.barAttribute, reached, f, top);
            if (f < b) Segment(bar, "Loss", s.barLoss, f, b);
        }

        // A layer's gain past what the earlier layers reached, kept under the final fill.
        private static float Gain(RectTransform bar, string name, Color color, float reached, float value, float top)
        {
            if (value <= reached) return reached;
            Segment(bar, name, color, reached, Mathf.Min(value, top));
            return value;
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

        // Banner with the rarity wash: name in its rarity colour, the kind line led by the rarity.
        private static void Title(RectTransform body, UIStyleSO s, object item, string kind, string element, Color elementColor = default,
                                  Sprite elementIcon = null)
        {
            var rarity = ItemText.RarityOf(item);
            var banner = UIKit.Box(body, "TitleBlock", s, s.backdrop);
            UIKit.RarityWash(banner.rectTransform, s, rarity, horizontal: true);
            UIKit.Outline(banner.rectTransform, s, ItemText.BorderColor(s, item));
            var block = banner.rectTransform;
            UIKit.VStack(block, 6f, UIKit.Pad(18f, 18f, 16f, 14f));
            UIKit.Text(block, "Name", s, ItemText.Name(item), s.titleSize - 4f, ItemText.NameColor(s, item, s.text), lines: 2, role: TextRole.Title);
            if (rarity.HasValue) kind = rarity.Value + "  /  " + kind;

            var line = UIKit.Node(block, "Kind");
            UIKit.HStack(line, 10f);
            UIKit.Text(line, "Text", s, kind, s.smallSize + 1f, s.textMuted);
            if (!string.IsNullOrEmpty(element))
            {
                // Pill in the damage type's colour
                var c = elementColor.a > 0f ? elementColor : s.accent;
                var pill = UIKit.Box(line, "Element", s, new Color(c.r, c.g, c.b, 0.12f), true,
                                     new Color(c.r, c.g, c.b, 0.5f), small: true);
                UIKit.HStack(pill, 5f, UIKit.Pad(elementIcon != null ? 6f : 10f, 10f, 3f, 3f), TextAnchor.MiddleCenter);
                UIKit.Icon(pill.transform, "Icon", elementIcon, c, s.labelSize + 6f);
                UIKit.Text(pill.transform, "Text", s, element, s.labelSize, c, TextAlignmentOptions.Center, heading: true);
            }

            var level = ItemText.LevelOf(item);
            if (level.HasValue) UIKit.Text(block, "Level", s, "Item Level " + level.Value, s.smallSize + 1f, s.text);
        }

        private static void SlotChip(Transform parent, UIStyleSO s, string label, bool on)
        {
            var chip = UIKit.Box(parent, "Slot_" + label, s, on ? new Color(s.accent.r, s.accent.g, s.accent.b, 0.16f) : s.tileEmpty,
                                 true, on ? s.accent : s.tileBorder, small: true);
            UIKit.Size(chip, width: s.chipSize, height: s.chipSize);
            var t = UIKit.Text(chip.transform, "Text", s, label, s.labelSize, on ? s.accent : s.textFaint, TextAlignmentOptions.Center, heading: true);
            t.characterSpacing = 0f;
            UIKit.Stretch(t.rectTransform);
        }

        // Icon, column, name, and the generated description with live keyword links.
        private static void PerkRow(Transform parent, InventoryScreen screen, PerkSO perk, string column, DescribeContext ctx)
        {
            var s = screen.Style;
            var row = UIKit.Node(parent, "Perk_" + perk.name);
            UIKit.HStack(row, 14f, UIKit.Pad(0f, 0f, 4f, 4f), TextAnchor.UpperLeft);

            bool elemental = ItemText.PerkElement(perk, out var type);
            var element = elemental ? type.color : default;
            var icon = UIKit.Box(row, "Icon", s, s.tileHover, true, elemental ? element : (Color?)null, small: true);
            UIKit.Size(icon, width: s.perkIconSize, height: s.perkIconSize);
            if (elemental) UIKit.Badge(icon.rectTransform, "Element", type.icon, element, s.perkIconSize * 0.42f, new Vector2(1f, 0f), -4f);
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
                var t = UIKit.Text(icon.transform, "Short", s, perk.ShortLabel, s.smallSize, elemental ? element : s.text, TextAlignmentOptions.Center, heading: true);
                t.characterSpacing = 0f;
                UIKit.Stretch(t.rectTransform);
            }

            var text = UIKit.Node(row, "Text");
            UIKit.VStack(text, 3f);
            UIKit.Size(text, flexWidth: 1f);
            UIKit.Label(text, "Column", s, column, s.textFaint);
            UIKit.Text(text, "Name", s, ItemText.PerkName(perk), s.bodySize, elemental ? element : s.text, role: TextRole.Title);
            var links = new List<KeywordLink>();
            string desc = Describer.Build(perk, ctx, links);
            if (string.IsNullOrWhiteSpace(desc)) return;
            var d = UIKit.Text(text, "Description", s, desc, s.smallSize, s.textMuted, wrap: true);
            d.richText = true;
            if (links.Count > 0) d.gameObject.AddComponent<TextLinkHover>().Setup(screen, links, ctx);
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
