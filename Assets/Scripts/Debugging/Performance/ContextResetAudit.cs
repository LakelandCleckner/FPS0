#if UNITY_EDITOR || STATUS_DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Combat.Core;
using Combat.Delivery;

namespace Combat.Diagnostics
{
    // RESET AUDIT — catches the WasCrit bug class at the point of introduction.
    //
    // HitContext and Projectile are both REUSED now (one context per EffectStackPool,
    // one per pooled Projectile). Reuse means every piece of mutable state has to be
    // deliberately placed in one of a few buckets: set once and never touched again,
    // or reset on every refill, or owned by something else entirely. A field that
    // belongs in "reset every refill" but isn't reset carries state from a previous
    // resolution into the next one.
    //
    // That bug is nearly undebuggable when the field has no loud failure mode. WasCrit
    // sat unreset for months precisely because a stale crit only changes damage-number
    // styling — nothing throws, no damage is wrong, it occasionally just looks fancy.
    // A stale WasKill would be worse: it fires a phantom Kill event, which under perks
    // reads as a balance problem rather than a stale field.
    //
    // So this does NOT try to spot the bug by eye. It enumerates the real members by
    // reflection and asserts every one is accounted for in exactly one declared bucket
    // per reuse site. Adding a field to HitContext then FAILS THIS AUDIT until someone
    // decides which bucket it belongs in — which catches the next version of the bug
    // rather than the last one.
    //
    // WHAT IT CATCHES: a new/renamed/removed member that nobody classified, and any
    // publicly-settable property (an unmonitored write surface).
    //
    // WHAT IT DOES NOT CATCH: a caller that stops honouring its own declared list —
    // if PerRefill says WasKill but BuildTickContext drops the line, this still
    // passes. Classification is the durable half; the line itself is code review.
    // See the note at the bottom about extending this to a behavioural check.
    //
    // Runs once at startup, before the first scene loads, so it can't be forgotten by
    // failing to add a component. Compiles out of builds entirely.
    public static class ContextResetAudit
    {
        // ------------------------------------------------------------------
        // HitContext — reuse site 1: EffectStackPool (one context per status pool)
        // ------------------------------------------------------------------

        // Set once in BuildReusableTickPayload; identical for every tick of this pool.
        private static readonly string[] PoolInvariant =
        {
            "Target",
            "Source",
            "DamageSource",
            "DamageType",
            "HitboxMultiplier",
            "SourceStatus",
            "ShowFloatingNumber",
            "FeedsAccumulator",
            "Attacker",
            "Effects",
            "MaxChainDepth",
            "ChainFalloff",
            "ChainGrowth",
            "DedupMode",
        };

        // Written by an effect or the resolver during a tick, so BuildTickContext must
        // clear them before the next one.
        private static readonly string[] PoolPerRefill =
        {
            "DamageDealt",
            "WasKill",
            "WasHeadshot",
            "WasDebuffed",
            "ChainDepth",
            "alreadyHit",   // via ResetAlreadyHit()
        };

        // Reset elsewhere on the path, not by the refill method.
        private static readonly string[] PoolExternallyManaged =
        {
            "CritMultiplier",   // WeaponHitResolver.RollCrit, top of every resolution
            "WasCrit",          // ditto
        };

        // Deliberately left at construction defaults for this site. A tick has no hit
        // point, no faction of its own on the context, always resolves as Torso, and
        // routes damage through StatusSummedTickEffect rather than these delegates.
        private static readonly string[] PoolUnused =
        {
            "HitPoint",
            "SourceFaction",
            "BodyPartHit",
            "ApplyDamageToTarget",
            "ApplyStatusTickDamage",
            "Shot"
        };

        // ------------------------------------------------------------------
        // HitContext — reuse site 2: Projectile (one context per pooled projectile)
        // ------------------------------------------------------------------

        // Set in Init; constant for the whole flight, across every target it pierces.
        private static readonly string[] ProjectileInvariant =
        {
            "Source",
            "DamageSource",
            "Attacker",
            "SourceFaction",
            "DamageType",
            "Effects",
            "MaxChainDepth",
            "ChainFalloff",
            "ChainGrowth",
            "DedupMode",
            "ApplyDamageToTarget",
            "Shot"
        };

