using System.Collections.Generic;
using System.Text;
using Combat.Armor;
using Combat.Loadout;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.UI
{
    // Display strings for owned items and their perks.
    public static class ItemText
    {
        public static string Name(object item)
        {
            if (item is WeaponInstance w && w.Definition != null) return Pick(w.Definition.displayName, w.Definition.name);
            if (item is ArmorInstance a && a.Definition != null) return Pick(a.Definition.displayName, a.Definition.name);
            return "";
        }

        public static string Kind(object item)
        {
            if (item is WeaponInstance w && w.Definition != null)
            {
                var arch = w.Definition.archetype;
                var type = arch != null ? arch.weaponType : null;
                string t = type != null ? Pick(type.displayName, type.name) : "Weapon";
                string a = arch != null ? Pick(arch.displayName, arch.name) : "";
                return string.IsNullOrEmpty(a) ? t : t + "  /  " + a;
            }
            if (item is ArmorInstance ar && ar.Slot != null) return Pick(ar.Slot.displayName, ar.Slot.name) + " armor";
            return "";
        }

        public static string SlotName(ArmorSlotSO slot) => slot != null ? Pick(slot.displayName, slot.name) : "";

        public static string Element(WeaponInstance w)
        {
            var t = w != null && w.Definition != null ? w.Definition.baseDamageType : null;
            return t != null ? Pick(t.displayName, t.name) : "";
        }

        // Two or three letters for a tile with no icon art yet.
        public static string Monogram(object item)
        {
            string n = Name(item);
            var sb = new StringBuilder(3);
            foreach (var word in n.Split(' ', '_', '-'))
                if (word.Length > 0 && sb.Length < 3 && char.IsLetterOrDigit(word[0])) sb.Append(char.ToUpperInvariant(word[0]));
            if (sb.Length == 1 && n.Length > 1) sb.Append(char.ToUpperInvariant(n[1]));
            return sb.ToString();
        }

        public static string PerkName(PerkSO p) => p != null ? Pick(p.displayName, p.name) : "";

        // "+10% Move Speed", "+5 Handling", "x1.2 Reload Time".
        public static string Effect(ModifierEntry e)
        {
            if (e == null || e.stat == null) return "";
            string stat = Pick(e.stat.displayName, e.stat.name);
            string amount;
            switch (e.bucket)
            {
                case ModifierBucket.Additive:
                    amount = e.stat.resolutionMode == StatResolutionMode.ScaledBase
                        ? Signed(e.value * 100f, "0.#") + "%"
                        : SignedStat(e.stat, e.value);
                    break;
                case ModifierBucket.Multiplicative:
                    amount = "x" + (1f + e.value).ToString("0.##");
                    break;
                default:
                    amount = SignedStat(e.stat, e.value);
                    break;
            }

            string scope = "";
            if (e.target == ModifierTarget.Weapons)
            {
                if (e.weaponTypes != null && e.weaponTypes.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var t in e.weaponTypes) if (t != null) names.Add(Pick(t.displayName, t.name));
                    scope = "  (" + string.Join(", ", names) + ")";
                }
                else scope = "  (weapons)";
            }
            if (e.scope == ActivityScope.InHands && e.target == ModifierTarget.Weapons) scope += "  while held";
            return amount + " " + stat + scope;
        }

        // Does this effect help? Uses the stat's lower-is-better flag.
        public static bool IsGood(ModifierEntry e)
            => e != null && (e.value >= 0f) != (e.stat != null && e.stat.lowerIsBetter);

        private static string SignedStat(StatDefinitionSO stat, float v)
        {
            string s = StatFormat.Value(stat, UnityEngine.Mathf.Abs(v));
            return (v < 0f ? "-" : "+") + s;
        }

        private static string Signed(float v, string format) => (v < 0f ? "-" : "+") + UnityEngine.Mathf.Abs(v).ToString(format);

        private static string Pick(string preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }
}
