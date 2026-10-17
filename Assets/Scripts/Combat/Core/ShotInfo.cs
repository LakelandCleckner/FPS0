using UnityEngine;

namespace Combat.Core
{
    // What the fire behaviour knows about a shot at the moment it authorises it.
    //
    // Exists because FireRequested used to be parameterless, which was fine while
    // auto/semi were the only behaviours — neither has anything to say about an
    // individual shot. Burst and charge do: a burst shot knows its index, a charged
    // shot knows its charge level, and neither can reach delivery without a payload.
    //
    // Carried from the behaviour, through delivery, onto HitContext, so effects and
    // perks can read it.
    public readonly struct ShotInfo
    {
        // Identity for this shot. Monotonic per weapon (see WeaponFireController).
        //
        // THE POINT OF THIS FIELD: a perk that counts HIT events over-counts whenever
        // one shot produces several — a piercing round through three enemies fires
        // three Hits, a fusion burst fires one per bolt. A perk wanting "per shot"
        // semantics counts DISTINCT ShotIds instead of events.
        //
        // Deliberately not enforced anywhere. A perk that genuinely wants to stack off
        // every bolt simply doesn't dedup. The system supplies identity; the perk
        // decides whether identity means anything to it.
        public readonly int ShotId;

        // Position within a burst, 0-based. 0 for non-burst fire.
        public readonly int BurstIndex;

        // How many shots this burst contains. 1 for non-burst fire.
        public readonly int BurstCount;

        // 0..1. Always 1 for behaviours that don't charge, so a damage scalar reading
        // it needs no special case.
        public readonly float ChargeLevel;

        // Pellet index within the shot, 0 for single-projectile fire
        public readonly int PelletIndex;

        // Camera ray origin at fire time, for range falloff
        public readonly Vector3 Origin;

        // ADS blend at fire time (0 hip, 1 aimed)
        public readonly float AimBlend;

        // False on default ShotInfo (ticks, probes): skips falloff
        public readonly bool HasOrigin;

        // Zero on default ShotInfo, so read through the accessors
        private readonly int pelletCount;
        private readonly float damageScale;

        public int PelletCount => pelletCount > 0 ? pelletCount : 1;

        // Base damage share per pellet
        public float DamageScale => damageScale > 0f ? damageScale : 1f;

        public bool IsPellet => PelletCount > 1;
        public bool IsFinalInBurst => BurstIndex >= BurstCount - 1;
        public bool IsFirstInBurst => BurstIndex == 0;

        public ShotInfo(int shotId, int burstIndex = 0, int burstCount = 1, float chargeLevel = 1f)
            : this(shotId, burstIndex, burstCount, chargeLevel, 0, 1, 1f, Vector3.zero, 0f, false) { }

        private ShotInfo(int shotId, int burstIndex, int burstCount, float chargeLevel,
                         int pelletIndex, int pelletCount, float damageScale,
                         Vector3 origin, float aimBlend, bool hasOrigin)
        {
            ShotId = shotId;
            BurstIndex = burstIndex;
            BurstCount = burstCount;
            ChargeLevel = chargeLevel;
            PelletIndex = pelletIndex;
            this.pelletCount = pelletCount;
            this.damageScale = damageScale;
            Origin = origin;
            AimBlend = aimBlend;
            HasOrigin = hasOrigin;
        }

        // Pellet `index` of `count`, each with an even base damage share
        public ShotInfo ForPellet(int index, int count)
        {
            int n = count > 0 ? count : 1;
            return new ShotInfo(ShotId, BurstIndex, BurstCount, ChargeLevel,
                                index, n, 1f / n, Origin, AimBlend, HasOrigin);
        }

        // Stamps the fire-time origin and aim blend
        public ShotInfo WithOrigin(Vector3 origin, float aimBlend)
            => new ShotInfo(ShotId, BurstIndex, BurstCount, ChargeLevel,
                            PelletIndex, pelletCount, damageScale,
                            origin, Mathf.Clamp01(aimBlend), true);
    }
}