        // Refilled per hit in HandleHit. Note this is a LARGER set than the pool's:
        // a projectile changes target mid-flight when it pierces, so Target, HitPoint
        // and the precision pair are per-hit here but invariant for a status pool.
        // This is why the two sites need separate declarations rather than one.
        private static readonly string[] ProjectilePerRefill =
        {
            "Target",
            "HitPoint",
            "HitboxMultiplier",
            "BodyPartHit",
            "ApplyStatusTickDamage",
            "DamageDealt",
            "WasKill",
            "WasHeadshot",
            "WasDebuffed",
            "ChainDepth",
            "alreadyHit",   // via ResetAlreadyHit()
        };

        private static readonly string[] ProjectileExternallyManaged =
        {
            "CritMultiplier",
            "WasCrit",
        };

        // Status-only presentation. Direct hits want the construction defaults
        // (ShowFloatingNumber true, FeedsAccumulator false, no source status).
        private static readonly string[] ProjectileUnused =
        {
            "SourceStatus",
            "ShowFloatingNumber",
            "FeedsAccumulator",
        };

        // ------------------------------------------------------------------
        // Projectile's own fields — pooling has the same hazard
        // ------------------------------------------------------------------

        // Assigned from Init's parameters every flight.
        private static readonly string[] ProjectileConfigured =
        {
            "resolver",
            "damageSource",
            "attacker",
            "effects",
            "sourceFaction",
            "damageType",
            "maxChainDepth",
            "chainFalloff",
            "chainGrowth",
            "dedupMode",
            "config",
            "direction",
        };

        // Accumulated during flight. MUST be cleared in Init — field initialisers run
        // once per instance, not once per spawn. A stale age despawns the projectile
        // instantly; stale hitTargets makes it refuse to hit an enemy it struck in a
        // previous life.
        private static readonly string[] ProjectilePerFlightReset =
        {
            "distanceTravelled",
            "age",
            "pierceUsed",
            "hitTargets",
            "currentHitbox",
            "initialized",
        };

        // Allocated once per instance and reused across every flight. Resetting these
        // would defeat the pooling they exist for.
        private static readonly string[] ProjectileInstanceLifetime =
        {
            "hitContext",
            "applyBaseDamage",
            "poolOrigin",
        };

        // ------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            var report = new StringBuilder();

            AuditType(typeof(HitContext), "HitContext @ EffectStackPool", report,
                ("invariant-per-pool", PoolInvariant),
                ("reset-per-tick", PoolPerRefill),
                ("externally managed", PoolExternallyManaged),
                ("unused at this site", PoolUnused));

            AuditType(typeof(HitContext), "HitContext @ Projectile", report,
                ("invariant-per-flight", ProjectileInvariant),
                ("reset-per-hit", ProjectilePerRefill),
                ("externally managed", ProjectileExternallyManaged),
                ("unused at this site", ProjectileUnused));

            AuditType(typeof(Projectile), "Projectile (pooled instance)", report,
                ("configured-per-flight", ProjectileConfigured),
                ("reset-per-flight", ProjectilePerFlightReset),
                ("instance lifetime", ProjectileInstanceLifetime));

            AuditSettableProperties(typeof(HitContext), report);

            if (report.Length > 0)
            {
                Debug.LogError(
                    "[ResetAudit] Reused-state classification is out of date. These types " +
                    "are pooled/refilled, so every mutable member must be deliberately " +
                    "placed in a bucket — see ContextResetAudit.\n" + report);
            }
        }

        // Enumerate the real instance fields and check each appears in exactly one
        // declared bucket. Reports both directions: fields nobody classified, and
        // classifications naming fields that no longer exist.
        private static void AuditType(
            Type type,
            string siteLabel,
            StringBuilder report,
            params (string name, string[] members)[] buckets)
        {
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in type.GetFields(BindingFlags.Instance |
                                             BindingFlags.Public |
                                             BindingFlags.NonPublic))
            {
                // Skip compiler-generated auto-property backing fields; the property
                // itself is audited separately where it has a public setter.
                if (f.Name.IndexOf('<') >= 0) continue;
                if (f.IsLiteral || f.IsInitOnly) continue;   // consts and readonly refs
                actual.Add(f.Name);
            }

