using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Combat.Core;
using Combat.Loadout;
using Combat.Stats;
using Combat.UI;
using Combat.Weapons;

namespace Combat.Attributes.EditorTools
{
    // Creates the attribute stats, attribute assets and library, registers the stats and adds the player
    // components in every scene with a StatBootstrap, and adds the Character tab group.
    // Safe to run again: existing assets keep their tuning.
    public static class AttributeSetup
    {
        private const string StatFolder = "Assets/GameData/Stats/Definitions/Attributes";
        private const string AttributeFolder = "Assets/GameData/Attributes";
        private const string LibraryPath = AttributeFolder + "/Attributes.asset";

        private struct Feed
        {
            public string stat; public ModifierBucket bucket; public float perPoint; public ModifierTarget target;
            public Feed(string stat, ModifierBucket bucket, float perPoint, ModifierTarget target)
            { this.stat = stat; this.bucket = bucket; this.perPoint = perPoint; this.target = target; }
        }

        private struct Spec
        {
            public string id, name, theme, element;
            public string[] archetypes;
            public Feed[] feeds;
        }

        private const ModifierBucket Flat = ModifierBucket.Flat, Add = ModifierBucket.Additive;
        private const ModifierTarget P = ModifierTarget.Player, W = ModifierTarget.Weapons;

        // Placeholder per-point values; tuning comes after the point budget is set.
        private static readonly Spec[] Specs =
        {
            new Spec { id = "might", name = "Might", theme = "Heavy hits", element = "fire",
                archetypes = new[] { "heavy_slug_shotgun", "aggressive_pump_shotgun", "lever_action_shotgun", "sidearm_revolver", "heavy_sidearm" },
                feeds = new[] { new Feed("stagger_power", Add, 0.005f, W), new Feed("weapon_stability", Flat, 0.1f, W), new Feed("weapon_recoil_direction", Flat, 0.1f, W) } },
            new Spec { id = "precision", name = "Precision", theme = "Range and accuracy", element = "ice",
                archetypes = new[] { "precision_semi_rifle", "sidearm_precision", "singleslugsemi", "dueling_br" },
                feeds = new[] { new Feed("weapon_range", Flat, 0.1f, W), new Feed("aim_assist", Flat, 0.1f, W) } },
            new Spec { id = "finesse", name = "Finesse", theme = "Speed and flow", element = "lightning",
                archetypes = new[] { "sidearm_light", "sidearm_bursting", "sidearm_double_tap", "tactical_smg" },
                feeds = new[] { new Feed("handling", Flat, 0.2f, W), new Feed("ads_time", Add, -0.002f, W),
                                new Feed("move_speed", Add, 0.001f, P), new Feed("sprint_multiplier", Add, 0.001f, P),
                                new Feed("jump_force", Add, 0.001f, P), new Feed("ground_acceleration", Add, 0.001f, P),
                                new Feed("air_acceleration", Add, 0.001f, P) } },
            new Spec { id = "focus", name = "Focus", theme = "Sustained fire", element = "poison",
                archetypes = new[] { "rapid_auto_rifle", "rapid_smg", "spraying_smg", "sidearm_machine_pistol", "auto_shotgun" },
                feeds = new[] { new Feed("magazine_size", Add, 0.002f, W), new Feed("reload_time", Add, -0.002f, W) } },
            new Spec { id = "grit", name = "Grit", theme = "Brawling and survival", element = "physical",
                archetypes = new[] { "sidearm_scatter", "double_barrel_shotgun", "burst_shotgun", "rapid_semi_shotgun" },
                feeds = new[] { new Feed("max_health", Add, 0.005f, P), new Feed("damage_taken", Add, -0.001f, P),
                                new Feed("physical_resistance", Add, 0.001f, P), new Feed("fire_resistance", Add, 0.001f, P),
                                new Feed("cold_resistance", Add, 0.001f, P), new Feed("lightning_resistance", Add, 0.001f, P),
                                new Feed("poison_resistance", Add, 0.001f, P), new Feed("pellet_spread", Add, -0.002f, W) } },
            new Spec { id = "fortune", name = "Fortune", theme = "Loot", element = null,
                archetypes = new string[0], feeds = new Feed[0] },
        };

