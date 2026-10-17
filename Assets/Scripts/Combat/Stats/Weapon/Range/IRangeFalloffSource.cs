namespace Combat.Core
{
    // A damage source with distance-based falloff
    public interface IRangeFalloffSource
    {
        // Base damage multiplier at this distance and aim blend
        float FalloffMultiplier(float distance, float aimBlend);
    }
}
