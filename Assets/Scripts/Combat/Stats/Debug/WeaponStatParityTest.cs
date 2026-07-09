using UnityEngine;
using Combat.Core;
using Combat.Stats;

namespace Combat.Weapons
{
    // Phase 2e parallel-equivalence proof. For a given WeaponSO, resolves each stat
    // BOTH ways — the legacy StatBlock (ResolveStats) and the new StatContainer
    // (populated from the same archetype+deltas) — and logs MATCH / MISMATCH per
    // stat. Does NOT touch the damage path; the game still runs on the StatBlock.
    //
    // If every line is MATCH, the container faithfully reproduces the StatBlock and
    // is safe to switch the damage path onto in Phase 2f.
    //
    // Assign the WeaponSO to test and the WeaponStatKeys. Delete after verifying.
    public class WeaponStatParityTest : MonoBehaviour
    {
        [SerializeField] private WeaponSO weapon;
        [SerializeField] private WeaponStatKeys keys;
        [Tooltip("Tolerance for float comparison.")]
        [SerializeField] private float epsilon = 0.0001f;

        private void Start()
        {
            if (weapon == null || keys == null)
            {
                Debug.LogError("[Parity] Assign weapon + keys.");
                return;
            }

            // old way
            //StatBlock sb = weapon.ResolveStats();

            // new way
            var container = new StatContainer();
            WeaponStatBuilder.PopulateBases(container, weapon, keys);
            /*
            Debug.Log($"[Parity] Comparing StatBlock vs StatContainer for '{weapon.id}':");
            Compare("weapon_damage", sb.WeaponDamage,           container.Resolve(keys.weaponDamage));
            Compare("crit_damage",   sb.CritDamage,             container.Resolve(keys.critDamage));
            Compare("crit_chance",   sb.CritChance,             container.Resolve(keys.critChance));
            Compare("global_damage", sb.GlobalDamageMultiplier, container.Resolve(keys.globalDamage));
            Compare("rpm",           sb.RoundsPerMinute,        container.Resolve(keys.rpm));
            Compare("magazine_size", sb.MagazineSize,           container.Resolve(keys.magazineSize));
            Compare("reload_time",   sb.ReloadTime,             container.Resolve(keys.reloadTime));
            */
            // delta proof: temporarily verify that a delta moves both identically is
            // implicit here — the weapon's authored deltas are already baked into
            // BOTH sb (via ResolveStats) and the container (via PopulateBases), so a
            // MATCH already proves deltas compose the same way. To spot-check, set a
            // delta on the WeaponSO in the inspector and re-run: both sides move.
        }

        private void Compare(string label, float statBlockValue, float containerValue)
        {
            bool match = Mathf.Abs(statBlockValue - containerValue) <= epsilon;
            Debug.Log($"[Parity] {label,-16} StatBlock={statBlockValue,-8:0.####} " +
                      $"Container={containerValue,-8:0.####} {(match ? "MATCH" : "*** MISMATCH ***")}");
        }
    }
}
