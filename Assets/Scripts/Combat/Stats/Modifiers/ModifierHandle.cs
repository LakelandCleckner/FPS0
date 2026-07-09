namespace Combat.Stats
{
    // Identity for a registered modifier (GDD 14 §4). Every AddModifier returns one.
    //
    //   Id    — globally unique per registration. The REAL identity: two skill nodes
    //           on the same player, or two copies of the same affix, get different
    //           Ids and are independently removable. Ids never collide.
    //   Owner — the source object (weapon, player, buff instance). Used ONLY for
    //           bulk removal ("remove everything from this weapon" on unequip;
    //           "remove everything from the tree" on respec). May be null (then the
    //           modifier just can't be bulk-removed by owner).
    //
    // Precise removal is by Id (RemoveModifier); bulk removal is by Owner
    // (RemoveAllFromOwner). This avoids the object-reference-as-handle flaw where
    // one object sourcing many independent modifiers would remove them all together.
    public readonly struct ModifierHandle
    {
        public readonly long Id;
        public readonly object Owner;

        public ModifierHandle(long id, object owner)
        {
            Id = id;
            Owner = owner;
        }

        public bool IsValid => Id != 0;

        // A never-valid handle (Id 0) for "no handle" returns.
        public static readonly ModifierHandle None = new ModifierHandle(0, null);
    }
}
