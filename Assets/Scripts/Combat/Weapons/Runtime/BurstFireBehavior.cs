using Combat.Core;

namespace Combat.Weapons
{
    // Runtime burst behavior. One trigger pull fires N shots at an intra-burst rate,
    // then waits out a cooldown so the weapon's sustained rate matches its RPM.
    //
    // RPM IS ROUNDS PER MINUTE — the same meaning it has for auto and semi. It is NOT
    // bursts per minute. One stat with one meaning across every behaviour, so a
    // 390 pulse reads 390 whether it fires in threes or one at a time, and any
    // damage-per-second maths can use rpm without asking what the weapon is.
    //
    // The burst-to-burst cooldown is therefore DERIVED, not authored:
    //
    //     cycle    = shotsPerBurst * 60 / rpm     one full burst cycle
    //     intra    = (shotsPerBurst - 1) * delay  time spent inside the burst
    //     cooldown = cycle - intra                the remainder, waited after the
    //                                             last shot of the burst
    //
    // The useful consequence: intraBurstDelay does NOT change the sustained rate. A
    // tighter burst gives a snappier burst and a longer pause, never a faster gun. So
    // RPM is the balance number and the delay is pure feel, and they can be tuned
    // independently without one undoing the other.
    //
    // It also means burst SIZE is a real design axis at a fixed RPM: a 4-round burst
    // and a 3-round burst at the same rpm fire the same rounds per minute but differ
    // in burst length and pause length.
    //
    // Holding the trigger repeats bursts when repeatWhileHeld is set; otherwise one
    // pull is one burst. That is authored rather than assumed, because both exist:
    // most pulse rifles repeat, while a single-burst-per-pull frame is a real design.
    //
    // Once started, a burst FINISHES. Releasing the trigger mid-burst does not cut it
    // short — the shots are already committed. Partial bursts happen only when ammo
    // runs out, which the controller reports via NotifyShotFailed so the burst stops
    // rather than dry-firing its remainder.
    public class BurstFireBehavior : IFireBehavior
    {
        public System.Action<ShotInfo> FireRequested { get; set; }

        private readonly int shotsPerBurst;
        private readonly float intraBurstDelay;   // seconds between shots IN a burst
        private readonly bool repeatWhileHeld;

        private float cooldown;      // after a burst, derived from rpm
        private float shotTimer;     // next shot within the current burst
        private float fireBuffer;
        private int shotsFired;      // into the current burst; -1 = not bursting
        private bool aborted;

        private const float BufferWindow = 0.15f;

        public BurstFireBehavior(int shotsPerBurst, float intraBurstDelay, bool repeatWhileHeld)
        {
            this.shotsPerBurst = shotsPerBurst < 1 ? 1 : shotsPerBurst;
            this.intraBurstDelay = intraBurstDelay < 0f ? 0f : intraBurstDelay;
            this.repeatWhileHeld = repeatWhileHeld;
            shotsFired = -1;
        }

        public bool Bursting => shotsFired >= 0;

        // The controller calls this when a shot couldn't be paid for (empty mag), so
        // the burst ends instead of dry-firing its remaining shots.
        public void NotifyShotFailed() => aborted = true;

        public void Tick(float deltaTime, in TriggerState trigger, float rpm)
        {
            if (cooldown > 0f) cooldown -= deltaTime;
            if (fireBuffer > 0f) fireBuffer -= deltaTime;
            if (shotTimer > 0f) shotTimer -= deltaTime;

            // --- mid-burst: keep firing regardless of the trigger ---
            if (Bursting)
            {
                if (aborted) { EndBurst(rpm); return; }

                if (shotTimer <= 0f)
                {
                    FireRequested?.Invoke(new ShotInfo(0, shotsFired, shotsPerBurst));
                    shotsFired++;

                    if (aborted || shotsFired >= shotsPerBurst) EndBurst(rpm);
                    else shotTimer = intraBurstDelay;
                }
                return;
            }

            // --- idle: start a burst when off cooldown ---
            //
            // Two kinds of intent, handled differently on purpose.
            //
            // PRESSED buffers, so clicking slightly early still fires. It alone can
            // only ever produce ONE burst: it is true for a single frame and the
            // window is far shorter than a burst cycle, so it has always expired by
            // the time the next cooldown ends.
            //
            // HELD is read live and never buffered. Buffering it refreshed the window
            // every frame the trigger was down, so releasing left intent still queued
            // and a whole extra burst went out after the player let go — much more
            // noticeable than the single stray shot the same bug caused on auto.
            if (trigger.Pressed) fireBuffer = BufferWindow;

            bool wantsBurst = fireBuffer > 0f || (repeatWhileHeld && trigger.Held);

            if (wantsBurst && cooldown <= 0f)
            {
                fireBuffer = 0f;
                aborted = false;
                shotsFired = 0;
                shotTimer = 0f;   // first shot goes out on this frame
            }
        }

        private void EndBurst(float rpm)
        {
            shotsFired = -1;
            aborted = false;
            cooldown = BurstCooldown(rpm);
        }

        // Time to wait AFTER the last shot of a burst so the weapon's sustained rate
        // comes out at rpm rounds per minute. The burst has already consumed
        // (shotsPerBurst - 1) * intraBurstDelay of the cycle by the time it ends.
        private float BurstCooldown(float rpm)
        {
            float safeRpm = rpm > 0f ? rpm : 1f;

            float cycle = shotsPerBurst * 60f / safeRpm;
            float intra = (shotsPerBurst - 1) * intraBurstDelay;

            // A burst that can't physically fit inside its cycle (very high rpm, or a
            // long intra-burst delay) clamps to no pause at all. The weapon then fires
            // slower than its authored rpm — the burst itself is the limit. Clamping
            // rather than going negative keeps it merely mistuned instead of
            // accelerating with every burst.
            float cooldown = cycle - intra;
            return cooldown > 0f ? cooldown : 0f;
        }

        public void Reset()
        {
            cooldown = 0f;
            shotTimer = 0f;
            fireBuffer = 0f;
            shotsFired = -1;
            aborted = false;
        }
    }
}