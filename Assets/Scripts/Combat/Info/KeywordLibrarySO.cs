using System.Collections.Generic;
using UnityEngine;

namespace Combat.Info
{
    // Keywords for mechanics that have no asset of their own (slow, pools, explosions, stacks).
    [CreateAssetMenu(fileName = "KeywordLibrary", menuName = "Combat/Info/Keyword Library")]
    public class KeywordLibrarySO : ScriptableObject
    {
        public List<KeywordSO> keywords = new List<KeywordSO>();

        public KeywordSO Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var k in keywords)
                if (k != null && k.id == id) return k;
            return null;
        }
    }
}
