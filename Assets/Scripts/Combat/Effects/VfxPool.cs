using System.Collections.Generic;
using UnityEngine;

namespace Combat.Effects
{
    // Pooled spawner for one-shot and looping VFX prefabs. Instances are pooled per prefab and tint, so each is tinted once.
    public static class VfxPool
    {
        private static readonly Dictionary<(GameObject, Color), Stack<VfxInstance>> free = new Dictionary<(GameObject, Color), Stack<VfxInstance>>();
        private static readonly List<VfxInstance> timed = new List<VfxInstance>(32);
        private static readonly List<VfxInstance> pending = new List<VfxInstance>(8);
        private static Transform root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            free.Clear();
            timed.Clear();
            pending.Clear();
            root = null;
        }

        // lifetime 0 = stays until Release. tint alpha 0 = leave the prefab's colours.
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent,
                                       Color tint, float lifetime, float scale = 1f)
        {
            if (prefab == null) return null;
            EnsureRoot();

            if (tint.a <= 0f) tint = Color.clear;
            VfxInstance inst = null;
            if (free.TryGetValue((prefab, tint), out var stack))
                while (stack.Count > 0 && inst == null) inst = stack.Pop();

            if (inst == null)
            {
                var go = Object.Instantiate(prefab, root);
                inst = go.AddComponent<VfxInstance>();
                inst.Prefab = prefab;
                inst.Tint = tint;
                inst.BaseScale = prefab.transform.localScale;
                if (tint.a > 0f) VfxTint.Apply(go, tint);
            }

            var t = inst.transform;
            t.SetParent(parent != null ? parent : root, false);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = inst.BaseScale * scale;

            inst.gameObject.SetActive(true);
            inst.Restart();

            inst.ReleaseAt = lifetime > 0f ? Time.time + lifetime : 0f;
            if (lifetime > 0f && !inst.Timed) { inst.Timed = true; timed.Add(inst); }
            return inst.gameObject;
        }

        // Returns an instance to its pool. Safe on null and on objects the pool didn't spawn.
        public static void Release(GameObject go)
        {
            if (go == null) return;
            var inst = go.GetComponent<VfxInstance>();
            if (inst == null) { Object.Destroy(go); return; }
            ReleaseInstance(inst);
        }

        // Release on the next frame. Use while the parent is being deactivated.
        public static void ReleaseLater(GameObject go)
        {
            if (go == null) return;
            var inst = go.GetComponent<VfxInstance>();
            if (inst != null && !pending.Contains(inst)) pending.Add(inst);
        }

        private static void ReleaseInstance(VfxInstance inst)
        {
            if (inst == null || !inst.gameObject.activeSelf) return;
            inst.gameObject.SetActive(false);
            inst.transform.SetParent(root, false);
            var key = (inst.Prefab, inst.Tint);
            if (!free.TryGetValue(key, out var stack)) free[key] = stack = new Stack<VfxInstance>();
            stack.Push(inst);
        }

        internal static void Tick()
        {
            for (int i = 0; i < pending.Count; i++) ReleaseInstance(pending[i]);
            pending.Clear();

            float now = Time.time;
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                var inst = timed[i];
                if (inst == null) { timed.RemoveAt(i); continue; }
                if (inst.ReleaseAt <= 0f || now < inst.ReleaseAt) continue;
                inst.Timed = false;
                timed[i] = timed[timed.Count - 1];
                timed.RemoveAt(timed.Count - 1);
                ReleaseInstance(inst);
            }
        }

        private static void EnsureRoot()
        {
            if (root != null) return;
            var go = new GameObject("[VfxPool]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<VfxPoolRunner>();
            root = go.transform;
        }
    }
}
