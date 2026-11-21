using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Combat.Status.EditorTools
{
    // One-click Fracture grit aura. Safe to run again: skips anything already in place.
    public static class StatusAuraSetup
    {
        private const string ArtDir = "Assets/Art/VFX";
        private const string PrefabDir = "Assets/Prefabs/VFX";
        private const string DustTexPath = ArtDir + "/GritDust.asset";
        private const string ChipMatPath = ArtDir + "/GritChip.mat";
        private const string DustMatPath = ArtDir + "/GritDust.mat";
        private const string PrefabPath = PrefabDir + "/FractureGrit.prefab";
        private const string ChipMeshPath = "Assets/Imports/PSX Weapons Assets Pack/RetroWeaponsPack/FX/Meshes/Wallhit_Debris.fbx";
        private const string FracturePath = "Assets/Scripts/Combat/Status/StatusSOFiles/StatusSOs/Ballistic/FractureStatus.asset";
        private const string Tag = "[StatusAuraSetup] ";

        private static readonly Color Stone = new Color(0.7f, 0.65f, 0.58f, 1f);
        private static readonly Color Dust = new Color(0.75f, 0.7f, 0.62f, 1f);

        [MenuItem("FPS0/Art/Build Fracture Grit Aura", priority = 520)]
        public static void Run()
        {
            Folder(ArtDir);
            Folder(PrefabDir);

            MakeReadable(ChipMeshPath);
            var chipMesh = AssetDatabase.LoadAllAssetsAtPath(ChipMeshPath).OfType<Mesh>().FirstOrDefault();
            if (chipMesh == null) Debug.LogWarning(Tag + "No mesh in " + ChipMeshPath + "; chips use billboards.");

            var prefab = GritPrefab(ChipMaterial(), DustMaterial(DustTexture()), chipMesh);
            WireFracture(prefab);
            BodySurfaceBake.Run();

            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            Debug.Log(Tag + "Done.");
        }

        // ------------------------------------------------------------------ assets

        private static Texture2D DustTexture()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DustTexPath);
            if (tex != null) return tex;

            const int n = 32;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "GritDust", wrapMode = TextureWrapMode.Clamp };
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
            AssetDatabase.CreateAsset(tex, DustTexPath);
            Debug.Log(Tag + "Created " + DustTexPath);
            return tex;
        }

        private static Material ChipMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(ChipMatPath);
            if (m != null) return m;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(m, ChipMatPath);
            Debug.Log(Tag + "Created " + ChipMatPath);
            return m;
        }

        // Alpha-blended soft dot.
        private static Material DustMaterial(Texture2D tex)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(DustMatPath);
            if (m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            Set(m, "_Surface", 1f);
            Set(m, "_Blend", 0f);
            Set(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Set(m, "_SrcBlendAlpha", (float)BlendMode.One);
            Set(m, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            Set(m, "_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            AssetDatabase.CreateAsset(m, DustMatPath);
            Debug.Log(Tag + "Created " + DustMatPath);
            return m;
        }

        private static GameObject GritPrefab(Material chipMat, Material dustMat, Mesh chipMesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            var root = new GameObject("FractureGrit");

            // Stone chips that drop off the body
            var chips = Emitter(root, chipMat, 25f, 100);
            var main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.gravityModifier = 1f;
            main.startColor = Stone;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = chips.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var sol = chips.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.5f));
            if (chipMesh != null)
            {
                var r = chips.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = chipMesh;
            }

            // Fine dust that hangs and fades
            var dustGo = new GameObject("Dust");
            dustGo.transform.SetParent(root.transform, false);
            var dust = Emitter(dustGo, dustMat, 12f, 80);
            main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.gravityModifier = 0.15f;
            main.startColor = Dust;
            var col = dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            sol = dust.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1.5f));

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log(Tag + "Created " + PrefabPath);
            return saved;
        }

        // Looping world-space emitter; sphere shape is the fallback when the target has no hit hulls.
        private static ParticleSystem Emitter(GameObject go, Material mat, float rate, int max)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = max;

            var em = ps.emission;
            em.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            return ps;
        }

        // ------------------------------------------------------------------ wiring

        private static void WireFracture(GameObject prefab)
        {
            var status = AssetDatabase.LoadAssetAtPath<StatusSO>(FracturePath);
            if (status == null) { Debug.LogWarning(Tag + "No status at " + FracturePath); return; }
            if (status.auraVfx != null) return;

            status.auraVfx = prefab;
            status.auraScalesWithStacks = true;
            status.auraEmitFrom = AuraEmitFrom.Skin;
            status.auraScale = 1f;
            status.tintAura = false;
            EditorUtility.SetDirty(status);
            Debug.Log(Tag + "Grit aura on " + FracturePath);
        }

        // ------------------------------------------------------------------ helpers

        private static void MakeReadable(string path)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter mi || mi.isReadable) return;
            mi.isReadable = true;
            mi.SaveAndReimport();
            Debug.Log(Tag + "Read/Write on " + path);
        }

        private static void Set(Material m, string prop, float v)
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
