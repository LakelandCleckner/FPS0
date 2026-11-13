using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Stats;

// Copies per-bone hitboxes and ragdoll parts from a template humanoid enemy, matched by Humanoid bone and rescaled to the new body
public static class HitboxBoneCopier
{
    // Limb bones scale by their own segment length; everything else by body height
    private static readonly Dictionary<HumanBodyBones, HumanBodyBones> Segments = new Dictionary<HumanBodyBones, HumanBodyBones>
    {
        { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm },
        { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
        { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm },
        { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
        { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg },
        { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
        { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg },
        { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot },
    };

    private const string UndoName = "Copy Hitbox Bones";

    // Template may be a prefab asset or a scene object; report lines starting with "?" are warnings, "!" errors
    public static void Copy(GameObject template, GameObject target, bool removeStray, List<string> report)
    {
        string path = EditorUtility.IsPersistent(template) ? AssetDatabase.GetAssetPath(template) : null;
        var src = path != null ? PrefabUtility.LoadPrefabContents(path) : template;
        try { CopyFrom(src, target, removeStray, report); }
        finally { if (path != null) PrefabUtility.UnloadPrefabContents(src); }
    }

    private static void CopyFrom(GameObject src, GameObject dst, bool removeStray, List<string> report)
    {
        var srcBones = HumanBones(src);
        var dstBones = HumanBones(dst);
        if (srcBones == null) { report.Add("! Template has no Humanoid Animator."); return; }
        if (dstBones == null) { report.Add("! Target has no Humanoid Animator."); return; }

        var combatant = dst.GetComponent<CombatantStats>();
        if (combatant == null) report.Add("? No CombatantStats on the target root: set the hitbox combatants by hand.");

        var srcIds = new Dictionary<Transform, HumanBodyBones>();
        foreach (var kv in srcBones) srcIds[kv.Value] = kv.Key;

        // Template bones with a hitbox or a ragdoll body
        var ids = new List<HumanBodyBones>();
        foreach (var t in src.GetComponentsInChildren<Transform>(true))
        {
            bool hit = t.GetComponent<EnemyHitbox>() != null;
            if (!hit && t.GetComponent<Rigidbody>() == null) continue;
            if (!srcIds.TryGetValue(t, out var id)) { if (hit) report.Add($"? Template hitbox '{t.name}' is not a Humanoid bone: skipped."); continue; }
            if (!dstBones.ContainsKey(id)) { report.Add($"? Target has no {id} bone: skipped."); continue; }
            ids.Add(id);
        }
        if (ids.Count == 0) { report.Add("! Template has no hitbox or ragdoll bones."); return; }

        float height = Ratio(srcBones, dstBones, HumanBodyBones.Hips, HumanBodyBones.Head, 1f);

        // Clear earlier copies: joints first, they need their bodies
        foreach (var id in ids) foreach (var j in dstBones[id].GetComponents<CharacterJoint>()) Undo.DestroyObjectImmediate(j);
        foreach (var id in ids)
        {
            var d = dstBones[id];
            foreach (var h in d.GetComponents<EnemyHitbox>()) Undo.DestroyObjectImmediate(h);
            foreach (var c in d.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);
            foreach (var r in d.GetComponents<Rigidbody>()) Undo.DestroyObjectImmediate(r);
        }

        var dstBoneSet = new HashSet<Transform>(dstBones.Values);
        int hitboxes = 0, bodies = 0, joints = 0, extras = 0;
        foreach (var id in ids)
        {
            Transform s = srcBones[id], d = dstBones[id];
            float k = Segments.TryGetValue(id, out var end) ? Ratio(srcBones, dstBones, id, end, height) : height;
            var r = LocalMap(id, srcBones, dstBones);

            Undo.RecordObject(d.gameObject, UndoName);
            d.gameObject.layer = s.gameObject.layer;

            foreach (var c in s.GetComponents<Collider>()) CopyCollider(c, s, d, r, k, report);

            // Collider-only children of the bone (e.g. a head capsule on its own object)
            foreach (Transform child in s)
            {
                if (srcIds.ContainsKey(child) || child.name.StartsWith("HB_Hull") || child.GetComponent<Collider>() == null) continue;
                CopyChild(child, s, d, r, k, combatant, dstBoneSet, report);
                extras++;
            }

            var rb = s.GetComponent<Rigidbody>();
            if (rb != null) { EditorUtility.CopySerialized(rb, Undo.AddComponent<Rigidbody>(d.gameObject)); bodies++; }

            var hb = s.GetComponent<EnemyHitbox>();
            if (hb != null)
            {
                var n = Undo.AddComponent<EnemyHitbox>(d.gameObject);
                EditorUtility.CopySerialized(hb, n);
                n.combatant = combatant;
                hitboxes++;
            }
        }

        // Joints last so every connected body exists
        foreach (var id in ids)
        {
            Transform s = srcBones[id], d = dstBones[id];
            var j = s.GetComponent<CharacterJoint>();
            if (j == null) continue;
            var n = Undo.AddComponent<CharacterJoint>(d.gameObject);
            EditorUtility.CopySerialized(j, n);
            n.connectedBody = j.connectedBody != null && srcIds.TryGetValue(j.connectedBody.transform, out var cid) && dstBones.TryGetValue(cid, out var ct)
                ? ct.GetComponent<Rigidbody>() : null;
            if (j.connectedBody != null && n.connectedBody == null) report.Add($"? Joint on {id}: connected body not found.");
            var r = LocalMap(id, srcBones, dstBones);
            n.axis = r * j.axis;
            n.swingAxis = r * j.swingAxis;
            n.anchor = Point(j.anchor, s, d, r, Segments.TryGetValue(id, out var end) ? Ratio(srcBones, dstBones, id, end, height) : height);
            n.autoConfigureConnectedAnchor = true;
            joints++;
        }

        if (removeStray) RemoveStray(dst, ids, dstBones, report);
        report.Add($"Copied {hitboxes} hitboxes, {bodies} bodies, {joints} joints, {extras} collider objects (body scale {height:F2}).");
    }

    // Hitboxes the copy didn't place (e.g. a whole-body one) would wrap the hulls and catch every shot
    private static void RemoveStray(GameObject dst, List<HumanBodyBones> ids, Dictionary<HumanBodyBones, Transform> dstBones, List<string> report)
    {
        var placed = new HashSet<Transform>();
        foreach (var id in ids) placed.Add(dstBones[id]);
        foreach (var hb in dst.GetComponentsInChildren<EnemyHitbox>(true))
        {
            if (placed.Contains(hb.transform)) continue;
            var go = hb.gameObject;
            Undo.DestroyObjectImmediate(hb);
            foreach (var c in go.GetComponents<Collider>()) { Undo.RecordObject(c, UndoName); c.enabled = false; }
            report.Add($"? Removed stray hitbox on '{go.name}' and turned off its colliders.");
        }
    }

    // Recreates a collider child under the target bone, replacing an earlier copy of the same name
    private static void CopyChild(Transform c, Transform s, Transform d, Quaternion r, float k, CombatantStats combatant,
                                  HashSet<Transform> dstBoneSet, List<string> report)
    {
        var old = d.Find(c.name);
        if (old != null && !dstBoneSet.Contains(old)) Undo.DestroyObjectImmediate(old.gameObject);

        var go = new GameObject(c.name);
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        var t = go.transform;
        t.SetParent(d, false);
        go.layer = c.gameObject.layer;
        t.localPosition = Point(c.localPosition, s, d, r, k);
        t.localRotation = r * c.localRotation;
        t.localScale = c.localScale;

        foreach (var col in c.GetComponents<Collider>()) CopyCollider(col, c, t, Quaternion.identity, k, report);
        var hb = c.GetComponent<EnemyHitbox>();
        if (hb != null)
        {
            var n = Undo.AddComponent<EnemyHitbox>(go);
            EditorUtility.CopySerialized(hb, n);
            n.combatant = combatant;
        }
    }

    private static void CopyCollider(Collider c, Transform s, Transform d, Quaternion r, float k, List<string> report)
    {
        float f = s.lossyScale.x * k / d.lossyScale.x;
        switch (c)
        {
            case BoxCollider box:
            {
                var n = Undo.AddComponent<BoxCollider>(d.gameObject);
                EditorUtility.CopySerialized(box, n);
                n.center = Point(box.center, s, d, r, k);
                var size = Vector3.zero;
                for (int i = 0; i < 3; i++) size[Axis(i, r)] = box.size[i] * f;
                n.size = size;
                break;
            }
            case CapsuleCollider cap:
            {
                var n = Undo.AddComponent<CapsuleCollider>(d.gameObject);
                EditorUtility.CopySerialized(cap, n);
                n.center = Point(cap.center, s, d, r, k);
                n.radius = cap.radius * f;
                n.height = cap.height * f;
                n.direction = Axis(cap.direction, r);
                break;
            }
            case SphereCollider sphere:
            {
                var n = Undo.AddComponent<SphereCollider>(d.gameObject);
                EditorUtility.CopySerialized(sphere, n);
                n.center = Point(sphere.center, s, d, r, k);
                n.radius = sphere.radius * f;
                break;
            }
            default:
                report.Add($"? {c.GetType().Name} on '{s.name}' not copied.");
                break;
        }
    }

    // Bone-local point mapped onto d and scaled by k
    private static Vector3 Point(Vector3 v, Transform s, Transform d, Quaternion r, float k)
        => r * v * (s.lossyScale.x * k / d.lossyScale.x);

    // d's local axis closest to s's local axis i
    private static int Axis(int i, Quaternion r)
    {
        var unit = Vector3.zero;
        unit[i] = 1f;
        var v = r * unit;
        float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
        return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
    }

    // Next bone along each chain; its local direction is the bone's axis whatever pose the prefab was saved in
    private static readonly Dictionary<HumanBodyBones, HumanBodyBones[]> Next = new Dictionary<HumanBodyBones, HumanBodyBones[]>
    {
        { HumanBodyBones.Hips, new[] { HumanBodyBones.Spine } },
        { HumanBodyBones.Spine, new[] { HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck } },
        { HumanBodyBones.Chest, new[] { HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head } },
        { HumanBodyBones.UpperChest, new[] { HumanBodyBones.Neck, HumanBodyBones.Head } },
        { HumanBodyBones.Neck, new[] { HumanBodyBones.Head } },
        { HumanBodyBones.LeftShoulder, new[] { HumanBodyBones.LeftUpperArm } },
        { HumanBodyBones.RightShoulder, new[] { HumanBodyBones.RightUpperArm } },
        { HumanBodyBones.LeftUpperArm, new[] { HumanBodyBones.LeftLowerArm } },
        { HumanBodyBones.LeftLowerArm, new[] { HumanBodyBones.LeftHand } },
        { HumanBodyBones.RightUpperArm, new[] { HumanBodyBones.RightLowerArm } },
        { HumanBodyBones.RightLowerArm, new[] { HumanBodyBones.RightHand } },
        { HumanBodyBones.LeftUpperLeg, new[] { HumanBodyBones.LeftLowerLeg } },
        { HumanBodyBones.LeftLowerLeg, new[] { HumanBodyBones.LeftFoot } },
        { HumanBodyBones.RightUpperLeg, new[] { HumanBodyBones.RightLowerLeg } },
        { HumanBodyBones.RightLowerLeg, new[] { HumanBodyBones.RightFoot } },
        { HumanBodyBones.LeftFoot, new[] { HumanBodyBones.LeftToes } },
        { HumanBodyBones.RightFoot, new[] { HumanBodyBones.RightToes } },
    };

    // Rotation from the template bone's local frame to the target's, matched on the bone axis; identity for end bones (same rig convention)
    private static Quaternion LocalMap(HumanBodyBones id, Dictionary<HumanBodyBones, Transform> s, Dictionary<HumanBodyBones, Transform> d)
    {
        if (!Next.TryGetValue(id, out var nexts)) return Quaternion.identity;
        foreach (var n in nexts)
        {
            if (!s.TryGetValue(n, out var sn) || !d.TryGetValue(n, out var dn)) continue;
            Vector3 sd = s[id].InverseTransformPoint(sn.position);
            Vector3 dd = d[id].InverseTransformPoint(dn.position);
            if (sd.sqrMagnitude < 1e-8f || dd.sqrMagnitude < 1e-8f) continue;
            return Quaternion.FromToRotation(sd, dd);
        }
        return Quaternion.identity;
    }

    // Target length over template length between two bones
    private static float Ratio(Dictionary<HumanBodyBones, Transform> s, Dictionary<HumanBodyBones, Transform> d,
                               HumanBodyBones a, HumanBodyBones b, float fallback)
    {
        if (!s.TryGetValue(a, out var sa) || !s.TryGetValue(b, out var sb) || !d.TryGetValue(a, out var da) || !d.TryGetValue(b, out var db))
            return fallback;
        float sl = Vector3.Distance(sa.position, sb.position);
        float dl = Vector3.Distance(da.position, db.position);
        return sl > 1e-4f && dl > 1e-4f ? dl / sl : fallback;
    }

    // Humanoid bone map from the first Humanoid Animator's avatar, read by name so it works on unopened prefabs
    public static Dictionary<HumanBodyBones, Transform> HumanBones(GameObject root)
    {
        Animator anim = null;
        foreach (var a in root.GetComponentsInChildren<Animator>(true))
            if (a.avatar != null && a.avatar.isHuman) { anim = a; break; }
        if (anim == null) return null;

        var byName = new Dictionary<string, Transform>();
        foreach (var t in anim.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;

        var map = new Dictionary<HumanBodyBones, Transform>();
        foreach (var hb in anim.avatar.humanDescription.human)
        {
            int idx = Array.IndexOf(HumanTrait.BoneName, hb.humanName);
            if (idx >= 0 && idx < (int)HumanBodyBones.LastBone && byName.TryGetValue(hb.boneName, out var t))
                map[(HumanBodyBones)idx] = t;
        }
        if (map.Count == 0)
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var t = anim.GetBoneTransform((HumanBodyBones)i);
                if (t != null) map[(HumanBodyBones)i] = t;
            }
        return map;
    }
}
