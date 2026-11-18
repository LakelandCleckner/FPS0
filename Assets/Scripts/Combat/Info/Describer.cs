using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Combat.Core;
using Combat.Events;
using Combat.Loadout;
using Combat.Sources;
using Combat.Stats;
using Combat.Status;
using Combat.Weapons;

namespace Combat.Info
{
    // Builds player-facing rich text: highlighted numbers, keyword links, and named tokens for custom templates.
    public sealed class Describer
    {
        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly Dictionary<string, string> tokens = new Dictionary<string, string>();

        public DescribeContext Context { get; }
        public List<KeywordLink> Links { get; }
        public string Result => sb.ToString();
        public bool IsEmpty => sb.Length == 0;

        public Describer(DescribeContext context, List<KeywordLink> links = null)
        {
            Context = context ?? new DescribeContext();
            Links = links ?? new List<KeywordLink>();
        }

        public static string Build(IDescribable source, DescribeContext context, List<KeywordLink> links = null)
        {
            if (source == null) return "";
            var d = new Describer(context, links);
            source.Describe(d);
            return d.Result.Trim();
        }

        // ------------------------------------------------------------------ text

        public Describer Add(string text)
        {
            if (!string.IsNullOrEmpty(text)) sb.Append(text);
            return this;
        }

