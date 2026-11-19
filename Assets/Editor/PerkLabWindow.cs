using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Info;
using Combat.Loadout;
using Combat.Perks;
using Combat.Weapons;
using Object = UnityEngine.Object;

// Play-mode perk testing: Forge builds an exact weapon or armor piece and gives it to the player.
public partial class PerkLabWindow : EditorWindow
{
    private enum Tab { Forge, Bench, Sweep }
    private enum ForgeKind { Weapon, Armor }

    [SerializeField] private Tab tab = Tab.Forge;
    [SerializeField] private ForgeKind kind = ForgeKind.Weapon;
    [SerializeField] private string itemSearch = "";
    [SerializeField] private Rarity rarity = Rarity.Legendary;
    [SerializeField] private bool anyPerk = false;

    private WeaponSO weapon;
    private ArmorSO armor;
    private readonly List<PerkSO> picks = new List<PerkSO>();
    private Vector2 scroll;
    private string message = "";

    private List<WeaponSO> weapons;
    private List<ArmorSO> armors;
    private List<PerkSO> allWeaponPerks, allArmorPerks;

    [MenuItem("FPS0/Items & Perks/Perk Lab", priority = 531)]
    private static void Open() => GetWindow<PerkLabWindow>("Perk Lab");

    private void OnEnable() => LoadAssets();

    private void LoadAssets()
    {
        weapons = Load<WeaponSO>().OrderBy(w => TypeName(w)).ThenBy(w => w.name).ToList();
        armors = Load<ArmorSO>().OrderBy(a => a.slot != null ? a.slot.name : "").ThenBy(a => a.name).ToList();
        var perks = Load<PerkSO>();
        allWeaponPerks = perks.Where(p => AssetDatabase.GetAssetPath(p).Contains("/Perks/Weapon/")).OrderBy(p => p.name).ToList();
        allArmorPerks = perks.Where(p => AssetDatabase.GetAssetPath(p).Contains("/Perks/Armor/")).OrderBy(p => p.name).ToList();
    }

