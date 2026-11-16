using UnityEngine;
using Combat.Stats;

namespace Combat.Info
{
    // Anything that can explain itself in player-facing text: perks, statuses, effects, enemy layers.
    public interface IDescribable
    {
        void Describe(Describer d);
    }

    // What a description knows about where it's shown.
    public sealed class DescribeContext
    {
        public KeywordLibrarySO Keywords;
        [Tooltip("Stat whose live value is known, so percents of it can also show the real number.")]
        public StatDefinitionSO DamageStat;
        public float DamageValue = -1f;
        public bool Rich = true;
        public Color Number = new Color(0.94f, 0.92f, 0.88f, 1f);
        public Color Good = new Color(0.5f, 0.86f, 0.58f, 1f);
        public Color Bad = new Color(0.96f, 0.44f, 0.4f, 1f);

        public bool KnowsValue(StatDefinitionSO stat) => stat != null && stat == DamageStat && DamageValue >= 0f;
    }

    // A keyword used in a description, plus the source whose numbers the popup shows.
    public struct KeywordLink
    {
        public KeywordSO Keyword;
        public IDescribable Detail;
    }
}
