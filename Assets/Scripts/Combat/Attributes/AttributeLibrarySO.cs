using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Weapons;

namespace Combat.Attributes
{
    // Every attribute in the game, in display order.
    [CreateAssetMenu(fileName = "Attributes", menuName = "Combat/Attributes/Attribute Library")]
    public class AttributeLibrarySO : ScriptableObject
    {
        public List<AttributeSO> attributes = new List<AttributeSO>();

        // The attribute tied to this element, or null.
        public AttributeSO For(DamageTypeSO element)
        {
            if (element == null) return null;
            foreach (var a in attributes)
                if (a != null && a.element == element) return a;
            return null;
        }

        // The attribute tied to this archetype, or null.
        public AttributeSO For(WeaponArchetypeSO archetype)
        {
            if (archetype == null) return null;
            foreach (var a in attributes)
                if (a != null && a.archetypes.Contains(archetype)) return a;
            return null;
        }
    }
}