        [MenuItem("FPS0/Stats/Setup Attributes", priority = 650)]
        private static void Run()
        {
            EnsureFolder(StatFolder);
            EnsureFolder(AttributeFolder);

            var stats = ById(AttributeValidation.LoadAll<StatDefinitionSO>(), s => s.id);
            var types = ById(AttributeValidation.LoadAll<DamageTypeSO>(), t => t.id);
            var archs = ById(AttributeValidation.LoadAll<WeaponArchetypeSO>(), a => a.id);
            var log = new List<string>();

            var library = AssetDatabase.LoadAssetAtPath<AttributeLibrarySO>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AttributeLibrarySO>();
                AssetDatabase.CreateAsset(library, LibraryPath);
                log.Add("Created the attribute library.");
            }

            var created = new List<StatDefinitionSO>();
            foreach (var spec in Specs)
            {
                var stat = StatAsset(spec, stats, log);
                created.Add(stat);
                var attr = AttributeAsset(spec, stat, stats, types, archs, log);
                if (!library.attributes.Contains(attr)) library.attributes.Add(attr);
            }
            EditorUtility.SetDirty(library);

            UpdateScenes(library, created, log);
            AddSheetGroup(created, log);
            AssetDatabase.SaveAssets();

            var problems = AttributeValidation.Problems(library);
            foreach (var p in problems) log.Add("Check: " + p);
            Selection.activeObject = library;
            Debug.Log("[AttributeSetup] Done.\n" + string.Join("\n", log));
        }

        private static StatDefinitionSO StatAsset(Spec spec, Dictionary<string, StatDefinitionSO> stats, List<string> log)
        {
            if (stats.TryGetValue(spec.id, out var existing)) return existing;

            var stat = ScriptableObject.CreateInstance<StatDefinitionSO>();
            stat.id = spec.id;
            stat.displayName = spec.name;
            stat.resolutionMode = StatResolutionMode.ScaledBase;
            stat.useMin = true;
            stat.minValue = 0f;
            AssetDatabase.CreateAsset(stat, $"{StatFolder}/{spec.name}.asset");
            stats[spec.id] = stat;
            log.Add($"Created stat {spec.id}.");
            return stat;
        }

        private static AttributeSO AttributeAsset(Spec spec, StatDefinitionSO stat, Dictionary<string, StatDefinitionSO> stats,
            Dictionary<string, DamageTypeSO> types, Dictionary<string, WeaponArchetypeSO> archs, List<string> log)
        {
            string path = $"{AttributeFolder}/{spec.name}.asset";
            var attr = AssetDatabase.LoadAssetAtPath<AttributeSO>(path);
            if (attr != null)
            {
                if (attr.stat == null) { attr.stat = stat; EditorUtility.SetDirty(attr); }
                return attr;
            }

            attr = ScriptableObject.CreateInstance<AttributeSO>();
            attr.stat = stat;
            attr.theme = spec.theme;

            if (spec.element != null)
            {
                if (types.TryGetValue(spec.element, out var t)) attr.element = t;
                else log.Add($"{spec.name}: no damage type with id '{spec.element}'.");
            }

            foreach (var id in spec.archetypes)
            {
                if (archs.TryGetValue(id, out var a)) attr.archetypes.Add(a);
                else log.Add($"{spec.name}: no archetype with id '{id}'.");
            }

            foreach (var f in spec.feeds)
            {
                if (!stats.TryGetValue(f.stat, out var s)) { log.Add($"{spec.name}: no stat with id '{f.stat}'."); continue; }
                attr.feeds.Add(new AttributeFeed { stat = s, bucket = f.bucket, perPoint = f.perPoint, target = f.target });
            }

            AssetDatabase.CreateAsset(attr, path);
            log.Add($"Created attribute {spec.name}.");
            return attr;
        }

        // Opens every scene with a StatBootstrap, updates it, saves it, and closes the ones it opened.
        private static void UpdateScenes(AttributeLibrarySO library, List<StatDefinitionSO> defs, List<string> log)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { log.Add("Scene update cancelled."); return; }

            string guid = ScriptGuid(typeof(StatBootstrap));
            if (string.IsNullOrEmpty(guid)) { log.Add("StatBootstrap script not found: scenes not updated."); return; }

