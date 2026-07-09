namespace Combat.Weapons
{
    // Runtime auto/semi behavior. Gates fire by RPM (resolved from the weapon's
    // stat container, passed into Tick). Both modes share the same fire-rate
    // ceiling and fill up to it whether held or spam-clicked (buffered intent).
    public class AutoFireBehavior : IFireBehavior
    {
        public System.Action FireRequested { get; set; }

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

            if (fireWhileHeld)
            {
                if (trigger.Held) fireBuffer = BufferWindow;
                else if (trigger.Pressed) fireBuffer = BufferWindow;
            }
            else
            {
                if (trigger.Pressed) fireBuffer = BufferWindow;
            }

            if (fireBuffer > 0f && cooldown <= 0f)
            {
                FireRequested?.Invoke();
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