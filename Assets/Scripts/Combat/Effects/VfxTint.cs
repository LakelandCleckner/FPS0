using UnityEngine;

namespace Combat.Effects
{
    // Recolours a spawned VFX instance to a damage type: swaps the hue of every particle (start, lifetime,
    // custom data), light and material colour, keeps brightness (HDR included) and alpha, and never adds saturation, so white cores stay white.
    public static class VfxTint
    {
        private static MaterialPropertyBlock block;
        public static void Apply(GameObject fx, Color tint)
        {
            if (fx == null) return;
            Color.RGBToHSV(tint, out float h, out float s, out _);

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = Tint(main.startColor, h, s);

                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = Tint(col.color, h, s);

                // Shader-driven colours often ride in custom vertex data instead.
                var custom = ps.customData;
                if (custom.enabled)
                    for (int i = 0; i < 2; i++)
                    {
                        var stream = i == 0 ? ParticleSystemCustomData.Custom1 : ParticleSystemCustomData.Custom2;
                        if (custom.GetMode(stream) == ParticleSystemCustomDataMode.Color)
                            custom.SetColor(stream, Tint(custom.GetColor(stream), h, s));
                    }
            }

            foreach (var light in fx.GetComponentsInChildren<Light>(true))
                light.color = Tint(light.color, h, s);

            // Most VFX colour lives in the material, not the particle colour.
            block ??= new MaterialPropertyBlock();
            foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null) continue;
                    r.GetPropertyBlock(block, i);
                    var sh = m.shader;
                    bool any = false;
                    for (int p = 0, n = sh.GetPropertyCount(); p < n; p++)
                    {
                        if (sh.GetPropertyType(p) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                        int id = sh.GetPropertyNameId(p);
                        block.SetColor(id, Tint(m.GetColor(id), h, s));
                        any = true;
                    }
                    if (any) r.SetPropertyBlock(block, i);
                }
            }
        }

        private static Color Tint(Color c, float h, float s)
        {
            Color.RGBToHSV(c, out _, out float cs, out float cv);
            var r = Color.HSVToRGB(h, Mathf.Min(cs, s), cv, true);
            r.a = c.a;
            return r;
        }

        private static Gradient Tint(Gradient g, float h, float s)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Tint(keys[i].color, h, s);
            var t = new Gradient { mode = g.mode };
            t.SetKeys(keys, g.alphaKeys);
            return t;
        }

        private static ParticleSystem.MinMaxGradient Tint(ParticleSystem.MinMaxGradient m, float h, float s)
        {
            switch (m.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Tint(m.color, h, s));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Tint(m.colorMin, h, s), Tint(m.colorMax, h, s));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Tint(m.gradient, h, s));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Tint(m.gradientMin, h, s), Tint(m.gradientMax, h, s));
                default:
                    // RandomColor picks from the gradient.
                    var r = new ParticleSystem.MinMaxGradient(Tint(m.gradient, h, s)) { mode = ParticleSystemGradientMode.RandomColor };
                    return r;
            }
        }
    }
}
