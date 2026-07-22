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

        public bool IsFinalInBurst => BurstIndex >= BurstCount - 1;
        public bool IsFirstInBurst => BurstIndex == 0;

        public ShotInfo(int shotId, int burstIndex = 0, int burstCount = 1, float chargeLevel = 1f)
        {
            ShotId = shotId;
            BurstIndex = burstIndex;
            BurstCount = burstCount;
            ChargeLevel = chargeLevel;
        }
    }
}
