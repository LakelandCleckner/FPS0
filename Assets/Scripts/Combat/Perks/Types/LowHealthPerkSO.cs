using System.Collections.Generic;
using UnityEngine;
using Combat.Loadout;

namespace Combat.Perks
{
    // Modifiers that are live while the player's health is at or below a threshold.
    [CreateAssetMenu(fileName = "LowHealthPerk", menuName = "Combat/Perks/Low Health Perk")]
    public class LowHealthPerkSO : PerkSO
    {
        [Tooltip("Fraction of max health (0.3 = 30%).")]
        [Range(0f, 1f)] public float threshold = 0.3f;
        [Tooltip("e.g. damage_taken, Flat, -0.3 for 30% damage reduction.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private readonly List<ModifierGrant> grants = new List<ModifierGrant>();

            private LowHealthPerkSO Def => (LowHealthPerkSO)Definition;

            protected override void OnAttach()
            {
                if (Ctx.Health == null) return;
                Ctx.Health.HealthChanged += HandleHealth;
                HandleHealth(Ctx.Health.CurrentHealth, Ctx.Health.MaxHealth);
            }

            protected override void OnDetach()
            {
                if (Ctx.Health != null) Ctx.Health.HealthChanged -= HandleHealth;
                grants.Clear();
            }

            private void HandleHealth(float current, float max)
            {
                bool low = max > 0f && current > 0f && current / max <= Def.threshold;

                if (low && grants.Count == 0)
                {
                    foreach (var e in Def.modifiers)
                    {
                        var g = Grant(e, e != null ? e.value : 0f);
                        if (g != null) grants.Add(g);
                    }
                }
                else if (!low && grants.Count > 0)
                {
                    for (int i = 0; i < grants.Count; i++) Remove(grants[i]);
                    grants.Clear();
                }
            }
        }
    }
}
