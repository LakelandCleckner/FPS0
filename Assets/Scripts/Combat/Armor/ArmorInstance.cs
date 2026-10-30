using System;
using UnityEngine;

namespace Combat.Armor
{
    // One owned armour piece. Also the owner its modifiers are registered under.
    [Serializable]
    public class ArmorInstance
    {
        [SerializeField] private string uid;
        [SerializeField] private ArmorSO definition;

        public string Uid => uid;
        public ArmorSO Definition => definition;
        public ArmorSlotSO Slot => definition != null ? definition.slot : null;

        public ArmorInstance(ArmorSO definition)
        {
            this.definition = definition;
            uid = Guid.NewGuid().ToString("N");
        }
    }
}
