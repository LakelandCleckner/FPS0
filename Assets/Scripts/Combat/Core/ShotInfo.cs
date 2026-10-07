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
        //
        // Every pellet of a shotgun blast shares its shot's id.
        public readonly int ShotId;

        // Position within a burst, 0-based. 0 for non-burst fire.
        public readonly int BurstIndex;

        // How many shots this burst contains. 1 for non-burst fire.
        public readonly int BurstCount;

        // 0..1. Always 1 for behaviours that don't charge, so a damage scalar reading
        // it needs no special case.
        public readonly float ChargeLevel;

        // Pellet position within one shot, 0-based. 0 for single-projectile fire.
        public readonly int PelletIndex;

        // Raw storage for PelletCount / DamageScale. Zero in a default-constructed
        // ShotInfo (status ticks carry one), which must still read as "one pellet,
        // full damage" — hence the accessors below rather than public fields.
        private readonly int pelletCount;
        private readonly float damageScale;

        // Pellets in this shot. 1 for single-projectile fire.
        public int PelletCount => pelletCount > 0 ? pelletCount : 1;

        // Multiplier on the BASE damage of a direct hit. Pellets split weapon_damage
        // evenly, so a full-pellet hit deals exactly weapon_damage.
        public float DamageScale => damageScale > 0f ? damageScale : 1f;

        public bool IsPellet => PelletCount > 1;
        public bool IsFinalInBurst => BurstIndex >= BurstCount - 1;
        public bool IsFirstInBurst => BurstIndex == 0;

        public ShotInfo(int shotId, int burstIndex = 0, int burstCount = 1, float chargeLevel = 1f)
            : this(shotId, burstIndex, burstCount, chargeLevel, 0, 1, 1f) { }

        private ShotInfo(int shotId, int burstIndex, int burstCount, float chargeLevel,
                         int pelletIndex, int pelletCount, float damageScale)
        {
            ShotId = shotId;
            BurstIndex = burstIndex;
            BurstCount = burstCount;
            ChargeLevel = chargeLevel;
            PelletIndex = pelletIndex;
            this.pelletCount = pelletCount;
            this.damageScale = damageScale;
        }

        // This shot, as pellet `index` of `count`, each carrying an even share of the
        // base damage.
        public ShotInfo ForPellet(int index, int count)
        {
            int n = count > 0 ? count : 1;
            return new ShotInfo(ShotId, BurstIndex, BurstCount, ChargeLevel, index, n, 1f / n);
        }
    }
}