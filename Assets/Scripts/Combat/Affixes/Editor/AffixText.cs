using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Affixes.EditorTools
{
    // One line per affix, for batch import and the migration:
    //   Name | stat kind values | rolls on and options
    //   kind: flat, pct, mult.  values: c,u,r,l,m by rarity, or "lvl 12" for an item level base.
    //   options: weapons, armor, types=A,B, archetypes=a,b, slots=a,b, weight=1, player, held, boosts=A,B
    public static class AffixText
    {
        public const string Folder = "Assets/GameData/Affixes";

        public sealed class Parsed
        {
            public string Name, Line;
            public StatDefinitionSO Stat;
            public ModifierBucket Bucket;
            public AffixValueSource Source;
            public float[] Values = new float[5];
            public float Base;
            public bool OnWeapons, OnArmor, Player, Held;
            public float Weight = 1f;
            public List<WeaponTypeSO> Types = new List<WeaponTypeSO>();
            public List<WeaponArchetypeSO> Archetypes = new List<WeaponArchetypeSO>();
            public List<ArmorSlotSO> Slots = new List<ArmorSlotSO>();
            public List<WeaponTypeSO> Boosts = new List<WeaponTypeSO>();
        }

        private static Dictionary<string, StatDefinitionSO> stats;
        private static Dictionary<string, WeaponTypeSO> types;
        private static Dictionary<string, WeaponArchetypeSO> archetypes;
        private static Dictionary<string, ArmorSlotSO> slots;

        public static List<Parsed> Parse(string text, List<string> errors)
        {
            LoadLookups();
            var list = new List<Parsed>();
            var names = new HashSet<string>();
            int n = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                n++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                var p = ParseLine(line, n, errors);
                if (p == null) continue;
                if (!names.Add(p.Name.ToLowerInvariant())) { errors.Add($"Line {n}: '{p.Name}' appears twice."); continue; }
                list.Add(p);
            }
            return list;
        }

        private static Parsed ParseLine(string line, int n, List<string> errors)
        {
            var parts = line.Split('|');
            if (parts.Length < 2) { errors.Add($"Line {n}: needs at least 'Name | stat kind values'."); return null; }
            var p = new Parsed { Name = parts[0].Trim(), Line = line };
            if (p.Name.Length == 0) { errors.Add($"Line {n}: no name."); return null; }

            var mod = parts[1].Trim().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (mod.Length < 3) { errors.Add($"Line {n}: expected 'stat kind values'."); return null; }
            if (!stats.TryGetValue(mod[0].ToLowerInvariant(), out p.Stat)) { errors.Add($"Line {n}: unknown stat '{mod[0]}'."); return null; }
            switch (mod[1].ToLowerInvariant())
            {
                case "flat": p.Bucket = ModifierBucket.Flat; break;
                case "pct": p.Bucket = ModifierBucket.Additive; break;
                case "mult": p.Bucket = ModifierBucket.Multiplicative; break;
                default: errors.Add($"Line {n}: kind must be flat, pct or mult."); return null;
            }
            if (mod[2].ToLowerInvariant() == "lvl")
            {
                if (mod.Length < 4 || !TryFloat(mod[3], out p.Base)) { errors.Add($"Line {n}: 'lvl' needs a base value."); return null; }
                p.Source = AffixValueSource.ItemLevel;
            }
            else
            {
                var vals = mod[2].Split(',');
                if (vals.Length != 5) { errors.Add($"Line {n}: needs 5 rarity values (Common to Mythic)."); return null; }
                for (int i = 0; i < 5; i++)
                    if (!TryFloat(vals[i], out p.Values[i])) { errors.Add($"Line {n}: bad value '{vals[i]}'."); return null; }
                p.Source = AffixValueSource.Rarity;
            }

            string opts = parts.Length > 2 ? parts[2] : "weapons";
            foreach (var tok in opts.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string t = tok.ToLowerInvariant();
                if (t == "weapons") p.OnWeapons = true;
                else if (t == "armor") p.OnArmor = true;
                else if (t == "player") p.Player = true;
                else if (t == "held") p.Held = true;
                else if (t.StartsWith("weight=")) { if (!TryFloat(t.Substring(7), out p.Weight)) errors.Add($"Line {n}: bad weight."); }
                else if (t.StartsWith("types=")) { if (!Fill(t.Substring(6), types, p.Types, "type", n, errors)) return null; }
                else if (t.StartsWith("boosts=")) { if (!Fill(t.Substring(7), types, p.Boosts, "type", n, errors)) return null; }
                else if (t.StartsWith("archetypes=")) { if (!Fill(t.Substring(11), archetypes, p.Archetypes, "archetype", n, errors)) return null; }
                else if (t.StartsWith("slots=")) { if (!Fill(t.Substring(6), slots, p.Slots, "slot", n, errors)) return null; }
                else { errors.Add($"Line {n}: unknown option '{tok}'."); return null; }
            }
            if (!p.OnWeapons && !p.OnArmor) { errors.Add($"Line {n}: say where it rolls (weapons, armor)."); return null; }
            return p;
        }

        private static bool Fill<T>(string csv, Dictionary<string, T> map, List<T> into, string what, int n, List<string> errors)
        {
            foreach (var k in csv.Split(','))
            {
                if (k.Length == 0) continue;
                if (!map.TryGetValue(k, out var v)) { errors.Add($"Line {n}: unknown {what} '{k}'."); return false; }
                if (!into.Contains(v)) into.Add(v);
            }
            return true;
        }

        private static bool TryFloat(string s, out float v) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        // Creates or updates each affix asset by name and adds it to the library.
        public static List<AffixSO> Apply(List<Parsed> parsed, AffixLibrarySO library, List<string> log)
        {
            Combat.Stats.EditorTools.BootstrapScenes.EnsureFolder(Folder);
            var made = new List<AffixSO>();
            foreach (var p in parsed)
            {
                string path = Folder + "/" + p.Name + ".asset";
                var a = AssetDatabase.LoadAssetAtPath<AffixSO>(path);
                bool created = a == null;
                if (created) a = ScriptableObject.CreateInstance<AffixSO>();
                else Undo.RecordObject(a, "Update affix");

                if (created || string.IsNullOrEmpty(a.id)) a.id = Id(p.Name);
                a.displayName = p.Name;
                a.stat = p.Stat;
                a.bucket = p.Bucket;
                a.target = p.Player ? ModifierTarget.Player : ModifierTarget.Weapons;
                a.scope = p.Held ? ActivityScope.InHands : ActivityScope.InLoadout;
                a.targetWeaponTypes = new List<WeaponTypeSO>(p.Boosts);
                a.valueSource = p.Source;
                a.rarityValues = (float[])p.Values.Clone();
                a.baseValue = p.Base;
                a.onWeapons = p.OnWeapons;
                a.weaponTypeFilter = new List<WeaponTypeSO>(p.Types);
                a.archetypeFilter = new List<WeaponArchetypeSO>(p.Archetypes);
                a.onArmor = p.OnArmor;
                a.slotFilter = new List<ArmorSlotSO>(p.Slots);
                a.weight = p.Weight;

                if (created) AssetDatabase.CreateAsset(a, path);
                else EditorUtility.SetDirty(a);
                log.Add((created ? "Created " : "Updated ") + p.Name + ".");
                made.Add(a);

                if (library != null && !library.affixes.Contains(a))
                {
                    Undo.RecordObject(library, "Add affix");
                    library.affixes.Add(a);
                    EditorUtility.SetDirty(library);
                }
            }
            AssetDatabase.SaveAssets();
            return made;
        }

        // The library as import text.
        public static string Write(AffixLibrarySO library)
        {
            var lines = new List<string>();
            if (library == null) return "";
            foreach (var a in library.affixes)
            {
                if (a == null || a.stat == null) continue;
                string kind = a.bucket == ModifierBucket.Flat ? "flat" : a.bucket == ModifierBucket.Additive ? "pct" : "mult";
                string vals = a.valueSource == AffixValueSource.ItemLevel ? "lvl " + F(a.baseValue)
                    : string.Join(",", (a.rarityValues ?? new float[0]).Select(F));
                var opts = new List<string>();
                if (a.onWeapons) opts.Add("weapons");
                if (a.onArmor) opts.Add("armor");
                if (a.weaponTypeFilter.Count > 0) opts.Add("types=" + Ids(a.weaponTypeFilter.Select(t => t != null ? t.id : null)));
                if (a.archetypeFilter.Count > 0) opts.Add("archetypes=" + Ids(a.archetypeFilter.Select(t => t != null ? t.id : null)));
                if (a.slotFilter.Count > 0) opts.Add("slots=" + Ids(a.slotFilter.Select(t => t != null ? t.id : null)));
                if (a.target == ModifierTarget.Player) opts.Add("player");
                if (a.scope == ActivityScope.InHands) opts.Add("held");
                if (a.targetWeaponTypes.Count > 0) opts.Add("boosts=" + Ids(a.targetWeaponTypes.Select(t => t != null ? t.id : null)));
                if (!Mathf.Approximately(a.weight, 1f)) opts.Add("weight=" + F(a.weight));
                lines.Add($"{a.DisplayName} | {a.stat.id} {kind} {vals} | {string.Join(" ", opts)}");
            }
            return string.Join("\n", lines);
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Ids(IEnumerable<string> ids) => string.Join(",", ids.Where(i => !string.IsNullOrEmpty(i)));
        private static string Id(string name) => name.Trim().ToLowerInvariant().Replace(' ', '_').Replace("(", "").Replace(")", "");

        private static void LoadLookups()
        {
            stats = new Dictionary<string, StatDefinitionSO>();
            foreach (var s in All<StatDefinitionSO>()) if (!string.IsNullOrEmpty(s.id)) stats[s.id.ToLowerInvariant()] = s;
            types = new Dictionary<string, WeaponTypeSO>();
            foreach (var t in All<WeaponTypeSO>()) Key(types, t, t.id, t.displayName);
            archetypes = new Dictionary<string, WeaponArchetypeSO>();
            foreach (var a in All<WeaponArchetypeSO>()) Key(archetypes, a, a.id, a.displayName);
            slots = new Dictionary<string, ArmorSlotSO>();
            foreach (var s in All<ArmorSlotSO>()) Key(slots, s, s.id, s.displayName);
        }

        private static void Key<T>(Dictionary<string, T> map, T v, string id, string display) where T : Object
        {
            foreach (var k in new[] { id, display, v.name })
                if (!string.IsNullOrEmpty(k)) map[k.ToLowerInvariant().Replace(' ', '_')] = v;
        }

        public static List<T> All<T>() where T : Object
        {
            var list = new List<T>();
            foreach (var g in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null) list.Add(a);
            }
            return list;
        }
    }
}