    private static List<T> Load<T>() where T : Object
    {
        var list = new List<T>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && !list.Contains(a)) list.Add(a);
        }
        return list;
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            tab = (Tab)GUILayout.Toolbar((int)tab, new[] { "Forge", "Bench", "Sweep" }, EditorStyles.toolbarButton, GUILayout.Width(230));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload Assets", EditorStyles.toolbarButton, GUILayout.Width(95))) LoadAssets();
        }

        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode to give items and use the bench. You can set up a build now.", MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (tab == Tab.Forge) DrawForge();
        else if (tab == Tab.Bench) DrawBench();
        else DrawSweep();
        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ forge

    private void DrawForge()
    {
        var newKind = (ForgeKind)GUILayout.Toolbar((int)kind, new[] { "Weapon", "Armor" });
        if (newKind != kind) { kind = newKind; picks.Clear(); message = ""; }

        itemSearch = EditorGUILayout.TextField("Search", itemSearch);
        if (kind == ForgeKind.Weapon) PickItem(weapons, ref weapon, w => $"{TypeName(w)} / {w.name}  [{(w.baseDamageType != null ? w.baseDamageType.name : "none")}]");
        else PickItem(armors, ref armor, a => $"{(a.slot != null ? a.slot.name : "?")} / {a.name}");

        var def = kind == ForgeKind.Weapon ? (Object)weapon : armor;
        if (def == null) return;

        bool mythic = kind == ForgeKind.Weapon ? weapon.mythic : armor.mythic;
        using (new EditorGUI.DisabledScope(mythic))
            rarity = (Rarity)EditorGUILayout.EnumPopup("Rarity", mythic ? Rarity.Mythic : rarity);
        anyPerk = EditorGUILayout.ToggleLeft("Any perk in any column (ignores pools and element)", anyPerk);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Intrinsic", EditorStyles.boldLabel);
        var intrinsics = kind == ForgeKind.Weapon ? weapon.ResolvedIntrinsics : armor.intrinsicPerks.Where(p => p != null).ToList();
        if (intrinsics.Count == 0) EditorGUILayout.LabelField("  none", EditorStyles.miniLabel);
        foreach (var p in intrinsics) PerkLine(p);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Columns", EditorStyles.boldLabel);
        var cols = kind == ForgeKind.Weapon ? weapon.ResolvedPerkColumns : armor.ResolvedPerkColumns;
        if (cols == null || cols.Count == 0) { EditorGUILayout.HelpBox("This item has no perk columns.", MessageType.None); }
        else
        {
            while (picks.Count < cols.Count) picks.Add(null);
            if (picks.Count > cols.Count) picks.RemoveRange(cols.Count, picks.Count - cols.Count);
            var element = kind == ForgeKind.Weapon ? weapon.baseDamageType : null;
            var gathered = new List<PerkSO>();
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                string label = $"{i + 1}. {(c != null && !string.IsNullOrWhiteSpace(c.label) ? c.label : "Column")}";
                bool locked = c != null && c.minRarity > (mythic ? Rarity.Mythic : rarity);
                if (locked)
                {
                    EditorGUILayout.LabelField(label, $"locked below {c.minRarity}", EditorStyles.miniLabel);
                    picks[i] = null;
                    continue;
                }

                List<PerkSO> options;
                if (anyPerk) options = kind == ForgeKind.Weapon ? allWeaponPerks : allArmorPerks;
                else
                {
                    gathered.Clear();
                    if (c != null) c.Gather(gathered, element);
                    options = gathered.Where(p => p != null && PerkRoll.FitsElement(p, element)).OrderBy(p => p.name).ToList();
                }

                var names = new List<string> { "(empty)" };
                names.AddRange(options.Select(p => p.name));
                int cur = picks[i] != null ? options.IndexOf(picks[i]) + 1 : 0;
                if (cur < 0) { names.Add(picks[i].name + " (not in this column)"); cur = names.Count - 1; }
                int next = EditorGUILayout.Popup(label, cur, names.ToArray());
                if (next != cur) picks[i] = next == 0 ? null : next <= options.Count ? options[next - 1] : picks[i];
                if (picks[i] != null) PerkLine(picks[i], true);
            }
        }

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Randomize")) Randomize();
            if (GUILayout.Button("Clear")) for (int i = 0; i < picks.Count; i++) picks[i] = null;
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("Give", GUILayout.Height(24))) Give();
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
    }

    private void PickItem<T>(List<T> items, ref T current, Func<T, string> label) where T : Object
    {
        string s = (itemSearch ?? "").ToLowerInvariant();
        var shown = items.Where(i => s.Length == 0 || label(i).ToLowerInvariant().Contains(s)).ToList();
        if (current != null && !shown.Contains(current)) shown.Insert(0, current);
        var names = new List<string> { "(pick one)" };
        names.AddRange(shown.Select(label));
        int cur = current != null ? shown.IndexOf(current) + 1 : 0;
        int next = EditorGUILayout.Popup(typeof(T) == typeof(WeaponSO) ? "Weapon" : "Armor", cur, names.ToArray());
        var picked = next == 0 ? null : shown[next - 1];
        if (picked != current) { current = picked; picks.Clear(); message = ""; }
    }

    private static void PerkLine(PerkSO p, bool indent = false)
    {
        string text;
        try { text = Describer.Build(p, new DescribeContext { Rich = false }); }
        catch (Exception e) { text = "(description failed: " + e.Message + ")"; }
        EditorGUILayout.LabelField((indent ? "     " : "  ") + (string.IsNullOrWhiteSpace(p.displayName) ? p.name : p.displayName) + ": " + text, EditorStyles.wordWrappedMiniLabel);
    }

    private void Randomize()
    {
        IReadOnlyList<PerkSO> rolled = kind == ForgeKind.Weapon
            ? new WeaponInstance(weapon, RollMode.Random, rarity).Perks
            : new ArmorInstance(armor, RollMode.Random, rarity).Perks;
        picks.Clear();
        picks.AddRange(rolled);
        message = "Rolled as the game would at " + rarity + ".";
    }

    private void Give()
    {
        var inventory = Object.FindFirstObjectByType<PlayerInventory>();
        var vault = inventory != null ? inventory.GetComponent<PlayerVault>() : null;
        if (vault == null) vault = Object.FindFirstObjectByType<PlayerVault>();
        if (inventory == null && vault == null) { message = "No PlayerInventory or PlayerVault in the scene."; return; }

        string where = null;
        if (kind == ForgeKind.Weapon)
        {
            var item = new WeaponInstance(weapon, RollMode.Random, rarity);
            SetPerks(item, picks);
            if (inventory != null && inventory.TryAddWeapon(item)) where = "carried";
            else if (vault != null && vault.TryAddWeapon(item)) where = "vault";
        }
        else
        {
            var item = new ArmorInstance(armor, RollMode.Random, rarity);
            SetPerks(item, picks);
            if (inventory != null && inventory.TryAddArmor(item)) where = "carried";
            else if (vault != null && vault.TryAddArmor(item)) where = "vault";
        }

        string n = kind == ForgeKind.Weapon ? weapon.name : armor.name;
        message = where != null ? $"Gave {n} ({rarity}) to {where}." : $"No room for {n} in carried or vault.";
    }

    // Overwrites the rolled perks; the instance types keep them private
    private static void SetPerks(object instance, List<PerkSO> perks)
    {
        var f = instance.GetType().GetField("perks", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null || !(f.GetValue(instance) is List<PerkSO> list)) { Debug.LogError("[PerkLab] Could not find the perks list on " + instance.GetType().Name); return; }
        list.Clear();
        list.AddRange(perks);
    }

    private static string TypeName(WeaponSO w)
        => w.archetype != null && w.archetype.weaponType != null ? w.archetype.weaponType.name : "?";

    // Bench tab lives in PerkLabWindow.Bench.cs
    partial void DrawBenchImpl(ref bool drawn);

    private void DrawBench()
    {
        bool drawn = false;
        DrawBenchImpl(ref drawn);
        if (!drawn) EditorGUILayout.HelpBox("Bench coming next.", MessageType.None);
    }
}
