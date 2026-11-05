using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Combat.Enemies;
using Combat.Loadout;
using Combat.UI;

namespace Combat.Loot.EditorTools
{
    // One-click setup for item drops. Safe to run again: skips anything already in place.
    public static class ItemDropSetup
    {
        private const string LootDir = "Assets/GameData/Loot";
        private const string PrefabDir = "Assets/Prefabs/Loot";
        private const string MarkerPath = LootDir + "/LootMarker.mat";
        private const string BeamPath = LootDir + "/LootBeam.mat";
        private const string TablePath = LootDir + "/LootTable_Default.asset";
        private const string DropPath = PrefabDir + "/ItemDrop.prefab";
        private const string StylePath = "Assets/GameData/UI/UIStyle.asset";
        private const string ClipPath = "Assets/Audio/FX/Weapons/Handgun/AmmoCollect.wav";
        private const string EnemyPath = "Assets/Prefabs/Enemies/EnemyRoot_ZombieAlien_Base.prefab";
        private const string PlayerDir = "Assets/Prefabs/Player";
        private const string Tag = "[ItemDropSetup] ";

        [MenuItem("Tools/Loot/Setup Item Drops")]
        public static void Run()
        {
            Folder(LootDir);
            Folder(PrefabDir);

            var marker = MarkerMaterial();
            var beam = BeamMaterial();
            var drop = DropPrefab(marker, beam);
            var table = Table(drop);

            SetupEnemy(table);
            SetupPlayerPrefabs(table);
            SetupScene(table);

            AssetDatabase.SaveAssets();
            Selection.activeObject = table;
            Debug.Log(Tag + "Done.");
        }

        // ------------------------------------------------------------------ assets

