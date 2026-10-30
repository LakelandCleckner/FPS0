using System.Collections.Generic;
using Combat.Core;
using Combat.Sources;

namespace Combat.Events
{
    // Kill events for weapons that shared a status kill but didn't land the killing tick.
    public static class KillCredit
    {
        public static void Publish(List<IDamageSource> sources, HitContext ctx)
        {
            int n = sources.Count;
            for (int i = 0; i < n && i < sources.Count; i++)
            {
                var weapon = sources[i] as WeaponDamageSource;
                if (weapon == null) continue;

                // Include inactive: the weapon may have left the loadout since it applied the status.
                var bus = weapon.GetComponentInParent<WeaponEventBus>(true);
                if (bus == null) continue;

                bus.Publish(WeaponEvent.ForHit(WeaponEventType.Kill, weapon, ctx));
            }
        }
    }
}
