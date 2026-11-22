using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Combat.Core;

namespace Combat.Enemies.EditorTools
{
    // Shield shells and hits from the PixPlays shields, URP materials, Shock/Ballistic shields and shield layers. Safe to run again.
    public static class ShieldVfxSetup
    {
        private const int Version = 3;
        private const string RimShader = "FPS0/ShieldRim";
        private const string PixDir = "Assets/PixPlays/ElementalShields/";
        private const string SpikePath = "Assets/PixPlays/Components/Meshes/Objects/spike_mesh.prefab";
        private const string OutDir = "Assets/Prefabs/VFX/Shields";
        private const string MatDir = "Assets/Art/VFX/Shields";
        private const string ModDir = "Assets/GameData/Enemies/Modifiers";
        private const string Tag = "[ShieldVfxSetup] ";

        private class Base
        {
            public string Name, Shell, Hit;
            public bool Uniform, Crystal, Rim, IceHit;
            public float PartSize = 1f;
            public Color RimColor;
        }

        private const string EarthShell = "EarthShield/Version_BuiltIn/EarthShield.prefab";
        private const string EarthHit = "EarthShield/Version_BuiltIn/EarthShieldHit.prefab";

        private static readonly Base Earth = new Base { Name = "Earth", Shell = EarthShell, Hit = EarthHit, Uniform = true, PartSize = 0.5f, Rim = true, RimColor = new Color(1.4f, 0.85f, 0.35f, 1f) };
        private static readonly Base Ice = new Base { Name = "Ice", Shell = EarthShell, Hit = EarthHit, Uniform = true, Crystal = true, IceHit = true, PartSize = 0.1f, Rim = true, RimColor = new Color(0.75f, 1.2f, 1.5f, 1f) };
        private static readonly Base Fire = new Base { Name = "Fire", Shell = "FireShield/Version_BuiltIn/FireShield.prefab", Hit = "FireShield/Version_BuiltIn/FireShieldHit.prefab" };
        private static readonly Base Water = new Base { Name = "Water", Shell = "WaterShield/Version_BuiltIn/WaterShield.prefab", Hit = "WaterShield/Version_BuiltIn/WaterShieldlHit.prefab" };
        private static readonly Base Wind = new Base { Name = "Wind", Shell = "WindShield/Version_BuiltIn/WindShield.prefab", Hit = "WindShield/Version_BuiltIn/WindShieldHit.prefab" };

        private class Entry
        {
            public string ElementId, Label, ShieldAsset;
            public Base Base;
            public bool Tint, Colorize;
            public Base Replaces;   // an older default this entry takes over from
        }

        private static readonly Entry[] Entries =
        {
            new Entry { ElementId = "physical", Label = "Ballistic", ShieldAsset = "BallisticShield", Base = Earth, Tint = false },
            new Entry { ElementId = "fire", Label = "Fire", ShieldAsset = "FireShield", Base = Fire, Tint = true },
            new Entry { ElementId = "ice", Label = "Frost", ShieldAsset = "IceShield", Base = Ice, Tint = true, Replaces = Water },
            new Entry { ElementId = "poison", Label = "Toxic", ShieldAsset = "PoisonShield", Base = Water, Tint = true },
            new Entry { ElementId = "lightning", Label = "Shock", ShieldAsset = "ShockShield", Base = Wind, Tint = true, Colorize = true },
        };

        [MenuItem("FPS0/Art/Build Shield VFX", priority = 522)]
        public static void Run()
        {
            Folder(OutDir);
            Folder(MatDir);
            var types = DamageTypes();
            var shells = new Dictionary<Base, GameObject>();
            var hits = new Dictionary<Base, GameObject>();

            foreach (var e in Entries)
            {
                if (!shells.ContainsKey(e.Base)) shells[e.Base] = Build(e.Base, true);
                if (!hits.ContainsKey(e.Base)) hits[e.Base] = Build(e.Base, false);

                types.TryGetValue(e.ElementId, out var element);
                if (element == null) { Debug.LogWarning(Tag + "No damage type with id '" + e.ElementId + "'"); continue; }

                var shield = ShieldAsset(e, element);
                if (shield == null) continue;
                Wire(shield, e, shells[e.Base], hits[e.Base]);
                ShieldLayer(e, shield);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "Done.");
        }

        // ------------------------------------------------------------------ prefabs

        private static string OutPath(Base b, bool shell) => OutDir + "/" + (shell ? "ShieldShell_" : "ShieldHit_") + b.Name + ".prefab";

