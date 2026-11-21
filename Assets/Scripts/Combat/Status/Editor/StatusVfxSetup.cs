using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Combat.Status.EditorTools
{
    // Builds each status's overlay material and particle prefabs and wires them up. Safe to run again: keeps tuned assets, only replaces empty or PixPlays defaults.
    public static class StatusVfxSetup
    {
        private const string MatDir = "Assets/Art/VFX/Status";
        private const string PrefabDir = "Assets/Prefabs/VFX/Status";
        private const string OverlayShader = "FPS0/StatusOverlay";
        private const string SpikePath = "Assets/PixPlays/Components/Meshes/Objects/spike_mesh.prefab";
        private const string IceMatPath = "Assets/Art/VFX/Shields/IceCrystal.mat";
        private const string Tag = "[StatusVfxSetup] ";

        [MenuItem("FPS0/Art/Build Status VFX", priority = 523)]
        public static void Run()
        {
            Folder(MatDir);
            Folder(PrefabDir);
            var dot = SoftDot();

            // ---- Frost ----
            var chillMat = Overlay("Chill", 1, m =>
            {
                m.SetColor("_Color", new Color(0.72f, 0.9f, 1f, 0.85f));
                m.SetColor("_EdgeColor", new Color(1.6f, 1.9f, 2.2f, 1f));
                m.SetFloat("_Fill", 0.45f);
                m.SetFloat("_RimStrength", 1.2f);
                m.SetFloat("_Sparkle", 1f);
                m.SetFloat("_CreepMin", 0.15f);
                m.SetFloat("_CreepMax", 0.85f);
                m.SetFloat("_CreepEdge", 0.12f);
                m.SetFloat("_NoiseScale", 14f);
                m.SetFloat("_NoiseSpeed", 0.05f);
            });
            var freezeMat = Overlay("Freeze", 1, m =>
            {
                m.SetColor("_Color", new Color(0.8f, 0.95f, 1.15f, 0.95f));
                m.SetColor("_EdgeColor", new Color(1.8f, 2.1f, 2.4f, 1f));
                m.SetFloat("_Fill", 0.75f);
                m.SetFloat("_RimStrength", 2f);
                m.SetFloat("_Sparkle", 1.5f);
                m.SetFloat("_CreepMin", 1.1f);
                m.SetFloat("_CreepMax", 1.1f);
                m.SetFloat("_NoiseScale", 10f);
                m.SetFloat("_NoiseSpeed", 0f);
            });
            var mist = Mist(dot);
            var shatter = Shatter(dot);

            Wire("ChillStatus", s =>
            {
                Set(ref s.overlayMaterial, chillMat);
                s.overlayScalesWithStacks = true;
                if (Replaceable(s.auraVfx)) { s.auraVfx = mist; s.auraScale = 1f; s.auraOffset = Vector3.zero; s.tintAura = false; }
                s.auraScalesWithStacks = true;
                s.auraEmitFrom = AuraEmitFrom.Skin;
            });
            Wire("FreezeStatus", s =>
            {
                Set(ref s.overlayMaterial, freezeMat);
                s.overlayScalesWithStacks = false;
                // Offset places the end shatter at the chest; the mist comes off the skin either way.
                if (Replaceable(s.auraVfx)) { s.auraVfx = mist; s.auraScale = 1f; s.auraOffset = Vector3.up; s.tintAura = false; }
                s.auraScalesWithStacks = false;
                s.auraEmitFrom = AuraEmitFrom.Skin;
                if (Replaceable(s.endVfx)) { s.endVfx = shatter; s.endVfxLifetime = 1.5f; s.colorizeAura = false; }
            });

            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "Done.");
        }

        // ------------------------------------------------------------------ overlays

        private static Material Overlay(string name, int mode, System.Action<Material> setup)
        {
            string path = MatDir + "/StatusOverlay_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find(OverlayShader);
            if (shader == null) { Debug.LogWarning(Tag + "Shader " + OverlayShader + " not found"); return null; }
            m = new Material(shader);
            m.SetFloat("_Mode", mode);
            m.DisableKeyword("_MODE_RIM");
            m.DisableKeyword("_MODE_CREEP");
            m.DisableKeyword("_MODE_CRACK");
            m.EnableKeyword(mode == 1 ? "_MODE_CREEP" : mode == 2 ? "_MODE_CRACK" : "_MODE_RIM");
            setup(m);
            AssetDatabase.CreateAsset(m, path);
            Debug.Log(Tag + "Created " + path);
            return m;
        }

        // ------------------------------------------------------------------ particles

        // Cold mist sinking off the body.
        private static GameObject Mist(Texture2D dot)
        {
            string path = PrefabDir + "/ChillMist.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("ChillMist");
            var ps = Emitter(root, AlphaMaterial("Mist", dot), 14f, 120);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.gravityModifier = 0.08f;
            main.startColor = new Color(0.8f, 0.92f, 1f, 1f);
            Fade(ps, 0.35f);
            Grow(ps, 1.6f);
            return Save(root, path);
        }

        // Ice shards bursting off when Freeze ends.
        private static GameObject Shatter(Texture2D dot)
        {
            string path = PrefabDir + "/FreezeShatter.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("FreezeShatter");
            var spikeGo = AssetDatabase.LoadAssetAtPath<GameObject>(SpikePath);
            var spike = spikeGo != null ? spikeGo.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            var ice = AssetDatabase.LoadAssetAtPath<Material>(IceMatPath);

            var shards = Burst(root, ice != null ? ice : AlphaMaterial("Mist", dot), 24);
            var main = shards.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.gravityModifier = 1f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = shards.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            if (spike != null)
            {
                var r = shards.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = spike;
            }

            var puffGo = new GameObject("Puff");
            puffGo.transform.SetParent(root.transform, false);
            var puff = Burst(puffGo, AlphaMaterial("Mist", dot), 10);
            main = puff.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startColor = new Color(0.85f, 0.95f, 1f, 1f);
            shape = puff.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            Fade(puff, 0.6f);
            Grow(puff, 1.8f);
            return Save(root, path);
        }

        private static ParticleSystem Emitter(GameObject go, Material mat, float rate, int max)
        {
            var ps = Base(go, mat, max);
            var main = ps.main;
            main.loop = true;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            return ps;
        }

        private static ParticleSystem Burst(GameObject go, Material mat, int count)
        {
            var ps = Base(go, mat, count * 2);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            return ps;
        }

        private static ParticleSystem Base(GameObject go, Material mat, int max)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var main = ps.main;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = max;
            return ps;
        }

        private static void Fade(ParticleSystem ps, float alpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        private static void Grow(ParticleSystem ps, float to)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, to));
        }

        private static GameObject Save(GameObject root, string path)
        {
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log(Tag + "Created " + path);
            return saved;
        }

        // ------------------------------------------------------------------ shared assets

        private static Texture2D SoftDot()
        {
            string path = MatDir + "/SoftDot.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            const int n = 32;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    px[y * n + x] = new Color(1f, 1f, 1f, (1f - d) * (1f - d));
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        // Alpha-blended unlit particle material with the soft dot.
        private static Material AlphaMaterial(string name, Texture2D tex)
        {
            string path = MatDir + "/Particle_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            SetF(m, "_Surface", 1f);
            SetF(m, "_Blend", 0f);
            SetF(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetF(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetF(m, "_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ wiring

        private static void Wire(string assetName, System.Action<StatusSO> setup)
        {
            var status = AssetDatabase.FindAssets(assetName)
                .Select(g => AssetDatabase.LoadAssetAtPath<StatusSO>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(s => s != null && s.name == assetName);
            if (status == null) { Debug.LogWarning(Tag + "No status named " + assetName); return; }
            setup(status);
            EditorUtility.SetDirty(status);
            Debug.Log(Tag + "Wired " + assetName);
        }

        // Empty, or still a PixPlays placeholder.
        private static bool Replaceable(Object o) => o == null || AssetDatabase.GetAssetPath(o).StartsWith("Assets/PixPlays");

        private static void Set(ref Material field, Material value)
        {
            if (field == null && value != null) field = value;
        }

        private static void SetF(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            Folder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
