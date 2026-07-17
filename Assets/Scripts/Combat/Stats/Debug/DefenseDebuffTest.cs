using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Stats;

// Quick defense test: applies resistance-shatter and vulnerability modifiers to an
// enemy's container on keypress, so you can watch damage change and (for
// vulnerability) the debuff damage-number styling light up. Delete after verifying.
//
// Keys (new Input System):
//   1 - toggle -0.5 on a resistance stat (shatter: if it had 0.5 resist -> 0)
//   2 - toggle +0.5 damage_taken (vulnerability: +50% all damage, WasDebuffed on)
//   3 - toggle +0.5 on the resistance stat (harden: +50% resist, takes less)
public class DefenseDebuffTest : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private CombatantStats enemyStats;

    [Header("Stats")]
    [SerializeField] private StatDefinitionSO resistanceStat;  // e.g. fire_resistance or physical_resistance
    [SerializeField] private StatDefinitionSO damageTakenStat; // damage_taken

    [Header("Amounts")]
    [SerializeField] private float shatterAmount = 0.5f;
    [SerializeField] private float vulnerabilityAmount = 0.5f;
    [SerializeField] private float hardenAmount = 0.5f;

    private ModifierHandle shatterHandle;
    private ModifierHandle vulnHandle;
    private ModifierHandle hardenHandle;
    private bool shatterOn, vulnOn, hardenOn;

    private void Update()
    {
        if (enemyStats == null) return;
        var c = enemyStats.Container;
        if (c == null) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame)
            Toggle(c, resistanceStat, -shatterAmount, ref shatterOn, ref shatterHandle, "SHATTER resist");

        if (kb.digit2Key.wasPressedThisFrame)
            Toggle(c, damageTakenStat, vulnerabilityAmount, ref vulnOn, ref vulnHandle, "VULNERABILITY");

        if (kb.digit3Key.wasPressedThisFrame)
            Toggle(c, resistanceStat, hardenAmount, ref hardenOn, ref hardenHandle, "HARDEN resist");
    }

    private void Toggle(StatContainer c, StatDefinitionSO stat, float amount,
                        ref bool on, ref ModifierHandle handle, string label)
    {
        if (stat == null) { Debug.LogError($"[DefenseTest] {label}: stat not assigned."); return; }

        if (!on)
        {
            handle = c.AddModifier(new StatModifier(stat, StatResolver.ADDITIVE, amount), owner: this);
            on = true;
            Debug.Log($"[DefenseTest] {label} ON ({(amount >= 0 ? "+" : "")}{amount}) | {stat.id} now {c.Resolve(stat):F2}");
        }
        else
        {
            c.RemoveModifier(handle);
            on = false;
            Debug.Log($"[DefenseTest] {label} OFF | {stat.id} now {c.Resolve(stat):F2}");
        }
    }
}