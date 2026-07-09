namespace Combat.Weapons
{
    // The FIRING BEHAVIOR axis: decides WHEN shots happen (semi / auto / burst /
    // charge). Ticked by the weapon runtime each frame with the trigger state and
    // the resolved RPM. Emits a shot by invoking FireRequested — it does NOT know
    // what delivery does with that. Fully decoupled from delivery.
    //
    // Phase 2f: Tick takes a resolved `rpm` float (from the weapon's StatContainer)
    // instead of the retired StatBlock. RPM was the only thing it read.
    public interface IFireBehavior
    {
        System.Action FireRequested { get; set; }

        // deltaTime scaled game time; rpm the resolved fire rate; trigger the
        // input-agnostic trigger snapshot.
        void Tick(float deltaTime, in TriggerState trigger, float rpm);

        void Reset();
    }
}