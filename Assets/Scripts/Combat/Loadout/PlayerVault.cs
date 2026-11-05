using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Weapons;

namespace Combat.Loadout
{
    // Unlimited storage for items the player isn't carrying. Data only.
    public class PlayerVault : MonoBehaviour
    {
        private readonly List<WeaponInstance> weapons = new List<WeaponInstance>();
        private readonly List<ArmorInstance> armor = new List<ArmorInstance>();

        public event Action Changed;

        public IReadOnlyList<WeaponInstance> Weapons => weapons;
        public IReadOnlyList<ArmorInstance> Armor => armor;

        public bool Contains(WeaponInstance weapon) => weapon != null && weapons.Contains(weapon);
        public bool Contains(ArmorInstance piece) => piece != null && armor.Contains(piece);

        public bool TryAddWeapon(WeaponInstance weapon)
        {
            if (weapon == null || weapon.Definition == null || weapons.Contains(weapon)) return false;
            weapons.Add(weapon);
            Changed?.Invoke();
            return true;
        }

        public bool RemoveWeapon(WeaponInstance weapon)
        {
            if (weapon == null || !weapons.Remove(weapon)) return false;
            Changed?.Invoke();
            return true;
        }

        public bool TryAddArmor(ArmorInstance piece)
        {
            if (piece == null || piece.Definition == null || armor.Contains(piece)) return false;
            armor.Add(piece);
            Changed?.Invoke();
            return true;
        }

        public bool RemoveArmor(ArmorInstance piece)
        {
            if (piece == null || !armor.Remove(piece)) return false;
            Changed?.Invoke();
            return true;
        }
    }
}
