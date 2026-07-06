using Combat.Core;

namespace Combat.Weapons
{
    // The FIRING BEHAVIOR axis: decides WHEN shots happen (semi / auto / burst /
    // charge). Ticked by the weapon runtime each frame with the trigger state and
    // the resolved stats (for RPM etc.). Emits a shot by invoking FireRequested —
    // it does NOT know what delivery does with that. Fully decoupled from delivery.
    //
    // Plain class (not MonoBehaviour): built once from a FireBehaviorSO, cached,
    // reused. No per-shot allocation; the per-frame Tick is a few comparisons.
    public interface IFireBehavior
    {
        // The runtime assigns this; the behavior calls it when a shot should fire.
        // Direct callback (not an event bus) — one call per shot, gameplay cadence.
        System.Action FireRequested { get; set; }

        // Called every frame by the runtime. deltaTime is scaled game time.
        // stats supplies RPM (and future rate-related stats). trigger is the
        // input-agnostic trigger snapshot.
        void Tick(float deltaTime, in TriggerState trigger, in StatBlock stats);

        // Called when the weapon is stowed/disabled so the behavior can reset
        // transient state (charge, burst-in-progress).
        void Reset();
    }
}
