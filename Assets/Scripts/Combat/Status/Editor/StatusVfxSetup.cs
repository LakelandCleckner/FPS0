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
        private const string RockPath = "Assets/PixPlays/Components/Meshes/Objects/rock.prefab";
        private const string RockTexPath = "Assets/PixPlays/Components/Textures/RockTextures/Rock_Diffuse.png";
        private static readonly Color Amber = new Color(1.4f, 0.85f, 0.35f, 1f);
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

            // ---- Shock ----
            var conductive = Find("ConductiveStatus");
            Color shock = conductive != null && conductive.damageType != null ? conductive.damageType.color : new Color(0.55f, 0.75f, 1f);
            var conductiveMat = Overlay("Conductive", 3, m =>
            {
                var c = shock * 1.5f; c.a = 0.7f;
                var e = Color.Lerp(shock, Color.white, 0.5f) * 4f; e.a = 1f;
                m.SetColor("_Color", c);
                m.SetColor("_EdgeColor", e);
                m.SetFloat("_Fill", 0.06f);
                m.SetFloat("_RimPower", 3f);
                m.SetFloat("_RimStrength", 1.4f);
                m.SetFloat("_NoiseScale", 6f);
                m.SetFloat("_NoiseSpeed", 0.3f);
                m.SetFloat("_NoiseStrength", 0.5f);
                m.SetFloat("_Flicker", 0f);
                m.SetFloat("_FlickerRate", 6f);
                m.SetFloat("_CrackScale", 7f);
                m.SetFloat("_CrackWidth", 0.05f);
                m.SetFloat("_ArcRate", 0.9f);
                m.SetFloat("_ArcCoverage", 0.35f);
            });
            var sparks = ConductiveSparks(dot);
            var discharge = Discharge(dot);

            Wire("ConductiveStatus", s =>
            {
                Set(ref s.overlayMaterial, conductiveMat);
                s.overlayScalesWithStacks = false;
                s.fadeOutTime = 1f;
                if (Replaceable(s.auraVfx)) { s.auraVfx = sparks; s.auraScale = 1f; s.auraOffset = Vector3.up; s.tintAura = true; s.colorizeAura = true; }
                s.auraScalesWithStacks = false;
                s.auraEmitFrom = AuraEmitFrom.Skin;
                if (Replaceable(s.endVfx)) { s.endVfx = discharge; s.endVfxLifetime = 1f; }
            });

            // ---- Fire ----
            var burnMat = Overlay("Burn", 4, m =>
            {
                m.SetColor("_Color", new Color(2f, 0.7f, 0.15f, 1f));
                m.SetColor("_EdgeColor", new Color(3f, 2f, 0.8f, 1f));
                m.SetFloat("_Fill", 0.15f);
                m.SetFloat("_RimPower", 1.8f);
                m.SetFloat("_RimStrength", 1.6f);
                m.SetFloat("_NoiseScale", 3f);
                m.SetFloat("_NoiseSpeed", 1.2f);
                m.SetFloat("_NoiseStrength", 0.3f);
                m.SetFloat("_CrackScale", 6f);
                m.SetFloat("_Sparkle", 1.5f);
                m.SetFloat("_PulseSpeed", 1.5f);
                m.SetFloat("_PulseAmount", 0.15f);
            });
            var flameTex = FlameTexture();
            var burnAura = BurnFlames(dot, flameTex);
            var burnOut = BurnOut(dot);

            Wire("FireBurnStatus", s =>
            {
                Set(ref s.overlayMaterial, burnMat);
                s.overlayScalesWithStacks = true;
                s.fadeOutTime = 0.5f;
                if (Replaceable(s.auraVfx)) { s.auraVfx = burnAura; s.auraScale = 1f; s.auraOffset = Vector3.up; s.tintAura = false; }
                s.auraScalesWithStacks = true;
                s.auraEmitFrom = AuraEmitFrom.Skin;
                if (Replaceable(s.endVfx)) { s.endVfx = burnOut; s.endVfxLifetime = 2f; s.colorizeAura = false; }
            });

            // ---- Toxic ----
            var poisonMat = Overlay("Poison", 5, m =>
            {
                m.SetColor("_Color", new Color(0.3f, 1.2f, 0.15f, 1f));
                m.SetColor("_EdgeColor", new Color(1f, 3f, 0.4f, 1f));
                m.SetFloat("_Fill", 0.08f);
                m.SetFloat("_RimPower", 2.5f);
                m.SetFloat("_RimStrength", 1f);
                m.SetFloat("_NoiseScale", 8f);
                m.SetFloat("_NoiseSpeed", 0f);
                m.SetFloat("_NoiseStrength", 0.3f);
                m.SetFloat("_CrackScale", 9f);
                m.SetFloat("_CrackWidth", 0.035f);
                m.SetFloat("_PulseSpeed", 0.6f);
                m.SetFloat("_PulseAmount", 0f);
            });
            var poisonAura = PoisonOoze(dot, BubbleTexture());
            var poisonOut = GasPuff(dot);

            Wire("PoisonBurnStatus", s =>
            {
                Set(ref s.overlayMaterial, poisonMat);
                s.overlayScalesWithStacks = true;
                s.stackCurve = 0.5f;
                s.fadeOutTime = 0.5f;
                if (Replaceable(s.auraVfx)) { s.auraVfx = poisonAura; s.auraScale = 1f; s.auraOffset = Vector3.up; s.tintAura = false; }
                s.auraScalesWithStacks = true;
                s.auraEmitFrom = AuraEmitFrom.Skin;
                if (Replaceable(s.endVfx)) { s.endVfx = poisonOut; s.endVfxLifetime = 2.5f; s.colorizeAura = false; }
            });

            // ---- Ballistic ----
            var fractureMat = Overlay("Fracture", 2, m => CrackLook(m, false));
            var breakMat = Overlay("Break", 2, m => CrackLook(m, true));
            var rockBurst = RockBurst(dot);
            var crumble = BreakCrumble(dot);
            var dustPuff = DustPuff(dot);

            Wire("FractureStatus", s =>
            {
                Set(ref s.overlayMaterial, fractureMat);
                s.overlayScalesWithStacks = true;
                if (Replaceable(s.endVfx)) { s.endVfx = rockBurst; s.endVfxLifetime = 2f; }
                s.endVfxOnConvertOnly = true;
            });
            Wire("BreakStatus", s =>
            {
                Set(ref s.overlayMaterial, breakMat);
                s.overlayScalesWithStacks = false;
                s.fadeOutTime = 0.5f;
                if (Replaceable(s.auraVfx)) { s.auraVfx = crumble; s.auraScale = 1f; s.auraOffset = Vector3.up; s.tintAura = false; s.colorizeAura = false; }
                s.auraScalesWithStacks = false;
                s.auraEmitFrom = AuraEmitFrom.Skin;
                if (Replaceable(s.endVfx)) { s.endVfx = dustPuff; s.endVfxLifetime = 2f; }
            });

            // ---- General ----
            var slowMat = Overlay("Slow", 6, m =>
            {
                m.SetColor("_Color", new Color(0.05f, 0.05f, 0.06f, 1f));
                m.SetColor("_EdgeColor", new Color(0.2f, 0.2f, 0.22f, 1f));
                m.SetFloat("_Fill", 0.12f);
                m.SetFloat("_RimPower", 2f);
                m.SetFloat("_RimStrength", 1.4f);
                m.SetFloat("_NoiseScale", 3f);
                m.SetFloat("_NoiseSpeed", 0.25f);
                m.SetFloat("_NoiseStrength", 0.2f);
                m.SetFloat("_PulseAmount", 0f);
            });
            var drag = SlowDrag(dot, RingTexture());

            Wire("SlowStatus", s =>
            {
                Set(ref s.overlayMaterial, slowMat);
                s.overlayScalesWithStacks = false;
                s.fadeOutTime = 0.5f;
                if (Replaceable(s.auraVfx)) { s.auraVfx = drag; s.auraScale = 1f; s.auraOffset = Vector3.zero; s.tintAura = false; s.colorizeAura = false; }
                s.auraScalesWithStacks = false;
                s.auraEmitFrom = AuraEmitFrom.Skin;
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
            m.DisableKeyword("_MODE_ARC");
            m.DisableKeyword("_MODE_BURN");
            m.DisableKeyword("_MODE_TOXIC");
            string[] modes = { "_MODE_RIM", "_MODE_CREEP", "_MODE_CRACK", "_MODE_ARC", "_MODE_BURN", "_MODE_TOXIC" };
            m.EnableKeyword(modes[Mathf.Clamp(mode, 0, modes.Length - 1)]);
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

        // Sparks off the skin plus crawling skin arcs; tinted to the element on spawn.
        private static GameObject ConductiveSparks(Texture2D dot)
        {
            string path = PrefabDir + "/ConductiveSparks.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("ConductiveSparks");
            var ps = Emitter(root, AdditiveMaterial("Spark", dot), 6f, 300);
            SparkLook(ps, 0.06f, 0.15f, 3f, 6f, 0.008f, 0.016f, 0.2f);
            root.AddComponent<StatusAura>();
            var arcs = root.AddComponent<SkinArcs>();
            arcs.sparks = ps;
            return Save(root, path);
        }

        // Spark burst and a quick flash when Conductive runs out.
        private static GameObject Discharge(Texture2D dot)
        {
            string path = PrefabDir + "/ConductiveDischarge.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("ConductiveDischarge");
            var add = AdditiveMaterial("Spark", dot);
            var burst = Burst(root, add, 40);
            SparkLook(burst, 0.1f, 0.25f, 4f, 9f, 0.01f, 0.02f, 0.3f);
            var shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(root.transform, false);
            var flash = Burst(flashGo, add, 1);
            var main = flash.main;
            main.startLifetime = 0.12f;
            main.startSpeed = 0f;
            main.startSize = 1.2f;
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            var fshape = flash.shape;
            fshape.enabled = false;
            Fade(flash, 1f);
            Grow(flash, 1.5f);
            return Save(root, path);
        }

        // Fast, short-lived thin streaks with little fall.
        private static void SparkLook(ParticleSystem ps, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = gravity;
            main.startColor = Color.white;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1f;
        }

        // Flame licks, embers and smoke off the skin; each follows the stack share on its own curve.
        private static GameObject BurnFlames(Texture2D dot, Texture2D flame)
        {
            string path = PrefabDir + "/BurnFlames.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("BurnFlames");
            var flames = Emitter(root, AdditiveMaterial("Flame", flame), 70f, 250);
            FlameLook(flames, 0.35f, 0.6f, 0.18f, 0.35f);
            Curve(root, 0f, 1.5f);

            var embersGo = new GameObject("Embers");
            embersGo.transform.SetParent(root.transform, false);
            var embers = Emitter(embersGo, AdditiveMaterial("Spark", dot), 14f, 120);
            EmberLook(embers, 0.8f, 1.6f, 0.2f, 0.6f);
            Curve(embersGo, 0f, 1f);

            var smokeGo = new GameObject("Smoke");
            smokeGo.transform.SetParent(root.transform, false);
            var smoke = Emitter(smokeGo, AlphaMaterial("Mist", dot), 8f, 80);
            SmokeLook(smoke, 1.2f, 2f, 0.3f, 0.5f);
            Curve(smokeGo, 0.5f, 1f);
            return Save(root, path);
        }

        // Smoke puff and a few embers when the last stack burns out.
        private static GameObject BurnOut(Texture2D dot)
        {
            string path = PrefabDir + "/BurnOut.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("BurnOut");
            var smoke = Burst(root, AlphaMaterial("Mist", dot), 8);
            SmokeLook(smoke, 1f, 1.6f, 0.35f, 0.6f);
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            var embersGo = new GameObject("Embers");
            embersGo.transform.SetParent(root.transform, false);
            var embers = Burst(embersGo, AdditiveMaterial("Spark", dot), 15);
            EmberLook(embers, 0.6f, 1.2f, 0.5f, 1.5f);
            shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            return Save(root, path);
        }

        private static void Curve(GameObject go, float startAt, float power)
        {
            var c = go.AddComponent<AuraShareCurve>();
            c.startAt = startAt;
            c.power = power;
        }

        // Shared crack network; Break adds the amber shield-rim glow pulsing inside.
        private static void CrackLook(Material m, bool glow)
        {
            m.SetColor("_Color", new Color(0.05f, 0.04f, 0.035f, 1f));
            m.SetColor("_EdgeColor", new Color(0.45f, 0.4f, 0.33f, 1f));
            m.SetColor("_GlowColor", glow ? Amber * 2.5f : Color.clear);
            m.SetFloat("_Fill", 0f);
            m.SetFloat("_RimStrength", glow ? 0.6f : 0f);
            m.SetFloat("_NoiseScale", 5f);
            m.SetFloat("_NoiseSpeed", 0f);
            m.SetFloat("_NoiseStrength", 0f);
            m.SetFloat("_CrackScale", 6f);
            m.SetFloat("_CrackWidth", 0.03f);
            m.SetFloat("_PulseSpeed", glow ? 0.6f : 0f);
            m.SetFloat("_PulseAmount", 0f);
        }

        // Rock chunks and dust bursting off the body when Fracture breaks.
        private static GameObject RockBurst(Texture2D dot)
        {
            string path = PrefabDir + "/BreakRockBurst.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("BreakRockBurst");
            var rocks = Burst(root, RockMaterial(dot), 14);
            RockLook(rocks, 0.8f, 1.2f, 3f, 6f, 0.06f, 0.14f, 1.5f);
            var shape = rocks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            var dustGo = new GameObject("Dust");
            dustGo.transform.SetParent(root.transform, false);
            var dust = Burst(dustGo, AlphaMaterial("Mist", dot), 12);
            DustLook(dust, 1f, 1.6f, 0.5f, 1.5f, 0.4f, 0.7f, 0.45f);
            shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            return Save(root, path);
        }

        // Chips and dust trickling off the skin while Broken.
        private static GameObject BreakCrumble(Texture2D dot)
        {
            string path = PrefabDir + "/BreakCrumble.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("BreakCrumble");
            var chips = Emitter(root, RockMaterial(dot), 12f, 80);
            RockLook(chips, 0.6f, 1f, 0f, 0.2f, 0.03f, 0.06f, 1f);

            var dustGo = new GameObject("Dust");
            dustGo.transform.SetParent(root.transform, false);
            var dust = Emitter(dustGo, AlphaMaterial("Mist", dot), 6f, 40);
            DustLook(dust, 0.8f, 1.4f, 0f, 0.1f, 0.15f, 0.3f, 0.3f);
            var main = dust.main;
            main.gravityModifier = 0.05f;
            return Save(root, path);
        }

        // Small dust puff as Break wears off.
        private static GameObject DustPuff(Texture2D dot)
        {
            string path = PrefabDir + "/BreakDustPuff.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("BreakDustPuff");
            var dust = Burst(root, AlphaMaterial("Mist", dot), 8);
            DustLook(dust, 0.8f, 1.3f, 0.3f, 0.8f, 0.3f, 0.5f, 0.35f);
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            return Save(root, path);
        }

        // Tumbling rock meshes that fall; dots if the rock mesh is missing.
        private static void RockLook(ParticleSystem ps, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = gravity;
            main.startColor = Color.white;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
            var rockGo = AssetDatabase.LoadAssetAtPath<GameObject>(RockPath);
            var mesh = rockGo != null ? rockGo.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) return;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
        }

        // Brown-grey dust that spreads and settles.
        private static void DustLook(ParticleSystem ps, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float alpha)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = 0.02f;
            main.startColor = new Color(0.55f, 0.5f, 0.42f, 1f);
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.limit = 0.3f;
            lv.dampen = 0.15f;
            Fade(ps, alpha);
            Grow(ps, 2.2f);
        }

        // URP Lit rock for chunks and chips, on the PixPlays rock texture.
        private static Material RockMaterial(Texture2D dot)
        {
            string path = MatDir + "/Rock_URP.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return AlphaMaterial("Mist", dot);
            m = new Material(shader);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RockTexPath);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.75f, 0.7f, 0.62f, 1f));
            SetF(m, "_Smoothness", 0.1f);
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // Dark specks sinking off the skin and a ground ring pulsing inward at the feet.
        private static GameObject SlowDrag(Texture2D dot, Texture2D ring)
        {
            string path = PrefabDir + "/SlowDrag.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("SlowDrag");
            var motes = Emitter(root, AlphaMaterial("Mist", dot), 10f, 60);
            var main = motes.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            main.gravityModifier = 0.4f;
            main.startColor = new Color(0.08f, 0.08f, 0.09f, 1f);
            Fade(motes, 0.9f);

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(root.transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            var rings = Emitter(ringGo, AlphaMaterial("Ring", ring), 1.2f, 4);
            main = rings.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 1.2f;
            main.startSpeed = 0f;
            main.startSize = 1.6f;
            main.startColor = new Color(0.05f, 0.05f, 0.06f, 1f);
            var shape = rings.shape;
            shape.enabled = false;
            var sol = rings.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));
            Fade(rings, 0.5f);
            rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var c = ringGo.AddComponent<AuraShareCurve>();
            c.ownShape = true;
            return Save(root, path);
        }

        // Rising licks: grow then shrink, yellow to orange to deep red.
        private static void FlameLook(ParticleSystem ps, float lifeMin, float lifeMax, float sizeMin, float sizeMax)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            main.gravityModifier = -0.8f;
            main.startColor = Color.white;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.45f), new GradientColorKey(new Color(0.6f, 0.1f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var k = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, k);
        }

        // Tiny bright specks drifting up with a wobble, orange cooling to red.
        private static void EmberLook(ParticleSystem ps, float lifeMin, float lifeMax, float speedMin, float speedMax)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
            main.gravityModifier = -0.25f;
            main.startColor = Color.white;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 0.6f), new GradientColorKey(new Color(0.5f, 0.05f, 0f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 1f;
            noise.scrollSpeed = 0.5f;
        }

        // Thin dark wisps that rise and spread.
        private static void SmokeLook(ParticleSystem ps, float lifeMin, float lifeMax, float sizeMin, float sizeMax)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = -0.15f;
            main.startColor = new Color(0.12f, 0.1f, 0.09f, 1f);
            Fade(ps, 0.35f);
            Grow(ps, 2.5f);
        }

        // Drips falling off the skin, bubbles swelling and popping, haze from half share.
        private static GameObject PoisonOoze(Texture2D dot, Texture2D bubble)
        {
            string path = PrefabDir + "/PoisonOoze.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("PoisonOoze");
            var drips = Emitter(root, AlphaMaterial("Drop", dot), 20f, 120);
            var main = drips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.gravityModifier = 1f;
            main.startColor = new Color(0.35f, 0.9f, 0.15f, 0.9f);
            var r = drips.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
            Fade(drips, 1f);

            var bubblesGo = new GameObject("Bubbles");
            bubblesGo.transform.SetParent(root.transform, false);
            var bubbles = Emitter(bubblesGo, AlphaMaterial("Bubble", bubble), 15f, 100);
            main = bubbles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.gravityModifier = -0.1f;
            main.startColor = new Color(0.6f, 1f, 0.35f, 0.9f);
            var sol = bubbles.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.8f, 1f), new Keyframe(1f, 1.2f)));
            var col = bubbles.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var hazeGo = new GameObject("Haze");
            hazeGo.transform.SetParent(root.transform, false);
            var haze = Emitter(hazeGo, AlphaMaterial("Mist", dot), 10f, 80);
            HazeLook(haze, 1.5f, 2.5f, 0.4f, 0.7f, 0.25f);
            Curve(hazeGo, 0.5f, 1f);
            return Save(root, path);
        }

        // Green gas puff when the last stack ends.
        private static GameObject GasPuff(Texture2D dot)
        {
            string path = PrefabDir + "/PoisonGasPuff.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("PoisonGasPuff");
            var gas = Burst(root, AlphaMaterial("Mist", dot), 10);
            HazeLook(gas, 1.2f, 2f, 0.5f, 0.8f, 0.4f);
            var main = gas.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            var shape = gas.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            return Save(root, path);
        }

        // Slow sickly green gas that drifts up and spreads.
        private static void HazeLook(ParticleSystem ps, float lifeMin, float lifeMax, float sizeMin, float sizeMax, float alpha)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = -0.03f;
            main.startColor = new Color(0.35f, 0.65f, 0.15f, 1f);
            Fade(ps, alpha);
            Grow(ps, 2f);
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

        // Flame lick: rounded base, pointed top, soft edges. White; colour comes from the particle.
        private static Texture2D FlameTexture()
        {
            string path = MatDir + "/FlameLick.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            const int w = 64, h = 128;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "FlameLick", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float rad = 0.9f * Mathf.Pow(1f - v, 0.8f) * Mathf.Sqrt(Mathf.Clamp01(v / 0.25f));
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (float)(w - 1) * 2f - 1f);
                    float a = rad > 0f ? Mathf.Clamp01(1f - u / rad) : 0f;
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Pow(a, 1.5f));
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        // Soft flat ring for ground effects. White; colour comes from the particle.
        private static Texture2D RingTexture()
        {
            string path = MatDir + "/Ring.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            const int n = 128;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Ring", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.15f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        // Bubble: thin ring with a small highlight. White; colour comes from the particle.
        private static Texture2D BubbleTexture()
        {
            string path = MatDir + "/Bubble.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            const int n = 64;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Bubble", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.12f);
                    float inner = d < 0.85f ? 0.15f : 0f;
                    float hx = dx + 0.35f, hy = dy - 0.35f;
                    float spec = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.18f);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring + inner + spec));
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        // Additive unlit particle material with the soft dot, so sparks glow.
        private static Material AdditiveMaterial(string name, Texture2D tex)
        {
            string path = MatDir + "/Particle_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            SetF(m, "_Surface", 1f);
            SetF(m, "_Blend", 2f);
            SetF(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetF(m, "_DstBlend", (float)BlendMode.One);
            SetF(m, "_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ wiring

        private static StatusSO Find(string assetName)
            => AssetDatabase.FindAssets(assetName)
                .Select(g => AssetDatabase.LoadAssetAtPath<StatusSO>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(s => s != null && s.name == assetName);

        private static void Wire(string assetName, System.Action<StatusSO> setup)
        {
            var status = Find(assetName);
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
