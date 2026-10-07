using System.Collections.Generic;
using UnityEngine;
using Combat.Delivery;

namespace Combat.Weapons
{
    // Pellet (shotgun) delivery: wraps another delivery and fires it once per pellet in
    // a fixed pattern. Pellet count = pattern length.
    [CreateAssetMenu(fileName = "PelletDelivery", menuName = "Combat/Weapons/Delivery/Pellet")]
    public class PelletDeliverySO : DeliverySO
    {
        [Tooltip("What each pellet is fired as (hitscan or projectile).")]
        public DeliverySO pelletDelivery;

        [Tooltip("Degrees from center to the pattern's EDGE (a point at radius 1). " +
                 "Multiplied by the weapon's pellet_spread stat.")]
        public float spreadAngle = 4f;

        [Tooltip("Pellet positions inside the unit circle (x right, y up). Same every shot. " +
                 "Use the context menu to generate a starting pattern.")]
        public List<Vector2> pattern = new List<Vector2>();

        public override IDelivery CreateDelivery(in DeliveryBuildContext ctx)
        {
            if (pelletDelivery == null || pelletDelivery == this)
            {
                Debug.LogError($"[{name}] PelletDeliverySO needs a pellet delivery (and not itself).");
                return null;
            }

            var inner = pelletDelivery.CreateDelivery(ctx);
            if (inner == null) return null;

            return new PelletDelivery(inner, pattern.ToArray(), spreadAngle);
        }

        private void Reset() => GenerateCenterRing8();

        // 1 center + 7 around a ring: a classic, readable 8-pellet spread.
        [ContextMenu("Generate Pattern/Center + Ring (8)")]
        public void GenerateCenterRing8()
        {
            pattern = new List<Vector2> { Vector2.zero };
            AddRing(7, 1f, 0f);
        }

        // 3 inner + 6 outer, offset so no pellet lines up with its neighbour.
        [ContextMenu("Generate Pattern/Two Rings (9)")]
        public void GenerateTwoRings9()
        {
            pattern = new List<Vector2>();
            AddRing(3, 0.45f, 90f);
            AddRing(6, 1f, 0f);
        }

        private void AddRing(int count, float radius, float startDegrees)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (startDegrees + 360f * i / count) * Mathf.Deg2Rad;
                pattern.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }
    }
}