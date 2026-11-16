using UnityEngine;

namespace Combat.Info
{
    // A rules word shown as a link in descriptions (Burn, Slow, Shield). Hover or click it for its popup.
    [CreateAssetMenu(fileName = "Keyword", menuName = "Combat/Info/Keyword")]
    public class KeywordSO : ScriptableObject
    {
        [Tooltip("Stable id. Generators look up built-in keywords by it (slow, pool, explode, stack, precision, shield).")]
        public string id = "";
        public string displayName = "";
        public Color color = new Color(0.55f, 0.8f, 1f, 1f);
        public Sprite icon;
        [Tooltip("General rule, shown at the top of the popup. Numbers for the specific source are added under it.")]
        [TextArea(2, 6)] public string description = "";

        public string Name => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }
}
