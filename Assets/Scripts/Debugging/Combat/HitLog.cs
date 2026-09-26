using UnityEngine;

namespace Combat.Core
{
    // Diagnostic hit tracing, compiled out by default. Mirrors StatusLog.
    //
    // [Conditional] removes the ENTIRE call at every call site, including evaluation
    // of the arguments, so none of the interpolated strings exist unless you opt in.
    //
    // To turn it on: Project Settings > Player > Other Settings > Scripting Define
    // Symbols, add HIT_DEBUG.
    //
    // Enabled is a runtime mute on top of the define. WeaponHitResolver.logHits
    // pushes it in Awake/OnValidate, so one inspector checkbox silences the resolver
    // AND the delivery-side lines.
    //
    // Line prefixes:
    //   [HitDebug:Hitscan] / [HitDebug:Projectile]  what the ray struck, which hitbox
    //                                               it resolved to, or why it dropped
    //   [HitDebug]                                  the resolved outcome
    public static class HitLog
    {
        public static bool Enabled = true;

        [System.Diagnostics.Conditional("HIT_DEBUG")]
        public static void Log(string message, Object context = null)
        {
            if (Enabled) Debug.Log(message, context);
        }

        [System.Diagnostics.Conditional("HIT_DEBUG")]
        public static void Warn(string message, Object context = null)
        {
            if (Enabled) Debug.LogWarning(message, context);
        }

        // ---- formatting helpers (only ever evaluated inside a stripped call) ----

        // Unity-null aware: a destroyed object reads as <destroyed>, not a throw.
        public static string NameOf(object o)
        {
            if (o is Object uo) return uo != null ? uo.name : "<destroyed>";
            return o != null ? o.ToString() : "<none>";
        }

        public static string DescribeCollider(Collider col)
        {
            if (col == null) return "hit <null collider>";
            string layer = LayerMask.LayerToName(col.gameObject.layer);
            if (string.IsNullOrEmpty(layer)) layer = "?";
            return $"hit '{col.name}' layer={layer}({col.gameObject.layer})" +
                   (col.isTrigger ? " TRIGGER" : "");
        }

        // INHERITED = the collider has no hitbox of its own and GetComponentInParent
        // walked up the hierarchy to an ancestor's. On a bone chain that usually means
        // a limb reporting as whatever part its nearest hitboxed ancestor is.
        public static string DescribeHitbox(Collider col, Component hitbox, BodyPart part, float mult)
        {
            bool inherited = hitbox != null && col != null && hitbox.gameObject != col.gameObject;
            return $"{DescribeCollider(col)} -> hitbox on '{NameOf(hitbox)}' " +
                   $"{part} x{mult:F2}" + (inherited ? " [INHERITED]" : "");
        }
    }
}