        // Starts a new sentence: a space after the last one, if any.
        public Describer Next()
        {
            if (sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n') sb.Append(' ');
            return this;
        }

        public Describer Line()
        {
            if (sb.Length > 0) sb.Append('\n');
            return this;
        }

        public Describer Token(string key, string value)
        {
            if (!string.IsNullOrEmpty(key)) tokens[key] = value;
            return this;
        }

        // ------------------------------------------------------------------ numbers

        public Describer Num(string formatted, string token = null) => Colored(formatted, Context.Number, token);

        public Describer Percent(float fraction, string token = null) => Num(Pct(fraction), token);

        public Describer Seconds(float s, string token = null) => Num(Trim(s) + "s", token);

        public Describer Meters(float m, string token = null) => Num(Trim(m) + "m", token);

        public Describer Count(int n, string token = null) => Num(n.ToString(), token);

        // Percent of a stat, plus the live number when it's known: "20% weapon damage (8.4)".
        public Describer StatPercent(StatDefinitionSO stat, float coefficient, string token = null, StatScope owner = StatScope.Source)
        {
            string text = Pct(coefficient);
            if (stat == null) return Num(Trim(coefficient), token).Add(" damage");
            Num(text, token).Add(" " + OwnerWord(owner) + StatName(stat).ToLowerInvariant());
            if (Context.KnowsValue(stat)) Add(" (").Num(Trim(Context.DamageValue * coefficient)).Add(")");
            return this;
        }

        // Rate of a repeating hit: "10% weapon damage per second (4.2)".
        public Describer PerSecond(StatDefinitionSO stat, float coefficient, float interval, string token = null)
        {
            float rate = interval > 0f ? coefficient / interval : coefficient;
            return StatPercent(stat, rate, token).Add(" per second");
        }

        // A damage derivation in words.
        public Describer Damage(DerivationKind kind, StatDefinitionSO stat, QuantityKind quantity, StatScope owner, float coefficient, string token = null)
        {
            switch (kind)
            {
                case DerivationKind.Flat: return Num(Trim(coefficient), token).Add(" damage");
                case DerivationKind.PercentOfQuantity: return Num(Pct(coefficient), token).Add(" of " + OwnerWord(owner) + QuantityName(quantity));
                default: return StatPercent(stat, coefficient, token, owner);
            }
        }

        // "+20% Magazine Size", coloured by whether it helps.
        public Describer Modifier(ModifierEntry e, string token = null)
        {
            if (e == null || e.stat == null) return this;
            string text = ModifierText(e);
            return Colored(text, ModifierHelps(e) ? Context.Good : Context.Bad, token);
        }

        public Describer Modifiers(IList<ModifierEntry> list, string tokenPrefix = "mod")
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].stat == null) continue;
                if (n > 0) Add(", ");
                Modifier(list[i], tokenPrefix + (n + 1));
                n++;
            }
            if (n == 0) Add("no stat changes");
            return this;
        }

        // ------------------------------------------------------------------ keywords

        public Describer Keyword(KeywordSO keyword, IDescribable detail = null, string word = null)
        {
            if (keyword == null) return Add(word);
            string text = string.IsNullOrEmpty(word) ? keyword.Name : word;
            if (!Context.Rich) return Add(text);
            int index = Links.Count;
            Links.Add(new KeywordLink { Keyword = keyword, Detail = detail });
            sb.Append("<link=\"").Append(index).Append("\"><u><color=#").Append(ColorUtility.ToHtmlStringRGB(keyword.color)).Append('>')
              .Append(text).Append("</color></u></link>");
            return this;
        }

        // A built-in keyword from the library, else the plain word.
        public Describer Keyword(string id, string word, IDescribable detail = null)
        {
            var k = Context.Keywords != null ? Context.Keywords.Find(id) : null;
            return k != null ? Keyword(k, detail, word) : Add(word);
        }

        public Describer Element(DamageTypeSO type, string fallback = "")
        {
            if (type == null) return Add(fallback);
            string word = string.IsNullOrWhiteSpace(type.displayName) ? type.name : type.displayName;
            if (Context.Rich) Add(type.InlineIcon());   // tinted element icon before the name
            return type.keyword != null ? Keyword(type.keyword, null, word) : Add(word);
        }

        public Describer StatusLink(StatusSO status)
        {
            if (status == null) return this;
            string word = status.keyword != null ? status.keyword.Name : StatusName(status);
            if (Context.Rich && status.damageType != null) Add(status.damageType.InlineIcon());   // element icon before the status
            return status.keyword != null ? Keyword(status.keyword, status, word) : Add(word);
        }

        // ------------------------------------------------------------------ templates

        // Custom wording: {auto} is the generated text, {token} any value it named.
        public Describer Template(string template, Describer generated)
        {
            if (string.IsNullOrEmpty(template)) return Add(generated.Result);
            string text = Regex.Replace(template, "\\{([a-zA-Z0-9_]+)\\}", m =>
            {
                string key = m.Groups[1].Value;
                if (key == "auto") return generated.Result;
                return generated.tokens.TryGetValue(key, out var v) ? v : m.Value;
            });
            return Add(text);
        }

        // ------------------------------------------------------------------ helpers

        public static string Trigger(WeaponEventType t)
        {
            switch (t)
            {
                case WeaponEventType.Hit: return "On hit";
                case WeaponEventType.PrecisionHit: return "On precision hit";
                case WeaponEventType.Kill: return "On kill";
                case WeaponEventType.PrecisionKill: return "On precision kill";
                case WeaponEventType.ShotFired: return "On each shot";
                case WeaponEventType.DryFire: return "On dry fire";
                case WeaponEventType.ReloadStart: return "When a reload starts";
                case WeaponEventType.ReloadComplete: return "On reload";
                case WeaponEventType.ReloadCancelled: return "When a reload is cancelled";
                case WeaponEventType.MagEmpty: return "When the magazine empties";
                case WeaponEventType.MagFull: return "When the magazine is full";
                case WeaponEventType.AmmoChanged: return "When ammo changes";
                case WeaponEventType.Equip: return "On equip";
                case WeaponEventType.Stow: return "On stow";
                default: return "On " + t;
            }
        }

        // Trigger plus who counts: "On kill with any weapon (status kills count)".
        public Describer When(WeaponEventType t, HitSourceMask sources, bool anyWeapon)
        {
            Add(Trigger(t));
            if (anyWeapon) Add(" with any weapon");
            return Who(t, sources).Add(": ");
        }

        // Counted trigger: "Every 3 precision hits within 2s: ". One trigger falls back to When.
        public Describer Every(int n, WeaponEventType t, float window, HitSourceMask sources, bool anyWeapon)
        {
            if (n <= 1) return When(t, sources, anyWeapon);
            Add("Every ").Count(n, "count").Add(" " + Noun(t));
            if (window > 0f) Add(" within ").Seconds(window, "window");
            if (anyWeapon) Add(" with any weapon");
            return Who(t, sources).Add(": ");
        }

        // Which hit sources count, for hit-family triggers.
        private Describer Who(WeaponEventType t, HitSourceMask sources)
        {
            bool hitFamily = t == WeaponEventType.Hit || t == WeaponEventType.PrecisionHit || t == WeaponEventType.Kill || t == WeaponEventType.PrecisionKill;
            if (!hitFamily) return this;
            bool ticks = (sources & HitSourceMask.StatusTick) != 0, chains = (sources & HitSourceMask.Chain) != 0;
            if (ticks && chains) return Add(" (status and chain hits count)");
            if (ticks) return Add(" (status ticks count)");
            if (chains) return Add(" (chain hits count)");
            return this;
        }

        // Plural noun for a counted trigger: "shots", "precision hits".
        public static string Noun(WeaponEventType t)
        {
            switch (t)
            {
                case WeaponEventType.Hit: return "hits";
                case WeaponEventType.PrecisionHit: return "precision hits";
                case WeaponEventType.Kill: return "kills";
                case WeaponEventType.PrecisionKill: return "precision kills";
                case WeaponEventType.ShotFired: return "shots";
                case WeaponEventType.DryFire: return "dry fires";
                case WeaponEventType.ReloadStart: return "reloads started";
                case WeaponEventType.ReloadComplete: return "reloads";
                case WeaponEventType.ReloadCancelled: return "cancelled reloads";
                case WeaponEventType.MagEmpty: return "empty magazines";
                case WeaponEventType.Equip: return "equips";
                case WeaponEventType.Stow: return "stows";
                case WeaponEventType.DamageTaken: return "hits taken";
                default: return "triggers";
            }
        }

        public static string Pct(float fraction) => Trim(fraction * 100f) + "%";

        public static string Trim(float v)
        {
            float a = Mathf.Abs(v);
            string f = a >= 100f ? "0" : a >= 10f ? "0.#" : "0.##";
            return v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string StatName(StatDefinitionSO s) => s == null ? "" : string.IsNullOrWhiteSpace(s.displayName) ? s.name : s.displayName;

        public static string StatusName(StatusSO s) => s == null ? "" : s.keyword != null ? s.keyword.Name : s.name;

        public static string StatValue(StatDefinitionSO stat, float v)
        {
            if (stat != null && stat.isPercent) return Trim(v * 100f) + "%";
            int d = stat != null ? Mathf.Clamp(stat.displayDecimals, 0, 4) : 0;
            return d == 0 ? Mathf.Round(v).ToString("0") : Trim(v);
        }

        // "+10% Move Speed", "+5 Handling", "x1.1 Reload Time (Rifle)", "while held".
        public static string ModifierText(ModifierEntry e)
        {
            string stat = StatName(e.stat);
            string amount;
            switch (e.bucket)
            {
                case ModifierBucket.Additive:
                    amount = e.stat.resolutionMode == StatResolutionMode.ScaledBase ? Signed(Pct(e.value), e.value) : Signed(StatValue(e.stat, Mathf.Abs(e.value)), e.value);
                    break;
                case ModifierBucket.Multiplicative:
                    amount = "x" + Trim(1f + e.value);
                    break;
                default:
                    amount = Signed(StatValue(e.stat, Mathf.Abs(e.value)), e.value);
                    break;
            }

            string scope = "";
            if (e.target == ModifierTarget.Weapons && e.weaponTypes != null && e.weaponTypes.Count > 0)
            {
                var names = new List<string>();
                foreach (var t in e.weaponTypes) if (t != null) names.Add(string.IsNullOrWhiteSpace(t.displayName) ? t.name : t.displayName);
                scope = " (" + string.Join(", ", names) + ")";
            }
            if (e.scope == ActivityScope.InHands) scope += " while held";
            return amount + " " + stat + scope;
        }

        public static bool ModifierHelps(ModifierEntry e)
        {
            return (e.value >= 0f) != (e.stat != null && e.stat.lowerIsBetter);
        }

        // Strips rich text tags, for plain displays like the Inspector.
        public static string Plain(string rich) => string.IsNullOrEmpty(rich) ? "" : Regex.Replace(rich, "<[^>]+>", "");

        private Describer Colored(string text, Color c, string token)
        {
            if (token != null) Token(token, text);
            if (!Context.Rich) return Add(text);
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>').Append(text).Append("</color>");
            return this;
        }

        private static string Signed(string magnitude, float v)
        {
            string m = magnitude.TrimStart('-');
            return (v < 0f ? "-" : "+") + m;
        }

        private static string OwnerWord(StatScope owner)
        {
            switch (owner)
            {
                case StatScope.Target: return "the target's ";
                case StatScope.Attacker: return "the attacker's ";
                default: return "";
            }
        }

        private static string QuantityName(QuantityKind q)
        {
            switch (q)
            {
                case QuantityKind.MissingHealth: return "missing health";
                case QuantityKind.HealthFraction: return "health fraction";
                default: return "current health";
            }
        }
    }
}
