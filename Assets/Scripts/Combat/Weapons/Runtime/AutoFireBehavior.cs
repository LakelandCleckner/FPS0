using Combat.Core;

namespace Combat.Weapons
{
    // Runtime auto/semi behavior. Gates fire by RPM (resolved from the weapon's stat
    // container, passed into Tick). Both modes share the same fire-rate ceiling and
    // fill up to it whether held or spam-clicked.
    //
    // TWO KINDS OF INTENT, deliberately handled differently:
    //
    //   PRESSED is a momentary event, so it BUFFERS. Clicking slightly before the
    //   weapon is ready still fires when it becomes ready, which is what makes
    //   semi-auto feel responsive instead of eating inputs.
    //
    //   HELD is a continuous condition, so it is READ AT FIRE TIME and never
    //   buffered. Buffering it meant the buffer was refreshed every frame the trigger
    //   was down, so releasing left up to a full window of intent still queued and one
    //   more shot went out after the player had stopped asking for it.
    //
    // A quick tap still fires either way: Pressed buffers it, and releasing does not
    // clear the buffer, so the shot lands when the cooldown clears.
    public class AutoFireBehavior : IFireBehavior
    {
        public System.Action<ShotInfo> FireRequested { get; set; }

        private readonly bool fireWhileHeld;
        private float cooldown;
        private float fireBuffer;

        private const float BufferWindow = 0.15f;

        public AutoFireBehavior(bool fireWhileHeld)
        {
            this.fireWhileHeld = fireWhileHeld;
            cooldown = 0f;
            fireBuffer = 0f;
        }

        public void Tick(float deltaTime, in TriggerState trigger, float rpm)
        {
            if (cooldown > 0f) cooldown -= deltaTime;
            if (fireBuffer > 0f) fireBuffer -= deltaTime;

            float safeRpm = rpm > 0f ? rpm : 1f;
            float fireDelay = 60f / safeRpm;

            // buffered: a press, which survives briefly so an early click isn't eaten
            if (trigger.Pressed) fireBuffer = BufferWindow;

            // live: the trigger being down right now, which stops the frame it's released
            bool wantsToFire = fireBuffer > 0f || (fireWhileHeld && trigger.Held);

            if (wantsToFire && cooldown <= 0f)
            {
                // shotId 0 — the controller stamps the real id (it owns the counter).
                FireRequested?.Invoke(new ShotInfo(0));
                cooldown = fireDelay;
                fireBuffer = 0f;
            }
        }

        public void Reset()
        {
            cooldown = 0f;
            fireBuffer = 0f;
        }
    }
}