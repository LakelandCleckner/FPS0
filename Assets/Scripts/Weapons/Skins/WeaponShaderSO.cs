using System;
using System.Collections.Generic;
using UnityEngine;

namespace Combat.Weapons
{
    // Colour preset for zone-masked guns; works on any gun whose mask uses the same zone ids.
    [CreateAssetMenu(fileName = "WeaponShader", menuName = "Combat/Weapons/Weapon Shader")]
    public class WeaponShaderSO : ScriptableObject
    {
        public const string ShaderName = "FPS0/WeaponZones";

        [Serializable]
        public class ZoneLook
        {
            public int zoneId = 1;
            public Color color = Color.white;
            [Range(0f, 1f)] public float recolor = 1f;
            [Range(0f, 4f)] public float brightness = 1f;
            [Range(0f, 1f)] public float metallic;
            [Range(0f, 1f)] public float smoothness;
            [ColorUsage(false, true)] public Color emission = Color.black;
        }

        [Tooltip("Zone names for the inspector dropdown. Matching is by zone id.")]
        public WeaponZoneSetSO zoneSet;
        public List<ZoneLook> zones = new List<ZoneLook>();

        private static readonly int ColorArray = Shader.PropertyToID("_ZoneColor");
        private static readonly int SurfaceArray = Shader.PropertyToID("_ZoneSurface");
        private static readonly int EmissionArray = Shader.PropertyToID("_ZoneEmission");
        private static readonly Vector4[] colorBuf = new Vector4[WeaponZoneSetSO.MaxZones];
        private static readonly Vector4[] surfaceBuf = new Vector4[WeaponZoneSetSO.MaxZones];
        private static readonly Vector4[] emissionBuf = new Vector4[WeaponZoneSetSO.MaxZones];

        // Applies this preset to every zone-shader renderer under the gun.
        public void ApplyTo(GameObject gun)
        {
            if (gun == null) return;
            var block = new MaterialPropertyBlock();
            WriteTo(block);
            foreach (var r in gun.GetComponentsInChildren<Renderer>(true))
                if (UsesZoneShader(r)) r.SetPropertyBlock(block);
        }

        public void WriteTo(MaterialPropertyBlock block) => WriteZones(block, zones);

        // Packs looks into the shader's per-id arrays; unlisted ids keep the original look.
        public static void WriteZones(MaterialPropertyBlock block, IList<ZoneLook> looks)
        {
            Array.Clear(colorBuf, 0, colorBuf.Length);
            Array.Clear(surfaceBuf, 0, surfaceBuf.Length);
            Array.Clear(emissionBuf, 0, emissionBuf.Length);

            if (looks != null)
            {
                foreach (var look in looks)
                {
                    if (look == null || look.zoneId < 1 || look.zoneId > WeaponZoneSetSO.MaxZones) continue;
                    int i = look.zoneId - 1;
                    var c = ToShaderSpace(look.color);
                    var e = ToShaderSpace(look.emission);
                    colorBuf[i] = new Vector4(c.r, c.g, c.b, look.recolor);
                    surfaceBuf[i] = new Vector4(look.metallic, look.smoothness, look.brightness, 1f);
                    emissionBuf[i] = new Vector4(e.r, e.g, e.b, 0f);
                }
            }

            block.SetVectorArray(ColorArray, colorBuf);
            block.SetVectorArray(SurfaceArray, surfaceBuf);
            block.SetVectorArray(EmissionArray, emissionBuf);
        }

        public static bool UsesZoneShader(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name == ShaderName) return true;
            return false;
        }

        // Vector arrays skip Unity's colour conversion, so convert here.
        private static Color ToShaderSpace(Color c) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
    }
}