        // New copy of a PixPlays prefab, or an upgrade of one built earlier.
        private static GameObject Build(Base b, bool shell)
        {
            if (!shell && b.IceHit) return IceHit(OutPath(b, false));
            string outPath = OutPath(b, shell);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
            if (existing != null)
            {
                var info = existing.GetComponent<ShieldShell>();
                bool stale = shell ? info == null || info.setupVersion < Version : HasBuiltIn(existing);
                if (!stale) return existing;
                var root = PrefabUtility.LoadPrefabContents(outPath);
                Finish(root, b, shell);
                PrefabUtility.SaveAsPrefabAsset(root, outPath);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log(Tag + "Updated " + outPath);
                return AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
            }

            string src = PixDir + (shell ? b.Shell : b.Hit);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (prefab == null) { Debug.LogWarning(Tag + "Missing " + src); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            int slash = outPath.LastIndexOf('/') + 1;
            go.name = outPath.Substring(slash, outPath.Length - slash - ".prefab".Length);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("PixPlays")) Object.DestroyImmediate(mb);
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            Finish(go, b, shell);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, outPath);
            Object.DestroyImmediate(go);
            Debug.Log(Tag + "Created " + outPath);
            return saved;
        }

        private static void Finish(GameObject go, Base b, bool shell)
        {
            ConvertMaterials(go);
            if (!shell) return;

            // The shell's own spawn animation scales and dissolves it, which fights the fade-in.
            foreach (var a in go.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = true;
            }

            var parts = new List<Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("shard")) { t.gameObject.SetActive(true); parts.Add(t); }
            if (b.Crystal) Crystalize(parts);