            var loaded = new Dictionary<string, Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (sc.isLoaded && !string.IsNullOrEmpty(sc.path)) loaded[sc.path] = sc;
            }

            int found = 0;
            foreach (var sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuid);
                if (!File.ReadAllText(path).Contains(guid)) continue;
                found++;

                bool opened = !loaded.TryGetValue(path, out var scene);
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                bool changed = Register(scene, defs, log);
                changed |= AddPlayerComponents(scene, library, log);
                if (changed) EditorSceneManager.SaveScene(scene);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            if (found == 0) log.Add("No scene has a StatBootstrap: stats not registered.");
        }

        // Adds the attribute stats to every StatBootstrap in the scene.
        private static bool Register(Scene scene, List<StatDefinitionSO> defs, List<string> log)
        {
            bool changed = false;
            foreach (var boot in InScene<StatBootstrap>(scene))
            {
                var so = new SerializedObject(boot);
                var list = so.FindProperty("statDefinitions");
                var have = new HashSet<Object>();
                for (int i = 0; i < list.arraySize; i++) have.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

                int added = 0;
                foreach (var d in defs)
                {
                    if (d == null || have.Contains(d)) continue;
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = d;
                    added++;
                }
                if (added == 0) continue;
                so.ApplyModifiedProperties();
                changed = true;
                log.Add($"Registered {added} stats on {boot.name} ({scene.name}).");
            }
            return changed;
        }

        // PlayerAttributes next to the loadout modifier applier, debug keys next to the loadout debug keys.
        private static bool AddPlayerComponents(Scene scene, AttributeLibrarySO library, List<string> log)
        {
            var appliers = InScene<LoadoutModifierApplier>(scene);
            if (appliers.Count == 0) return false;

            bool changed = false;
            var host = appliers[0].gameObject;
            var attrs = host.GetComponent<PlayerAttributes>();
            if (attrs == null)
            {
                attrs = Undo.AddComponent<PlayerAttributes>(host);
                changed = true;
                log.Add($"Added PlayerAttributes to {host.name} ({scene.name}).");
            }
            var so = new SerializedObject(attrs);
            var lib = so.FindProperty("library");
            if (lib.objectReferenceValue == null)
            {
                lib.objectReferenceValue = library;
                so.ApplyModifiedProperties();
                changed = true;
            }

            var debugHosts = InScene<LoadoutDebugKeys>(scene);
            var keysHost = debugHosts.Count > 0 ? debugHosts[0].gameObject : host;
            if (keysHost.GetComponent<AttributeDebugKeys>() == null)
            {
                Undo.AddComponent<AttributeDebugKeys>(keysHost);
                changed = true;
                log.Add($"Added AttributeDebugKeys to {keysHost.name} ({scene.name}).");
            }
            return changed;
        }

        private static List<T> InScene<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        private static string ScriptGuid(System.Type type)
        {
            foreach (var g in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (script != null && script.GetClass() == type) return g;
            }
            return null;
        }

        // An Attributes group at the top of the character stat sheet.
        private static void AddSheetGroup(List<StatDefinitionSO> defs, List<string> log)
        {
            StatSheetSO sheet = null;
            foreach (var s in AttributeValidation.LoadAll<StatSheetSO>())
                if (s.name == "CharacterStatSheet") { sheet = s; break; }
            if (sheet == null) { log.Add("No CharacterStatSheet asset found: Character tab group not added."); return; }

            foreach (var g in sheet.groups)
                if (g != null && g.label == "Attributes") return;

            var group = new StatSheetSO.Group { label = "Attributes" };
            foreach (var d in defs)
                if (d != null) group.entries.Add(new StatSheetSO.Entry { stat = d, display = StatDisplay.Number, decimals = 0 });

            Undo.RecordObject(sheet, "Add Attributes group");
            sheet.groups.Insert(0, group);
            EditorUtility.SetDirty(sheet);
            log.Add("Added the Attributes group to the Character tab.");
        }

        private static Dictionary<string, T> ById<T>(List<T> assets, System.Func<T, string> id)
        {
            var map = new Dictionary<string, T>();
            foreach (var a in assets)
            {
                string key = id(a);
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key)) map[key] = a;
            }
            return map;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
