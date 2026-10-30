namespace Combat.Weapons
{
    // Which of the loadout weapon slots something may occupy.
    [System.Flags]
    public enum LoadoutSlotMask
    {
        None = 0,
        Slot1 = 1 << 0,
        Slot2 = 1 << 1,
        Slot3 = 1 << 2,
        All = Slot1 | Slot2 | Slot3
    }

    public static class LoadoutSlots
    {
        public const int Count = 3;

        public static bool Allows(LoadoutSlotMask mask, int slot)
            => slot >= 0 && slot < Count && (mask & (LoadoutSlotMask)(1 << slot)) != 0;
    }
}