            // readonly collections still hold mutable CONTENTS, so they matter: a
            // readonly HashSet that never clears is exactly the stale-hitTargets bug.
            foreach (var f in type.GetFields(BindingFlags.Instance |
                                             BindingFlags.Public |
                                             BindingFlags.NonPublic))
            {
                if (f.Name.IndexOf('<') >= 0) continue;
                if (f.IsInitOnly && IsMutableContainer(f.FieldType))
                    actual.Add(f.Name);
            }

            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            foreach (var (bucketName, members) in buckets)
            {
                foreach (var m in members)
                {
                    if (declared.TryGetValue(m, out var already))
                        duplicates.Add($"{m} (in both '{already}' and '{bucketName}')");
                    else
                        declared[m] = bucketName;
                }
            }

            var unclassified = new List<string>();
            foreach (var name in actual)
                if (!declared.ContainsKey(name))
                    unclassified.Add(name);

            var stale = new List<string>();
            foreach (var kv in declared)
                if (!actual.Contains(kv.Key))
                    stale.Add($"{kv.Key} (declared '{kv.Value}')");

            if (unclassified.Count == 0 && stale.Count == 0 && duplicates.Count == 0)
                return;

            report.AppendLine();
            report.AppendLine($"--- {siteLabel} ---");

            if (unclassified.Count > 0)
            {
                unclassified.Sort(StringComparer.Ordinal);
                report.AppendLine(
                    "  UNCLASSIFIED (a member exists that no bucket claims — decide " +
                    "whether it is invariant, must reset on refill, or is managed " +
                    "elsewhere, then add it):");
                foreach (var n in unclassified) report.AppendLine("    " + n);
            }

            if (stale.Count > 0)
            {
                stale.Sort(StringComparer.Ordinal);
                report.AppendLine(
                    "  STALE DECLARATION (named here but not on the type — renamed or " +
                    "removed; the refill code probably needs the same edit):");
                foreach (var n in stale) report.AppendLine("    " + n);
            }

            if (duplicates.Count > 0)
            {
                report.AppendLine("  DUPLICATE (claimed by two buckets):");
                foreach (var n in duplicates) report.AppendLine("    " + n);
            }
        }

        // A publicly-settable property is a write surface the field audit can't see.
        // There are none today; this fails loudly if one appears.
        private static void AuditSettableProperties(Type type, StringBuilder report)
        {
            var offenders = new List<string>();
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var setter = p.GetSetMethod(nonPublic: false);
                if (setter != null) offenders.Add(p.Name);
            }

            if (offenders.Count == 0) return;

            offenders.Sort(StringComparer.Ordinal);
            report.AppendLine();
            report.AppendLine($"--- {type.Name} settable properties ---");
            report.AppendLine(
                "  A public setter is mutable state the field buckets don't cover. " +
                "Either make it a field so it is audited, or give it a private setter " +
                "and a named mutator (see Generation/BumpGeneration):");
            foreach (var n in offenders) report.AppendLine("    " + n);
        }

        private static bool IsMutableContainer(Type t)
        {
            if (!t.IsGenericType) return false;
            var def = t.GetGenericTypeDefinition();
            return def == typeof(HashSet<>)
                || def == typeof(List<>)
                || def == typeof(Dictionary<,>)
                || def == typeof(Queue<>)
                || def == typeof(Stack<>);
        }
    }
}
#endif

// EXTENDING THIS TO A BEHAVIOURAL CHECK
//
// The audit above verifies classification, not conduct: if PerRefill names WasKill but
// BuildTickContext drops the line, it still passes. A behavioural check would dirty
// every PerRefill member with a sentinel, drive one refill, and assert each came back
// to its constructed default.
//
// That needs a refill it can actually drive. Projectile.Init is public and takes plain
// arguments, so a temporary disabled instance could be dirtied via reflection and
// checked — a real test, at the cost of instantiating a GameObject at startup.
// EffectStackPool.BuildTickContext is private and its enclosing Init needs a live
// StatusSO, resolver and target, so driving it means either a fixture asset or
// reflection deep enough to be more fragile than the bug it guards.
//
// Deliberately not built. Classification catches the failure at the moment a field is
// introduced, which is when it is cheap; conduct is caught by the diff.
