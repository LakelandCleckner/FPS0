using UnityEngine;

namespace Combat.Core
{
    // One damaging hit as the target saw it, for hit reactions
    public struct HitReport
    {
        public float Damage;          // after defense, before any shield
        public float HealthDamage;    // what reached health (filled by CombatantHealth)
        public BodyPart Part;
        public DamageTypeSO Type;
        public HitSource Source;
        public Vector3 Point;
        public Vector3 Direction;     // travel direction of the hit, zero when unknown
        public float StaggerPower;    // 1 = normal

        public HitReport(float damage, BodyPart part, DamageTypeSO type, HitSource source,
                         Vector3 point, Vector3 direction, float staggerPower)
        {
            Damage = damage;
            HealthDamage = 0f;
            Part = part;
            Type = type;
            Source = source;
            Point = point;
            Direction = direction;
            StaggerPower = staggerPower;
        }
    }

    // A damage source that staggers harder or softer than normal
    public interface IStaggerSource
    {
        float StaggerPower { get; }
    }
}
