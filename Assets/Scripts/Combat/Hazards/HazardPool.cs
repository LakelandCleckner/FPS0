using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Stats;

namespace Combat.Hazards
{
    // Size, timing and look of a ground pool.
    [System.Serializable]
    public class HazardPoolSpec
    {
        public float radius = 2.5f;
        [Tooltip("Height above the ground the pool reaches.")]
        public float height = 2f;
        public float duration = 6f;
        [Tooltip("Seconds between ticks.")]
        public float tickInterval = 0.5f;
        [Tooltip("Seconds to grow to full size.")]
        public float growTime = 0.3f;
        [Tooltip("What the pool settles onto.")]
        public LayerMask groundLayers = ~((1 << 7) | (1 << 8));
        [Tooltip("Colour comes from the damage type; this sets how see-through it is.")]
        [Range(0f, 1f)] public float opacity = 0.45f;
        [Tooltip("Used when there's no damage type.")]
        public Color fallbackColor = new Color(1f, 0.35f, 0.05f, 1f);
        [Tooltip("Speed taken away while inside (0.4 = 40% slower). 0 = none.")]
        [Range(0f, 0.95f)] public float slow = 0f;
        [Tooltip("Stat the slow reduces (move_speed).")]
        public StatDefinitionSO slowStat;

        public Color ColorFor(DamageTypeSO type)
        {
            var c = type != null ? type.color : fallbackColor;
            c.a = opacity;
            return c;
        }
    }

    // One tick for everyone inside. True when anything took damage.
    public delegate bool HazardTick(HazardPool pool, List<HazardTargets.Target> inside);

    // A live ground pool: a tinted disc that ticks, pulses when it lands damage, then fades. Pooled.
    public class HazardPool : MonoBehaviour
    {
        private const float FadeTime = 0.5f;
        private const float GroundProbe = 3f;
        private const float PulseTime = 0.2f;

        private static readonly Stack<HazardPool> free = new Stack<HazardPool>();

        private HazardPoolSpec spec;
        private HazardTick tick;
        private int playerMask;
        private int enemyMask;
        private ICombatant exclude;
        private readonly List<HazardTargets.Target> inside = new List<HazardTargets.Target>(8);
        private HashSet<StatContainer> slowed = new HashSet<StatContainer>();
        private HashSet<StatContainer> slowNext = new HashSet<StatContainer>();
        private Color color;
        private float time;
        private float nextTick;
        private float pulse;
        private Renderer disc;
        private MaterialPropertyBlock block;

        public int Generation { get; private set; }
        public float Height => spec.height;
        public float Radius => spec.radius * (spec.growTime > 0f ? Mathf.Clamp01(time / spec.growTime) : 1f);
        public bool Ticking => time < spec.duration;

        // A zero mask skips that side. Exclude is usually whoever made the pool.
        public static HazardHandle Spawn(HazardPoolSpec spec, Vector3 at, Color color, int playerMask, int enemyMask,
                                         ICombatant exclude, HazardTick tick)
        {
            if (spec == null || spec.duration <= 0f || spec.radius <= 0f) return default;

            HazardPool p = null;
            while (free.Count > 0 && p == null) p = free.Pop();
            if (p == null)
            {
                p = new GameObject("HazardPool").AddComponent<HazardPool>();
                p.block = new MaterialPropertyBlock();
                p.disc = HazardTargets.MakeShape(PrimitiveType.Cylinder, "Disc", p.transform);
            }

            // Sits on the ground under the spawn point.
            if (Physics.Raycast(at + Vector3.up, Vector3.down, out var hit, GroundProbe, spec.groundLayers, QueryTriggerInteraction.Ignore))
                at = hit.point;

            p.spec = spec;
            p.tick = tick;
            p.playerMask = playerMask;
            p.enemyMask = enemyMask;
            p.exclude = exclude;
            p.color = color;
            p.time = 0f;
            p.nextTick = 0f;
            p.pulse = 0f;
            p.transform.position = at;
            p.gameObject.SetActive(true);
            p.Refresh();
            return new HazardHandle(p);
        }

        // Stops ticking and fades out.
        public void Expire()
        {
            if (time < spec.duration) time = spec.duration;
        }

        private void Update()
        {
            time += Time.deltaTime;
            if (time >= spec.duration + FadeTime)
            {
                ClearSlow();
                Generation++;
                tick = null;
                exclude = null;
                gameObject.SetActive(false);
                free.Push(this);
                return;
            }

            if (Ticking && time >= nextTick)
            {
                nextTick = time + Mathf.Max(0.05f, spec.tickInterval);
                HazardTargets.InPool(this, playerMask, enemyMask, exclude, inside);
                bool slowing = UpdateSlow();
                bool landed = tick != null && tick(this, inside);
                if (landed || slowing) pulse = PulseTime;
            }
            else if (!Ticking && slowed.Count > 0) ClearSlow();
            Refresh();
        }

        // Slows everyone inside, lifts it from anyone who left. True when anyone is slowed.
        private bool UpdateSlow()
        {
            if (spec.slow <= 0f || spec.slowStat == null) return false;

            slowNext.Clear();
            for (int i = 0; i < inside.Count; i++)
            {
                var c = inside[i].Combatant.Stats;
                if (c == null || !slowNext.Add(c) || slowed.Contains(c)) continue;
                c.AddModifier(new StatModifier(spec.slowStat, StatResolver.MULTIPLICATIVE, -spec.slow), this);
            }
            foreach (var c in slowed)
                if (!slowNext.Contains(c)) c.RemoveAllFromOwner(this);

            var swap = slowed;
            slowed = slowNext;
            slowNext = swap;
            return slowed.Count > 0;
        }

        private void ClearSlow()
        {
            foreach (var c in slowed) c.RemoveAllFromOwner(this);
            slowed.Clear();
        }

        // Swells and brightens for a moment when a tick lands.
        private void Refresh()
        {
            pulse = Mathf.Max(0f, pulse - Time.deltaTime);
            float p = pulse / PulseTime;
            float r = Mathf.Max(0.01f, Radius) * (1f + 0.08f * p);
            disc.transform.localScale = new Vector3(r * 2f, 0.01f, r * 2f);
            disc.transform.localPosition = Vector3.up * 0.02f;

            float fade = Ticking ? 1f : 1f - (time - spec.duration) / FadeTime;
            var c = Color.Lerp(color, Color.white, 0.35f * p);
            c.a = Mathf.Clamp01((color.a + 0.3f * p) * Mathf.Clamp01(fade));
            HazardTargets.Tint(disc, block, c);
        }
    }

    // A pool reference that goes stale once the pool is recycled.
    public readonly struct HazardHandle
    {
        private readonly HazardPool pool;
        private readonly int generation;

        public HazardHandle(HazardPool pool)
        {
            this.pool = pool;
            generation = pool != null ? pool.Generation : 0;
        }

        public bool Ticking => pool != null && pool.Generation == generation && pool.Ticking;

        public void Expire()
        {
            if (Ticking) pool.Expire();
        }
    }
}
