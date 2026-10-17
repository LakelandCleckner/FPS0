using UnityEngine;

namespace Combat.Weapons
{
    // Range falloff math, shared by runtime damage and debug tools
    public static class WeaponFalloff
    {
        // ADS band mapped by range, scaled hip -> ADS
        public static float Start(WeaponArchetypeSO a, float range, float aimBlend)
        {
            if (a == null) return float.MaxValue;
            float ads = Mathf.Lerp(a.falloffStartAtMinRange, a.falloffStartAtMaxRange, Mathf.Clamp(range, 0f, 100f) / 100f);
            return ads * Mathf.Lerp(a.hipFalloffMultiplier, 1f, Mathf.Clamp01(aimBlend));
        }

        public static float End(WeaponArchetypeSO a, float range, float aimBlend)
            => a == null ? float.MaxValue : Start(a, range, aimBlend) * Mathf.Max(1f, a.falloffEndMultiplier);

        public static float Multiplier(WeaponArchetypeSO a, float range, float distance, float aimBlend)
        {
            if (a == null) return 1f;

            float start = Start(a, range, aimBlend);
            if (distance <= start) return 1f;

            float end = End(a, range, aimBlend);
            if (distance >= end || end <= start) return a.minDamagePercent;

            float t = (distance - start) / (end - start);
            float shape = a.falloffCurve != null && a.falloffCurve.length > 0
                ? Mathf.Clamp01(a.falloffCurve.Evaluate(t))
                : 1f - t;

            return Mathf.Lerp(a.minDamagePercent, 1f, shape);
        }
    }
}
