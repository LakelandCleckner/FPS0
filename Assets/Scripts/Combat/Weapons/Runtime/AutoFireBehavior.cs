using Combat.Core;

namespace Combat.Weapons
{
    // Runtime auto/semi behavior. RPM (from resolved stats) is the shared fire-rate
    // ceiling for both modes. Both fill right up to it whether you hold or click.
    //
    // Model: a short-lived "fire buffer" holds the intent to shoot. It's set by
    // input and consumed when cooldown clears, so a click whose frame doesn't line
    // up with the cooldown-clear moment isn't lost. The buffer expires after a
    // brief window so a stale press doesn't fire much later.
    //   - full-auto: holding continuously refreshes the buffer -> fires every cycle.
    //     spam-clicking refreshes it on each press -> also fills to the cap.
    //   - semi-auto: each press sets the buffer once; one shot per press.
    public class AutoFireBehavior : IFireBehavior
    {
        public System.Action FireRequested { get; set; }

        private readonly bool fireWhileHeld;

        private float cooldown;       // time until next allowed shot
        private float fireBuffer;     // >0 means "want to fire", counts down

        // How long a press stays queued waiting for cooldown. Long enough to bridge
        // one fire-interval so no click is dropped; short enough that stale clicks
        // don't fire. Tunable if needed.
        private const float BufferWindow = 0.1f;

        public AutoFireBehavior(bool fireWhileHeld)
        {
            this.fireWhileHeld = fireWhileHeld;
            cooldown = 0f;
            fireBuffer = 0f;
        }

        public void Tick(float deltaTime, in TriggerState trigger, in StatBlock stats)
        {
            if (cooldown > 0f) cooldown -= deltaTime;
            if (fireBuffer > 0f) fireBuffer -= deltaTime;

            float rpm = stats.RoundsPerMinute > 0f ? stats.RoundsPerMinute : 1f;
            float fireDelay = 60f / rpm;

            // Set/refresh the fire buffer from input.
            if (fireWhileHeld)
            {
                // holding keeps intent alive continuously; a press also sets it
                if (trigger.Held) fireBuffer = BufferWindow;
                else if (trigger.Pressed) fireBuffer = BufferWindow;
            }
            else
            {
                // semi-auto: only a fresh press sets intent (one per press)
                if (trigger.Pressed) fireBuffer = BufferWindow;
            }

            // Fire if we have buffered intent and the rate allows.
            if (fireBuffer > 0f && cooldown <= 0f)
            {
                FireRequested?.Invoke();
                cooldown = fireDelay;
                fireBuffer = 0f; // consume the intent (held will re-set it next tick)
            }
        }

        public void Reset()
        {
            cooldown = 0f;
            fireBuffer = 0f;
        }
    }
}