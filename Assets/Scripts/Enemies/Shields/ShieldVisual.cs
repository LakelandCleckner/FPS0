using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Enemies
{
    // Shell around a shielded enemy: fades in on spawn and refill, flashes where hit, flickers when low, pops on break.
    // Added and bound by EnemyDefinitionApplier next to the EnemyShield.
    public class ShieldVisual : MonoBehaviour
    {
        private enum State { Hidden, Rising, Up, Popping }

        private const float RiseTime = 0.35f;
        private const float RiseFlicker = 0.12f;
        private const float PopTime = 0.14f;
        private const int BurstCount = 36;

        private EnemyShield shield;
        private CombatantHealth health;
        private EnemyShieldSO def;
        private GameObject shell;
        private GameObject shellPrefab;
        private ShieldShell info;
        private Renderer[] shellRenderers;
        private readonly List<Renderer> body = new List<Renderer>();
        private Vector3 fitScale = Vector3.one;
        private State state;
        private float t;
        private float pulse;
        private bool shown = true;
        private float offUntil;
        private float nextFlicker;
        private float lastHitVfx = -999f;
        private ParticleSystem burst;

        private static Material burstMaterial;

        public void Bind(EnemyShield s, CombatantHealth h)
        {
            if (shield != s)
            {
                if (shield != null) { shield.Raised -= OnRaised; shield.Broken -= OnBroken; }
                shield = s;
                if (shield != null) { shield.Raised += OnRaised; shield.Broken += OnBroken; }
            }
            if (health != h)
            {
                if (health != null) { health.Damaged -= OnDamaged; health.OnDeath -= OnDeath; }
                health = h;
                if (health != null) { health.Damaged += OnDamaged; health.OnDeath += OnDeath; }
            }
            Sync();
        }

        private void OnDestroy()
        {
            if (shield != null) { shield.Raised -= OnRaised; shield.Broken -= OnBroken; }
            if (health != null) { health.Damaged -= OnDamaged; health.OnDeath -= OnDeath; }
        }

        // Released while the enemy goes back to its pool; the next spawn's apply raises a fresh shell.
        private void OnDisable()
        {
            if (shell != null) VfxPool.ReleaseLater(shell);
            shell = null;
            state = State.Hidden;
        }

        private void Sync()
        {
            def = shield != null ? shield.Definition : null;
            var prefab = def != null ? def.shellVfx : null;
            if (prefab != shellPrefab) Release();
            shellPrefab = prefab;
            if (prefab == null || !isActiveAndEnabled) return;
            if (shield.Up) Raise(); else Hide();
        }

        private void OnRaised() { if (isActiveAndEnabled) Sync(); }
        private void OnDeath() => Hide();

        // ------------------------------------------------------------------ states

        private void Raise()
        {
            if (shellPrefab == null) return;
            if (shell == null)
            {
                Color tint = def.tintVfx ? def.Color : Color.clear;
                shell = VfxPool.Spawn(shellPrefab, transform.position, transform.rotation, transform, tint, 0f, 1f, def.colorizeVfx);
                if (shell == null) return;
                info = shell.GetComponent<ShieldShell>();
                shellRenderers = shell.GetComponentsInChildren<Renderer>(true);
            }
            else if (!shell.activeSelf) shell.SetActive(true);

            Fit();
            state = State.Rising;
            t = 0f;
            pulse = 0f;
            SetShown(true);
        }

        private void Hide()
        {
            if (shell != null) shell.SetActive(false);
            state = State.Hidden;
        }

        private void Release()
        {
            if (shell != null) VfxPool.Release(shell);
            shell = null;
            info = null;
            state = State.Hidden;
        }

        private void OnBroken()
        {
            if (shell == null || !shell.activeSelf || state == State.Hidden) return;
            state = State.Popping;
            t = 0f;
            Burst();
        }

        // Ellipsoid around the body bounds, or a sphere for shells with spinning parts.
        private void Fit()
        {
            body.Clear();
            foreach (var r in EnemyLook.FindRenderers(gameObject))
                if (r != null && r.enabled && !r.transform.IsChildOf(shell.transform)) body.Add(r);

            Vector3 centre = transform.position + Vector3.up;
            Vector3 ext = new Vector3(0.4f, 0.9f, 0.4f);
            if (body.Count > 0)
            {
                var b = body[0].bounds;
                for (int i = 1; i < body.Count; i++) b.Encapsulate(body[i].bounds);
                centre = b.center;
                ext = b.extents;
            }

            Vector3 ls = transform.lossyScale;
            float sx = Mathf.Max(0.01f, ls.x), sy = Mathf.Max(0.01f, ls.y);
            float pad = def.shellPadding;
            float baseR = info != null ? info.baseRadius : 1f;
            float rxz = (Mathf.Max(ext.x, ext.z) + pad) / baseR;
            float ry = (ext.y + pad) / baseR;
            if (info != null && info.uniformFit)
            {
                // Even scale; the orbit stretches instead so the parts don't warp.
                info.stretchY = ry / Mathf.Max(0.01f, rxz);
                fitScale = new Vector3(rxz / sx, rxz / sy, rxz / sx);
            }
            else
            {
                if (info != null) info.stretchY = 1f;
                fitScale = new Vector3(rxz / sx, ry / sy, rxz / sx);
            }

            Vector3 offset = info != null ? Vector3.Scale(info.centerOffset, fitScale) : Vector3.zero;
            shell.transform.localPosition = transform.InverseTransformPoint(centre) - offset;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = fitScale * 0.6f;
            if (info != null) info.KeepPartSize(shell.transform.lossyScale);
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (shell == null || !shell.activeSelf || state == State.Hidden) return;
            t += Time.deltaTime;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 0.5f);
            float s = 1f;

            switch (state)
            {
                case State.Rising:
                    float k = Mathf.Clamp01(t / RiseTime);
                    s = Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(k));
                    // Stutters on as it forms
                    SetShown(t >= RiseFlicker || Random.value > 0.45f);
                    if (k >= 1f) { state = State.Up; SetShown(true); }
                    break;

                case State.Up:
                    s = 1f + pulse;
                    LowFlicker();
                    break;

                case State.Popping:
                    float p = Mathf.Clamp01(t / PopTime);
                    s = Mathf.Lerp(1f, 1.3f, p);
                    SetShown(Random.value > 0.5f);
                    if (p >= 1f) { Hide(); return; }
                    break;
            }
            shell.transform.localScale = fitScale * s;
            if (info != null)
            {
                info.KeepPartSize(shell.transform.lossyScale);
                info.SetFlash(Mathf.Clamp01(pulse / 0.04f));
            }
        }

        // Random short drop-outs below the threshold, closer together as the shield empties.
        private void LowFlicker()
        {
            float below = def.flickerBelow;
            float fill = shield != null ? shield.Fill : 1f;
            float now = Time.time;
            if (below <= 0f || fill >= below)
            {
                if (!shown) SetShown(true);
                return;
            }
            if (!shown)
            {
                if (now >= offUntil) SetShown(true);
                return;
            }
            if (now < nextFlicker) return;
            float severity = 1f - fill / below;
            offUntil = now + Random.Range(0.03f, 0.07f + 0.05f * severity);
            nextFlicker = offUntil + Random.Range(0.05f, Mathf.Lerp(0.9f, 0.15f, severity));
            SetShown(false);
        }

        private void SetShown(bool on)
        {
            if (shown == on || shellRenderers == null) return;
            shown = on;
            for (int i = 0; i < shellRenderers.Length; i++)
                if (shellRenderers[i] != null) shellRenderers[i].enabled = on;
        }

        private static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        // ------------------------------------------------------------------ hits and pop

        private void OnDamaged(HitReport r)
        {
            if (shell == null || state != State.Up && state != State.Rising) return;
            if (shield == null || shield.LastAbsorbTime != Time.time || !shield.Up) return;
            pulse = Mathf.Max(pulse, 0.04f);
            if (def.hitVfx == null || r.Source != HitSource.Direct) return;
            if (Time.time - lastHitVfx < def.hitVfxInterval) return;
            lastHitVfx = Time.time;

            Vector3 centre = ShellCentre();
            Vector3 local = shell.transform.InverseTransformPoint(r.Point) - (info != null ? info.centerOffset : Vector3.zero);
            if (local.sqrMagnitude < 1e-6f) local = shell.transform.InverseTransformDirection(-r.Direction);
            Vector3 surface = shell.transform.TransformPoint(Surface(local));
            Vector3 normal = surface - centre;
            var rot = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            Color tint = def.tintVfx ? def.Color : Color.clear;
            VfxPool.Spawn(def.hitVfx, surface, rot, null, tint, def.hitVfxLifetime, def.hitVfxScale, def.colorizeVfx);
        }

        private Vector3 Surface(Vector3 dir) => info != null ? info.Surface(dir) : dir.normalized;

        private Vector3 ShellCentre() => shell.transform.TransformPoint(info != null ? info.centerOffset : Vector3.zero);

        // Shards flung off the shell surface in the shield colour.
        private void Burst()
        {
            if (burst == null) burst = CreateBurst();
            Vector3 centre = ShellCentre();
            Color c = def.Color;
            for (int i = 0; i < BurstCount; i++)
            {
                Vector3 at = shell.transform.TransformPoint(Surface(Random.onUnitSphere));
                var e = new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = (at - centre).normalized * Random.Range(3f, 7f),
                    startColor = Color.Lerp(c, Color.white, Random.Range(0f, 0.35f)),
                    startSize = Random.Range(0.04f, 0.1f),
                    startLifetime = Random.Range(0.25f, 0.5f),
                    applyShapeToPosition = false
                };
                burst.Emit(e, 1);
            }
        }

        private ParticleSystem CreateBurst()
        {
            var go = new GameObject("ShieldBurst");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = BurstMaterial();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.6f;
            main.maxParticles = BurstCount * 3;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2f;
            go.SetActive(true);
            return ps;
        }

        // Additive soft dot shared by every burst.
        private static Material BurstMaterial()
        {
            if (burstMaterial != null) return burstMaterial;
            burstMaterial = new Material(ArcVfx.LineMaterial()) { name = "ShieldBurst" };
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ShieldBurstDot", wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    px[y * n + x] = new Color(1f, 1f, 1f, (1f - d) * (1f - d));
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            if (burstMaterial.HasProperty("_BaseMap")) burstMaterial.SetTexture("_BaseMap", tex);
            if (burstMaterial.HasProperty("_MainTex")) burstMaterial.SetTexture("_MainTex", tex);
            return burstMaterial;
        }
    }
}
