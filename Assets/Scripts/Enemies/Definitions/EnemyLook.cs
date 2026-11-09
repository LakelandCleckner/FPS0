using System.Collections.Generic;
using UnityEngine;
using Combat.Zones;

namespace Combat.Enemies
{
    // The combined look of an enemy's modifier layers. Shared by the runtime applier and the editor tools.
    public class EnemyLook
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public readonly List<ZoneShaderSO.ZoneLook> Zones = new List<ZoneShaderSO.ZoneLook>();
        public Color Emission = Color.black;
        public float Scale = 1f;

        public static readonly EnemyLook Plain = new EnemyLook();

        public bool IsPlain => Zones.Count == 0 && Emission == Color.black;

        // Per zone: the last layer whose preset sets it wins. Emission adds, scale multiplies.
        public static EnemyLook Resolve(IReadOnlyList<EnemyModifierSO> layers)
        {
            var look = new EnemyLook();
            if (layers == null) return look;
            for (int i = 0; i < layers.Count; i++)
            {
                var m = layers[i];
                if (m == null) continue;
                if (m.look != null)
                    foreach (var z in m.look.zones)
                    {
                        if (z == null) continue;
                        look.Zones.RemoveAll(e => e.zoneId == z.zoneId);
                        look.Zones.Add(z);
                    }
                look.Emission += m.emission;
                look.Scale *= m.scale;
            }
            return look;
        }

        // Writes per-material overrides on FPS0/EnemyLit materials; a plain look clears them so renderers stay SRP Batcher compatible.
        public void ApplyTo(Renderer[] renderers, MaterialPropertyBlock block)
        {
            bool plain = IsPlain;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (plain) { r.SetPropertyBlock(null, i); continue; }
                    block.Clear();
                    var mat = mats[i];
                    if (mat != null)
                    {
                        if (Zones.Count > 0 && mat.shader != null && mat.shader.name == ZoneShaderSO.EnemyShaderName)
                            ZoneShaderSO.WriteZones(block, Zones);
                        if (Emission != Color.black && mat.HasProperty(EmissionId)) block.SetColor(EmissionId, Emission);
                    }
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        // Changes when anything that affects the rendered look changes.
        public int Signature()
        {
            unchecked
            {
                int h = Emission.GetHashCode() * 31 + Scale.GetHashCode();
                foreach (var z in Zones)
                    h = h * 31 + z.zoneId * 7 + z.color.GetHashCode() + z.recolor.GetHashCode() * 3
                        + z.brightness.GetHashCode() * 5 + z.metallic.GetHashCode() * 11
                        + z.smoothness.GetHashCode() * 13 + z.emission.GetHashCode() * 17;
                return h;
            }
        }

        // Renderers that take the look (mesh and skinned mesh only).
        public static Renderer[] FindRenderers(GameObject root)
        {
            var all = root.GetComponentsInChildren<Renderer>(true);
            var list = new List<Renderer>(all.Length);
            foreach (var r in all)
                if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
            return list.ToArray();
        }
    }
}
