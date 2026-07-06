namespace Combat.Weapons
{
    // Input-agnostic trigger snapshot the runtime passes to a fire behavior each
    // tick. The behavior never reads Mouse/Input directly — it only knows the
    // trigger's logical state, so behaviors stay decoupled from the input system
    // and testable.
    public readonly struct TriggerState
    {
        public readonly bool Held;      // trigger currently down
        public readonly bool Pressed;   // went down this tick (edge)
        public readonly bool Released;  // went up this tick (edge)

        public TriggerState(bool held, bool pressed, bool released)
        {
            Held = held;
            Pressed = pressed;
            Released = released;
        }
    }
}