        private static Material MarkerMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MarkerPath);
            if (m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, MarkerPath);
            Debug.Log(Tag + "Created " + MarkerPath);
            return m;
        }

        // Transparent additive, so the beam glows and fades out by vertex alpha.
        private static Material BeamMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(BeamPath);
            if (m != null) return m;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(shader);

            Set(m, "_Surface", 1f);
            Set(m, "_Blend", 2f);
            Set(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(m, "_DstBlend", (float)BlendMode.One);
            Set(m, "_SrcBlendAlpha", (float)BlendMode.One);
            Set(m, "_DstBlendAlpha", (float)BlendMode.One);
            Set(m, "_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(m, BeamPath);
            Debug.Log(Tag + "Created " + BeamPath);
            return m;
        }

        private static ItemDrop DropPrefab(Material marker, Material beamMat)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ItemDrop>(DropPath);
            if (existing != null) return existing;

            var root = new GameObject("ItemDrop");

            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.5f, 0f);
            col.radius = 0.75f;

            var item = root.AddComponent<ItemDrop>();

            var line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.up * 2.5f);
            line.widthMultiplier = 0.05f;
            line.sharedMaterial = beamMat;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, 0.4f, 0f);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            cube.name = "Marker";
            cube.transform.SetParent(visual, false);
            cube.transform.localScale = Vector3.one * 0.25f;
            cube.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            var cubeRenderer = cube.GetComponent<MeshRenderer>();
            cubeRenderer.sharedMaterial = marker;
            cubeRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 3f;
            glow.intensity = 2f;
            glow.shadows = LightShadows.None;

            // Shots and the ground probe ignore drops.
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ignoreRaycast;

            var so = new SerializedObject(item);
            so.FindProperty("style").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UIStyleSO>(StylePath);
            so.FindProperty("visual").objectReferenceValue = visual;
            var tinted = so.FindProperty("tinted");
            tinted.arraySize = 1;
            tinted.GetArrayElementAtIndex(0).objectReferenceValue = cubeRenderer;
            so.FindProperty("beam").objectReferenceValue = line;
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("pickupClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (so.FindProperty("style").objectReferenceValue == null) Debug.LogWarning(Tag + "No UIStyle at " + StylePath + "; drops will be white.");

            var saved = PrefabUtility.SaveAsPrefabAsset(root, DropPath);
            Object.DestroyImmediate(root);
            Debug.Log(Tag + "Created " + DropPath);
            return saved.GetComponent<ItemDrop>();
        }

        private static LootTableSO Table(ItemDrop drop)
        {
            var table = AssetDatabase.LoadAssetAtPath<LootTableSO>(TablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTableSO>();
                table.groundMask = LayerMask.GetMask("Default", "Ground");
                table.dropPrefab = drop;
                AssetDatabase.CreateAsset(table, TablePath);
                Debug.Log(Tag + "Created " + TablePath);
            }
            else if (table.dropPrefab == null)
            {
                table.dropPrefab = drop;
                EditorUtility.SetDirty(table);
                Debug.Log(Tag + "Set drop prefab on " + TablePath);
            }
            return table;
        }

        // ------------------------------------------------------------------ wiring

        // Only the spawner's enemy. Added as an override here, so other prefabs sharing the base don't drop.
        private static void SetupEnemy(LootTableSO table)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath) == null)
            {
                Debug.LogWarning(Tag + "No enemy prefab at " + EnemyPath);
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(EnemyPath);
            var health = root.GetComponentInChildren<CombatantHealth>(true);
            if (health == null)
            {
                Debug.LogWarning(Tag + "No CombatantHealth in " + EnemyPath);
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            bool changed = false;
            var dropper = health.GetComponent<EnemyLootDropper>();
            if (dropper == null) { dropper = health.gameObject.AddComponent<EnemyLootDropper>(); changed = true; }
            changed |= AssignIfEmpty(dropper, "lootTable", table);

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, EnemyPath);
                Debug.Log(Tag + "EnemyLootDropper on " + EnemyPath + " (" + health.name + ")");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void SetupPlayerPrefabs(LootTableSO table)
        {
            var done = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerDir }))
                SetupPlayerPrefab(AssetDatabase.GUIDToAssetPath(guid), table, done);
        }

        // Edits each component in the prefab that owns it, so nested and variant prefabs never get duplicates.
        private static void SetupPlayerPrefab(string path, LootTableSO table, HashSet<string> done)
        {
            if (!done.Add(path)) return;

            var root = PrefabUtility.LoadPrefabContents(path);
            var sources = new List<string>();
            bool changed = false;

            foreach (var inv in root.GetComponentsInChildren<PlayerInventory>(true))
            {
                if (Owned(inv.gameObject, path, sources)) continue;
                if (inv.GetComponent<PlayerVault>() != null) continue;
                inv.gameObject.AddComponent<PlayerVault>();
                Debug.Log(Tag + "PlayerVault on " + path + " (" + inv.name + ")");
                changed = true;
            }

            foreach (var keys in root.GetComponentsInChildren<LoadoutDebugKeys>(true))
            {
                if (Owned(keys.gameObject, path, sources)) continue;
                if (AssignIfEmpty(keys, "debugLootTable", table))
                {
                    Debug.Log(Tag + "Debug loot table on " + path + " (" + keys.name + ")");
                    changed = true;
                }
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            foreach (var s in sources) SetupPlayerPrefab(s, table, done);
        }

        // True when the object comes from another prefab; that prefab's path is queued instead.
        private static bool Owned(GameObject go, string path, List<string> sources)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return false;
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            string p = src != null ? AssetDatabase.GetAssetPath(src) : null;
            if (string.IsNullOrEmpty(p) || p == path) return false;
            if (!sources.Contains(p)) sources.Add(p);
            return true;
        }

        // Covers a player placed in the open scene without a prefab, or with the fields overridden.
        private static void SetupScene(LootTableSO table)
        {
            bool dirty = false;

            foreach (var inv in Object.FindObjectsByType<PlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (inv.GetComponent<PlayerVault>() != null) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(inv.gameObject);
                if (src != null && src.GetComponent<PlayerVault>() != null) continue;
                Undo.AddComponent<PlayerVault>(inv.gameObject);
                Debug.Log(Tag + "PlayerVault on scene object " + inv.name);
                dirty = true;
            }

            foreach (var keys in Object.FindObjectsByType<LoadoutDebugKeys>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!AssignIfEmpty(keys, "debugLootTable", table)) continue;
                Debug.Log(Tag + "Debug loot table on scene object " + keys.name);
                dirty = true;
            }

            if (dirty) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ------------------------------------------------------------------ helpers

        private static bool AssignIfEmpty(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null || prop.objectReferenceValue != null) return false;
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            return true;
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
