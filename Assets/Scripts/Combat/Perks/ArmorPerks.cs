using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Events;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Perks
{
    // Runs the perks of equipped armour. Sits on the player root beside PlayerLoadout.
    public class ArmorPerks : MonoBehaviour
    {
        [Tooltip("Found on this object or its parents if empty.")]
        [SerializeField] private PlayerLoadout model;
        [SerializeField] private bool debugLog = false;

        private readonly Dictionary<ArmorInstance, List<PerkRuntime>> attached = new Dictionary<ArmorInstance, List<PerkRuntime>>();
        private bool started;

        private void Awake()
        {
            if (model == null) model = GetComponentInParent<PlayerLoadout>();
        }

        private void OnEnable()
        {
            if (model != null) model.ArmorSlotChanged += HandleArmorChanged;
            if (started) AttachAll();
        }

        private void OnDisable()
        {
            if (model != null) model.ArmorSlotChanged -= HandleArmorChanged;
            DetachAll();
        }

        // Start, so the player's containers and the modifier applier are ready.
        private void Start()
        {
            started = true;
            AttachAll();
        }

        // Rebuilds one piece's perks, e.g. after a reroll.
        public void Reattach(ArmorInstance armor)
        {
            Detach(armor);
            if (started && isActiveAndEnabled && armor != null && model.GetArmor(armor.Slot) == armor) Attach(armor);
        }

        private void AttachAll()
        {
            if (model == null) return;
            model.EnsureInitialized();
            for (int i = 0; i < model.ArmorSlotCount; i++) Attach(model.GetArmor(i));
        }

        private void DetachAll()
        {
            var keys = new List<ArmorInstance>(attached.Keys);
            foreach (var k in keys) Detach(k);
        }

        private void HandleArmorChanged(ArmorSlotSO slot, ArmorInstance oldArmor, ArmorInstance newArmor)
        {
            if (!started) return;
            Detach(oldArmor);
            Attach(newArmor);
        }

        private void Attach(ArmorInstance armor)
        {
            if (armor == null || armor.Definition == null || attached.ContainsKey(armor)) return;

            var player = GetComponentInParent<CombatantStats>();
            var ctx = new PerkContext
            {
                Item = armor,
                Bus = WeaponEventBus.FindFor(this),
                Applier = GetComponentInParent<LoadoutModifierApplier>(),
                Clock = PerkClock.For(this),
                Loadout = GetComponentInChildren<WeaponLoadout>(),
                Player = player,
                Health = player != null ? player.GetComponent<CombatantHealth>() : null
            };

            var list = new List<PerkRuntime>();
            foreach (var perk in armor.AllPerks())
            {
                if (perk == null) continue;
                var rt = perk.CreateRuntime();
                if (rt == null) continue;

                try { rt.Attach(perk, ctx); list.Add(rt); }
                catch (System.Exception e) { Debug.LogError($"[ArmorPerks] {perk.name} failed to attach: {e}"); }

                if (debugLog) Debug.Log($"[ArmorPerks] {armor.Definition.name} + {perk.name}");
            }
            attached[armor] = list;
        }

        private void Detach(ArmorInstance armor)
        {
            if (armor == null || !attached.TryGetValue(armor, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--) list[i].Detach();
            attached.Remove(armor);
        }
    }
}