            var info = go.GetComponent<ShieldShell>();
            bool fresh = info == null || info.setupVersion < 2;
            if (info == null) info = go.AddComponent<ShieldShell>();
            if (parts.Count > 0) MeasureOrbit(parts, info);
            else Measure(go, info);
            info.uniformFit = b.Uniform;
            info.fixedParts = parts.ToArray();
            if (fresh) info.partSize = b.PartSize;
            if (b.Rim) AddRim(go, info, b);
            info.outer = Find(go.transform, "RotationOuter");
            info.inner = Find(go.transform, "RotationInner");
            info.setupVersion = Version;
        }

        // Size and height from the shell's meshes when it has any, else from its bubble emitters; always centred horizontally.
        private static void Measure(GameObject go, ShieldShell info)
        {
            bool any = false;
            Bounds b = default;
            void Add(Bounds x) { if (!any) { b = x; any = true; } else b.Encapsulate(x); }

            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (!IsGround(r.transform)) Add(r.bounds);

            if (!any)
            {
                var systems = new List<ParticleSystem>();
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    if (!IsGround(ps.transform) && (ps.name.Contains("Side") || ps.name.Contains("Shield"))) systems.Add(ps);
                if (systems.Count == 0)
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                        if (!IsGround(ps.transform)) systems.Add(ps);

                foreach (var ps in systems)
                {
                    var tr = ps.transform;
                    float scale = Mathf.Max(tr.lossyScale.x, tr.lossyScale.y, tr.lossyScale.z);
                    float size = ps.main.startSize.constantMax;
                    var pr = ps.GetComponent<ParticleSystemRenderer>();
                    float r = pr != null && pr.renderMode == ParticleSystemRenderMode.Mesh && pr.mesh != null
                        ? pr.mesh.bounds.extents.magnitude / 1.732f * size * scale
                        : size * 0.5f * scale;
                    var shape = ps.shape;
                    if (shape.enabled) r = Mathf.Max(r, shape.radius * scale);
                    Add(new Bounds(tr.position, Vector3.one * r * 2f));
                }
            }

            if (!any) { info.baseRadius = 1f; info.centerOffset = Vector3.zero; return; }
            info.centerOffset = new Vector3(0f, b.center.y, 0f);
            info.baseRadius = Mathf.Max(0.05f, Mathf.Max(b.extents.x, b.extents.y, b.extents.z));
            Debug.Log(Tag + go.name + ": radius " + info.baseRadius.ToString("F2") + ", height " + b.center.y.ToString("F2") + " (tune on ShieldShell)");
        }

        // Rim-glow sphere at the shell centre, sized to the shell.
        private static void AddRim(GameObject go, ShieldShell info, Base b)
        {
            var t = Find(go.transform, "Rim");
            if (t == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
                t = new GameObject("Rim").transform;
                t.SetParent(go.transform, false);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = t.gameObject.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var mr = t.GetComponent<MeshRenderer>();
            mr.sharedMaterial = RimMaterial(b);
            t.localPosition = info.centerOffset;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * info.baseRadius * 2f;
            info.rim = mr;
        }

        private static Material RimMaterial(Base b)
        {
            string path = MatDir + "/ShieldRim_" + b.Name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find(RimShader);
            if (shader == null) { Debug.LogWarning(Tag + "Shader " + RimShader + " not found"); return null; }
            m = new Material(shader);
            m.SetColor("_Color", b.RimColor);
            AssetDatabase.CreateAsset(m, path);
            Debug.Log(Tag + "Created " + path);
            return m;
        }

        // Orbit shells: radius from where the parts sit, not their size, so big meshes don't inflate it.
        private static void MeasureOrbit(List<Transform> parts, ShieldShell info)
        {
            float y = 0f;
            foreach (var t in parts) y += t.position.y;
            y /= parts.Count;
            var centre = new Vector3(0f, y, 0f);
            float r = 0f;
            foreach (var t in parts) r = Mathf.Max(r, Vector3.Distance(t.position, centre));
            info.centerOffset = centre;
            info.baseRadius = Mathf.Max(0.05f, r + 0.15f);
            Debug.Log(Tag + info.name + ": orbit radius " + info.baseRadius.ToString("F2") + ", height " + y.ToString("F2") + " (tune on ShieldShell)");
        }

        private static bool IsGround(Transform t) => t.name.Contains("Ground");

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // Ice hit of our own: spike shards and a frosty puff. Rebuilds an old rock copy in place, keeping its GUID and references.
        private static GameObject IceHit(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && Find(existing.transform, "IceShards") != null) return existing;

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            var shardsGo = new GameObject("IceShards");
            shardsGo.transform.SetParent(root.transform, false);
            var shards = HitBurst(shardsGo, IceMaterial(), 8);
            var main = shards.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.gravityModifier = 1f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var sol = shards.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
            var spikeGo = AssetDatabase.LoadAssetAtPath<GameObject>(SpikePath);
            var spike = spikeGo != null ? spikeGo.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            if (spike != null)
            {
                var r = shards.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = spike;
            }

            var frostGo = new GameObject("Frost");
            frostGo.transform.SetParent(root.transform, false);
            var mist = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/VFX/Status/Particle_Mist.mat");
            var frost = HitBurst(frostGo, mist, 6);
            main = frost.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startColor = new Color(0.8f, 0.95f, 1f, 0.5f);
            var col = frost.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            sol = frost.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1.8f));

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log(Tag + (existing != null ? "Rebuilt " : "Created ") + path);
            return saved;
        }

        // One-shot burst in a cone off the surface; sizes are authored for the default 0.5 hit scale.
        private static ParticleSystem HitBurst(GameObject go, Material mat, int count)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.05f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // Rocks become ice spikes pointing out from the centre.
        private static void Crystalize(List<Transform> parts)
        {
            var spikeGo = AssetDatabase.LoadAssetAtPath<GameObject>(SpikePath);
            var spike = spikeGo != null ? spikeGo.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            if (spike == null) { Debug.LogWarning(Tag + "No spike mesh at " + SpikePath + "; ice keeps the rock shapes."); }
            var mat = IceMaterial();

            Vector3 axis = Vector3.up;
            if (spike != null)
            {
                var e = spike.bounds.extents;
                axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.z >= e.y ? Vector3.forward : Vector3.up;
            }

            foreach (var t in parts)
            {
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<MeshRenderer>();
                if (mf != null && spike != null) mf.sharedMesh = spike;
                if (mr != null) mr.sharedMaterial = mat;
                Vector3 dir = t.localPosition.sqrMagnitude > 1e-6f ? t.localPosition.normalized : Vector3.up;
                t.localRotation = Quaternion.FromToRotation(axis, dir);
            }
        }

        private static Material IceMaterial()
        {
            string path = MatDir + "/IceCrystal.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Set(m, "_Surface", 1f);
            Set(m, "_Blend", 0f);
            Set(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Set(m, "_ZWrite", 1f);
            Set(m, "_Smoothness", 0.95f);
            Set(m, "_Metallic", 0.1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.72f, 0.9f, 1f, 0.6f));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.15f, 0.45f, 0.7f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(m, path);
            Debug.Log(Tag + "Created " + path);
            return m;
        }

        // ------------------------------------------------------------------ materials

        private static bool NeedsUrp(Material m)
        {
            if (m == null) return false;
            var sh = m.shader;
            if (sh == null || !sh.isSupported) return true;
            string n = sh.name;
            return n == "Standard" || n.StartsWith("Standard (") || n.StartsWith("Legacy Shaders/") || n == "Hidden/InternalErrorShader"
                || n.StartsWith("Particles/") || n.StartsWith("Mobile/");
        }

        private static bool HasBuiltIn(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (NeedsUrp(m)) return true;
            return false;
        }

        private static void ConvertMaterials(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!NeedsUrp(mats[i])) continue;
                    mats[i] = UrpCopy(mats[i], r is ParticleSystemRenderer);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // URP copy with the texture, colour, normal map and smoothness carried over.
        private static Material UrpCopy(Material src, bool particle)
        {
            string path = MatDir + "/" + src.name + "_URP.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find(particle ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit"));
            if (src.HasProperty("_MainTex") && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
            if (src.HasProperty("_Color") && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_Color"));
            if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
                m.EnableKeyword("_NORMALMAP");
            }
            if (src.HasProperty("_Glossiness")) Set(m, "_Smoothness", src.GetFloat("_Glossiness"));
            if (src.HasProperty("_Metallic")) Set(m, "_Metallic", src.GetFloat("_Metallic"));
            AssetDatabase.CreateAsset(m, path);
            Debug.Log(Tag + "URP copy " + path);
            return m;
        }

        // ------------------------------------------------------------------ data

        private static Dictionary<string, DamageTypeSO> DamageTypes()
        {
            var map = new Dictionary<string, DamageTypeSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:DamageTypeSO"))
            {
                var t = AssetDatabase.LoadAssetAtPath<DamageTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null && !string.IsNullOrEmpty(t.id)) map[t.id] = t;
            }
            return map;
        }

        // The element's shield, created from the Fire shield's numbers (no break effect) when missing.
        private static EnemyShieldSO ShieldAsset(Entry e, DamageTypeSO element)
        {
            string path = ModDir + "/" + e.ShieldAsset + ".asset";
            var so = AssetDatabase.LoadAssetAtPath<EnemyShieldSO>(path);
            if (so != null) return so;

            var template = AssetDatabase.LoadAssetAtPath<EnemyShieldSO>(ModDir + "/FireShield.asset");
            so = template != null ? Object.Instantiate(template) : ScriptableObject.CreateInstance<EnemyShieldSO>();
            so.element = element;
            so.breakEffect = null;
            so.shellVfx = null;
            so.hitVfx = null;
            AssetDatabase.CreateAsset(so, path);
            Debug.Log(Tag + "Created " + path);
            return so;
        }

        // Fills empty slots, and takes over slots still holding an older default.
        private static void Wire(EnemyShieldSO so, Entry e, GameObject shell, GameObject hit)
        {
            var oldShell = e.Replaces != null ? AssetDatabase.LoadAssetAtPath<GameObject>(OutPath(e.Replaces, true)) : null;
            var oldHit = e.Replaces != null ? AssetDatabase.LoadAssetAtPath<GameObject>(OutPath(e.Replaces, false)) : null;
            bool changed = false;
            if (shell != null && (so.shellVfx == null || (oldShell != null && so.shellVfx == oldShell)))
            {
                so.shellVfx = shell;
                so.tintVfx = e.Tint;
                so.colorizeVfx = e.Colorize;
                changed = true;
            }
            if (hit != null && (so.hitVfx == null || (oldHit != null && so.hitVfx == oldHit))) { so.hitVfx = hit; changed = true; }
            if (!changed) return;
            EditorUtility.SetDirty(so);
            Debug.Log(Tag + "VFX on " + so.name);
        }

        // A layer that only adds the shield: no look, behaviour, name or stats, so it fits on any enemy.
        private static void ShieldLayer(Entry e, EnemyShieldSO shield)
        {
            string path = ModDir + "/Shield_" + e.Label + ".asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyModifierSO>(path) != null) return;
            var m = ScriptableObject.CreateInstance<EnemyModifierSO>();
            m.displayName = e.Label + " Shield";
            m.namePrefix = "";
            m.kind = EnemyModifierKind.Other;
            m.shield = shield;
            AssetDatabase.CreateAsset(m, path);
            Debug.Log(Tag + "Created " + path);
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
