using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Hazards
{
    // Finds the player and enemies inside an area effect, plus placeholder shape helpers.
    public static class HazardTargets
    {
        public struct Target
        {
            public ICombatant Combatant;
            public PlayerHurtbox Hurtbox;   // set for the player
            public Vector3 Point;
        }

        private static Collider[] overlap = new Collider[128];
        private static readonly HashSet<object> seen = new HashSet<object>();

        // Everyone in a sphere. A zero mask skips that side. Exclude is usually the effect's owner.
        // Accept filters per collider, so one stray limb outside an area can't drop the whole body.
        public static void InSphere(Vector3 centre, float radius, int playerMask, int enemyMask, ICombatant exclude,
                                    List<Target> results, System.Func<Vector3, bool> accept = null)
        {
            results.Clear();
            seen.Clear();
            int mask = playerMask | enemyMask;
            if (mask == 0 || radius <= 0f) return;

            // Every body carries many hitboxes; grow the buffer rather than miss enemies.
            int n = Physics.OverlapSphereNonAlloc(centre, radius, overlap, mask, QueryTriggerInteraction.Collide);
            while (n == overlap.Length)
            {
                overlap = new Collider[overlap.Length * 2];
                n = Physics.OverlapSphereNonAlloc(centre, radius, overlap, mask, QueryTriggerInteraction.Collide);
            }
            for (int i = 0; i < n; i++)
            {
                var col = overlap[i];
                if (col == null) continue;
                int bit = 1 << col.gameObject.layer;
                Vector3 point = col.ClosestPoint(centre);
                if (accept != null && !accept(point)) continue;

                if ((playerMask & bit) != 0)
                {
                    var hurt = col.GetComponentInParent<PlayerHurtbox>();
                    if (hurt != null)
                    {
                        var t = hurt.Target;
                        if (t != null && t != exclude && !t.IsDying && seen.Add(hurt))
                            results.Add(new Target { Combatant = t, Hurtbox = hurt, Point = point });
                        continue;
                    }
                }

                if ((enemyMask & bit) != 0)
                {
                    var hb = col.GetComponentInParent<EnemyHitbox>();
                    if (hb == null || hb.combatant == null) continue;
                    ICombatant c = hb.combatant;
                    if (c == exclude || c.IsDying || !seen.Add(c)) continue;
                    results.Add(new Target { Combatant = c, Point = point });
                }
            }
        }

        // Everyone standing in a pool's cylinder.
        public static void InPool(HazardPool pool, int playerMask, int enemyMask, ICombatant exclude, List<Target> results)
        {
            float r = pool.Radius, h = pool.Height;
            Vector3 foot = pool.transform.position;
            InSphere(foot + Vector3.up * (h * 0.5f), Mathf.Sqrt(r * r + h * h * 0.25f), playerMask, enemyMask, exclude, results,
                p =>
                {
                    Vector3 d = p - foot;
                    return d.y >= -0.5f && d.y <= h && d.x * d.x + d.z * d.z <= r * r;
                });
        }

        // Primitive with no collider, tinted through a property block.
        public static Renderer MakeShape(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            return go.GetComponent<Renderer>();
        }

        public static void Tint(Renderer r, MaterialPropertyBlock block, Color c)
        {
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }
    }
}
