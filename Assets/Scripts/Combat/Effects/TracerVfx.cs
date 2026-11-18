using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Effects
{
    // Visual bullets for hitscan shots. Damage is already resolved; a tracer flies from the muzzle to the
    // end point as a coloured streak (core over glow) plus the element's tracer prefab, then plays the
    // impact effect on arrival and lingers briefly so trails can fade. All pooled.
    // Hitscan uses Fire (flies on its own). Projectiles use Attach/Detach (the tracer follows the projectile).
    // With a RoundSO the round's model flies at the head; elements add a glow shell and a smaller element body.
    public sealed class TracerVfx : MonoBehaviour
    {
        private const float CoreWidth = 0.035f;
        private const float GlowWidth = 0.14f;
        private const float Intensity = 2.5f;

        // Opaque handle for an attached tracer.
        public sealed class Tracer
        {
            internal Transform Follow;
            internal bool Following;
            internal LineRenderer Core, Glow;
            internal GameObject Body;
            internal DamageTypeSO Type;
            internal Vector3 From, Dir;
            internal float Distance, Travelled, LingerUntil;
            internal bool Impact, Arrived;
            internal Vector3 Normal;
            internal Color Hue;
            internal RoundSO Round;
            internal Transform RoundT;
            internal MeshFilter RoundF, ShellF;
            internal MeshRenderer RoundR, ShellR;
            internal float LengthMul, WidthMul;
            internal float StartDist, BodyScale;
            internal Gradient CoreGrad, GlowGrad;
        }

        private static TracerVfx instance;
        private readonly List<Tracer> live = new List<Tracer>(64);
        private readonly Stack<Tracer> free = new Stack<Tracer>(64);
        private readonly Dictionary<DamageTypeSO, float> lastImpact = new Dictionary<DamageTypeSO, float>();
        private readonly Vector3[] two = new Vector3[2];
        private readonly GradientColorKey[] colorKeys = new GradientColorKey[2];
        private readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[3];
        // Fraction of the streak (from the tail) that fades in; the rest is solid
        private const float TailFade = 0.35f;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        // True when this type draws tracers; the tracer then owns the impact effect.
        public static bool Uses(DamageTypeSO type) => type != null && type.tracer && type.tracerSpeed > 0f;

        private static TracerVfx Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[TracerVfx]");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<TracerVfx>();
                }
                return instance;
            }
        }

        // One tracer from 'from' to 'to'. impact = play the type's hit effect where it lands.
        // normal = surface normal at 'to' for the impact; zero = face back along the shot.
        public static void Fire(Vector3 from, Vector3 to, DamageTypeSO type, bool impact, RoundSO round = null,
                                Vector3 normal = default)
        {
            if (!Uses(type)) return;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.05f) { if (impact) Instance.PlayImpactThrottled(type, to, normal); return; }
            var t = Instance.Spawn(from, delta / dist, type, round);
            t.Distance = dist;
            t.Impact = impact;
            t.Normal = normal;
        }

        // A tracer that follows a moving object until Detach. Null when the type has no tracer.
        public static Tracer Attach(Transform follow, Vector3 direction, DamageTypeSO type, RoundSO round = null)
        {
            if (!Uses(type) || follow == null) return null;
            var t = Instance.Spawn(follow.position, direction.normalized, type, round);
            t.Follow = follow;
            t.Following = true;
            t.Distance = float.MaxValue;
            return t;
        }

        // Stops following at 'end'; the tracer lingers there. impact = play the hit effect at 'end'.
        public static void Detach(Tracer t, Vector3 end, bool impact, Vector3 normal = default)
        {
            if (t == null || !t.Following || instance == null) return;
            t.Follow = null;
            t.Following = false;
            t.Distance = Mathf.Max(0.01f, Vector3.Dot(end - t.From, t.Dir));
            t.Impact = impact;
            t.Normal = normal;
        }

        // The type's hit effect at a point, throttled per type (pierce hits mid-flight).
        public static void PlayImpact(DamageTypeSO type, Vector3 at, Vector3 normal = default)
        {
            if (type != null) Instance.PlayImpactThrottled(type, at, normal);
        }

        private Tracer Spawn(Vector3 from, Vector3 dir, DamageTypeSO type, RoundSO round)
        {
            var t = free.Count > 0 ? free.Pop() : Create();
            t.Type = type;
            t.From = from;
            t.Dir = dir;
            t.Distance = 0f;
            t.Travelled = 0f;
            t.Impact = false;
            t.Normal = Vector3.zero;
            t.Arrived = false;
            t.Follow = null;
            t.Following = false;

            Color.RGBToHSV(type.color, out float h, out float s, out float v);
            // Low-saturation types (Ballistic) stay near white; elements get a strong hue
            bool elemental = s >= 0.15f;
            t.Hue = elemental ? Color.HSVToRGB(h, Mathf.Max(s, 0.9f), 1f) : Color.HSVToRGB(0.1f, 0.25f, 1f);
            t.Round = round;
            t.LengthMul = round != null ? round.streakLength : 1f;
            t.WidthMul = round != null ? round.streakWidth : 1f;
            t.StartDist = round != null ? round.hideNearMuzzle : 0f;

            var mat = ArcVfx.LineMaterial();
            t.Core.sharedMaterial = mat;
            t.Glow.sharedMaterial = mat;
            SetStreak(t, from, from, 1f);
            t.Core.gameObject.SetActive(type.tracerStreak);
            t.Glow.gameObject.SetActive(type.tracerStreak);

            // Body spawns once the shot clears the start distance (see Update)
            t.BodyScale = type.tracerScale * (round != null ? round.elementBodyScale : 1f);
            t.Body = null;

            ShowRound(t, from, elemental);

            live.Add(t);
            return t;
        }

        private void Update()
        {
            if (VfxPool.Quitting) return;
            float now = Time.time;
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var t = live[i];
                var type = t.Type;

                if (t.Following)
                {
                    // Followed object gone without a Detach: stop where it was
                    if (t.Follow == null || !t.Follow.gameObject.activeInHierarchy)
                    {
                        t.Following = false;
                        t.Distance = Mathf.Max(0.01f, t.Travelled);
                    }
                    else
                    {
                        t.Travelled = Mathf.Max(0f, Vector3.Dot(t.Follow.position - t.From, t.Dir));
                    }
                }
                else
                {
                    t.Travelled += type.tracerSpeed * dt;
                }

                float head = Mathf.Min(t.Travelled, t.Distance);
                float tail = Mathf.Clamp(t.Travelled - type.tracerLength * t.LengthMul, 0f, t.Distance);
                Vector3 headPos = t.From + t.Dir * head;

                if (!t.Arrived && t.Travelled >= t.Distance)
                {
                    t.Arrived = true;
                    t.LingerUntil = now + type.tracerLinger;
                    if (t.Impact) PlayImpactThrottled(type, headPos, t.Normal.sqrMagnitude > 0.01f ? t.Normal : -t.Dir);
                }

                // Nothing is drawn inside the start distance so the muzzle stays clear
                bool clear = head >= t.StartDist;
                if (t.Body == null && clear && !t.Arrived && type.tracerVfx != null && t.BodyScale > 0f)
                    t.Body = VfxPool.Spawn(type.tracerVfx, headPos, Quaternion.LookRotation(t.Dir), null, type.color, 0f,
                                           t.BodyScale, type.colorizeVfx);
                if (t.Body != null) t.Body.transform.position = headPos;
                if (t.RoundT != null && t.Round != null)
                {
                    // Hidden near the muzzle and once spent on landing
                    bool show = !t.Arrived && clear;
                    if (show) t.RoundT.position = headPos;
                    if (t.RoundT.gameObject.activeSelf != show) t.RoundT.gameObject.SetActive(show);
                }

                float fade = t.Arrived ? Mathf.Clamp01((t.LingerUntil - now) / Mathf.Max(0.01f, type.tracerLinger)) : 1f;
                tail = Mathf.Min(Mathf.Max(tail, t.StartDist), head);
                SetStreak(t, t.From + t.Dir * tail, headPos, clear ? fade : 0f);

                if (t.Arrived && now >= t.LingerUntil)
                {
                    if (t.Body != null) VfxPool.Release(t.Body);
                    t.Body = null;
                    t.Core.gameObject.SetActive(false);
                    t.Glow.gameObject.SetActive(false);
                    if (t.RoundT != null) t.RoundT.gameObject.SetActive(false);
                    t.Round = null;
                    live[i] = live[live.Count - 1];
                    live.RemoveAt(live.Count - 1);
                    free.Push(t);
                }
            }
        }

        private void SetStreak(Tracer t, Vector3 tail, Vector3 head, float alpha)
        {
            two[0] = tail;
            two[1] = head;
            t.Core.positionCount = 2;
            t.Core.SetPositions(two);
            t.Glow.positionCount = 2;
            t.Glow.SetPositions(two);

            float w = (t.Type != null ? t.Type.tracerWidth : 1f) * t.WidthMul;
            t.Core.widthMultiplier = CoreWidth * w;
            t.Glow.widthMultiplier = GlowWidth * w;

            // Fades in over the tail, solid and bright for the rest
            SetGradient(t.Core, t.CoreGrad, Color.Lerp(t.Hue, Color.white, 0.3f) * Intensity, alpha);
            SetGradient(t.Glow, t.GlowGrad, t.Hue * Intensity, alpha);
        }

        private void SetGradient(LineRenderer line, Gradient g, Color c, float alpha)
        {
            colorKeys[0] = new GradientColorKey(c, 0f);
            colorKeys[1] = new GradientColorKey(c, 1f);
            alphaKeys[0] = new GradientAlphaKey(0f, 0f);
            alphaKeys[1] = new GradientAlphaKey(alpha, TailFade);
            alphaKeys[2] = new GradientAlphaKey(alpha, 1f);
            g.SetKeys(colorKeys, alphaKeys);
            line.colorGradient = g;
        }

        // Places the round model at the head, tinted; elements add an additive shell in the element hue.
        private void ShowRound(Tracer t, Vector3 at, bool elemental)
        {
            var round = t.Round;
            if (round == null)
            {
                if (t.RoundT != null) t.RoundT.gameObject.SetActive(false);
                return;
            }
            if (t.RoundT == null) CreateRound(t);

            var mesh = round.Mesh;
            t.RoundT.SetPositionAndRotation(at, Quaternion.LookRotation(t.Dir) * Quaternion.Euler(round.rotation));
            t.RoundT.localScale = round.scale;

            t.RoundF.sharedMesh = mesh;
            t.RoundR.sharedMaterial = round.material != null ? round.material : ArcVfx.LineMaterial();
            block ??= new MaterialPropertyBlock();
            if (round.tint != Color.white || round.material == null)
            {
                block.Clear();
                block.SetColor(BaseColorId, round.tint);
                block.SetColor(ColorId, round.tint);
                t.RoundR.SetPropertyBlock(block);
            }
            else t.RoundR.SetPropertyBlock(null);

            bool shell = elemental && round.elementGlow;
            t.ShellR.gameObject.SetActive(shell);
            if (shell)
            {
                t.ShellF.sharedMesh = mesh;
                t.ShellR.transform.localScale = Vector3.one * round.glowSize;
                t.ShellR.sharedMaterial = ArcVfx.LineMaterial();
                var c = t.Hue * Intensity;
                block.Clear();
                block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, round.glowAlpha));
                block.SetColor(ColorId, new Color(c.r, c.g, c.b, round.glowAlpha));
                t.ShellR.SetPropertyBlock(block);
            }

            t.RoundT.gameObject.SetActive(round.hideNearMuzzle <= 0f);
        }

        private void CreateRound(Tracer t)
        {
            var go = new GameObject("Round");
            go.transform.SetParent(transform, false);
            t.RoundT = go.transform;
            t.RoundF = go.AddComponent<MeshFilter>();
            t.RoundR = go.AddComponent<MeshRenderer>();
            t.RoundR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var shell = new GameObject("Shell");
            shell.transform.SetParent(go.transform, false);
            t.ShellF = shell.AddComponent<MeshFilter>();
            t.ShellR = shell.AddComponent<MeshRenderer>();
            t.ShellR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.ShellR.receiveShadows = false;
            go.SetActive(false);
        }

        // The type's hit effect, throttled per type so fast guns don't stack them.
        private void PlayImpactThrottled(DamageTypeSO type, Vector3 at, Vector3 normal)
        {
            if (type.hitVfx == null) return;
            float now = Time.time;
            if (lastImpact.TryGetValue(type, out float last) && now - last < type.hitVfxInterval) return;
            lastImpact[type] = now;
            // Hit effects face +Z: point it out of the surface
            var rot = normal.sqrMagnitude > 0.01f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            VfxPool.Spawn(type.hitVfx, at, rot, null, type.color, type.hitVfxLifetime,
                          type.hitVfxScale, type.colorizeVfx);
        }

        private Tracer Create() => new Tracer
        {
            Glow = Line("Glow"), Core = Line("Core"),
            CoreGrad = new Gradient(), GlowGrad = new Gradient()
        };

        private LineRenderer Line(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            go.SetActive(false);
            return line;
        }
    }
}
