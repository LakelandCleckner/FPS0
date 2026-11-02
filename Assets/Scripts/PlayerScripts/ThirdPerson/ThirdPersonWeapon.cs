using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Weapons;

// Holds the held weapon's world model on the third-person body, with aim pitch, fire kick and left-hand IK
[DefaultExecutionOrder(120)]
public class ThirdPersonWeapon : MonoBehaviour
{
    [Header("Refs (found automatically if empty)")]
    [SerializeField] private PlayerPresentation presentation;
    [SerializeField] private Animator animator;
    [SerializeField] private BodyVisibility visibility;

    [Header("Upper body layer")]
    [SerializeField] private string upperBodyLayer = "UpperBody";
    [Tooltip("Seconds to fade the weapon pose in or out.")]
    [SerializeField] private float layerFade = 0.15f;

    [Header("Aim")]
    [SerializeField] private float maxPitch = 70f;
    [Tooltip("Share of the aim turn per bone: spine, chest, upper chest.")]
    [SerializeField] private Vector3 pitchWeights = new Vector3(0.3f, 0.3f, 0.4f);
    [Tooltip("How much the barrel is turned onto the aim line at hip (0 = pose as authored, 1 = on target). ADS is always 1.")]
    [Range(0f, 1f)]
    [SerializeField] private float hipAimWeight = 0f;

    [Header("Fire kick")]
    [Tooltip("Upward upper-body kick per shot, in degrees.")]
    [SerializeField] private float kickDegrees = 3f;
    [Tooltip("Kick never lasts longer than this, even on slow weapons.")]
    [SerializeField] private float maxKickTime = 0.15f;

    [Header("Left hand")]
    [Tooltip("Seconds to blend the support hand on or off.")]
    [SerializeField] private float supportBlend = 0.15f;

    private sealed class WorldModel
    {
        public GameObject Root;
        public GunModel Gun;
        public Renderer[] Renderers;
    }

    private readonly Dictionary<WeaponSO, WorldModel> models = new Dictionary<WeaponSO, WorldModel>();
    private WorldModel current;
    private WeaponSO currentWeapon;
    private RuntimeAnimatorController baseController;

    private int layer = -1;
    private float layerWeight;
    private float supportWeight;
    private float kickTime = -1f;
    private float kickDuration;

    private Transform rightHand, leftUpper, leftLower, leftHand;
    private readonly Transform[] spine = new Transform[3];

    private void Awake()
    {
        if (presentation == null) presentation = GetComponentInParent<PlayerPresentation>();
        if (animator == null) animator = GetComponent<Animator>();
        if (visibility == null) visibility = GetComponent<BodyVisibility>();
        if (animator == null) return;

        baseController = animator.runtimeAnimatorController;
        layer = animator.GetLayerIndex(upperBodyLayer);

        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        spine[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
        spine[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
        spine[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
    }

    private void OnEnable()
    {
        if (presentation == null) return;
        presentation.EquipStarted += HandleEquipStarted;
        presentation.WeaponEquipped += Show;
        presentation.Fired += HandleFired;
    }

    private void OnDisable()
    {
        if (presentation == null) return;
        presentation.EquipStarted -= HandleEquipStarted;
        presentation.WeaponEquipped -= Show;
        presentation.Fired -= HandleFired;
    }

    private void Start()
    {
        if (presentation != null) Show(presentation.State.HeldWeapon);
    }

    private void HandleEquipStarted(WeaponSO weapon, float duration) => Show(weapon);

    private void HandleFired(float interval)
    {
        if (current == null) return;
        kickDuration = Mathf.Max(0.01f, Mathf.Min(interval, maxKickTime));
        kickTime = 0f;
    }

    private void Update()
    {
        if (animator == null) return;
        float dt = Time.deltaTime;

        layerWeight = Mathf.MoveTowards(layerWeight, current != null ? 1f : 0f, dt / Mathf.Max(0.01f, layerFade));
        if (layer >= 0) animator.SetLayerWeight(layer, layerWeight);

        bool hasSupport = current != null && current.Gun != null && current.Gun.TpSupport != null;
        supportWeight = Mathf.MoveTowards(supportWeight, hasSupport ? 1f : 0f, dt / Mathf.Max(0.01f, supportBlend));
    }

    // After animation: pitch and kick first, then the left hand onto the moved gun
    private void LateUpdate()
    {
        if (animator == null || presentation == null) return;

        float kick = 0f;
        if (kickTime >= 0f)
        {
            kickTime += Time.deltaTime;
            if (kickTime >= kickDuration) kickTime = -1f;
            else kick = kickDegrees * (1f - kickTime / kickDuration);
        }

        float pitch = Mathf.Clamp(presentation.State.Pitch, -maxPitch, maxPitch) - kick;

        // Holding: aim the barrel down the camera ray. Otherwise plain spine pitch.
        if (current != null && current.Gun != null && layerWeight > 0f)
        {
            float aimWeight = layerWeight * Mathf.Lerp(hipAimWeight, 1f, presentation.State.AimBlend);
            ApplyPitch(pitch * (1f - aimWeight));
            if (aimWeight > 0f)
            {
                Vector3 aimDir = transform.rotation * Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
                AimSpine(aimDir, aimWeight);
            }
        }
        else
        {
            ApplyPitch(pitch);
        }

        if (supportWeight > 0f && current != null && current.Gun != null && current.Gun.TpSupport != null)
        {
            var s = current.Gun.TpSupport;
            SolveTwoBone(leftUpper, leftLower, leftHand, s.position, s.rotation, supportWeight);
        }
    }

    // ===== Weapon model =====

    private void Show(WeaponSO weapon)
    {
        if (weapon == currentWeapon) return;
        currentWeapon = weapon;

        if (current != null) current.Root.SetActive(false);
        current = weapon != null ? Acquire(weapon) : null;
        if (current != null) current.Root.SetActive(true);

        if (visibility != null) visibility.SetWeaponRenderers(current != null ? current.Renderers : Array.Empty<Renderer>());
        ApplyController(weapon);
    }

    private WorldModel Acquire(WeaponSO weapon)
    {
        if (models.TryGetValue(weapon, out var cached)) return cached;

        var vm = weapon.viewmodel;
        var prefab = vm != null ? vm.gunPrefab : null;
        if (prefab == null || rightHand == null)
        {
            models[weapon] = null;
            return null;
        }

        var go = Instantiate(prefab);
        go.name = "TP_" + prefab.name;
        WeaponLoadout.ApplySkin(go, weapon.viewmodelSkin);
        if (weapon.defaultShader != null) weapon.defaultShader.ApplyTo(go);

        // Static world copy: no gun animation, no colliders, Default layer
        foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;

        var visible = new List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.enabled) visible.Add(r);

        var gun = go.GetComponent<GunModel>();
        Attach(go.transform, gun, rightHand, prefab.transform.localScale);
        if (gun == null || gun.TpGrip == null)
            Debug.LogWarning($"[ThirdPersonWeapon] '{prefab.name}' has no TP grip; run Tools > TP Grip Tool.");

        var model = new WorldModel { Root = go, Gun = gun, Renderers = visible.ToArray() };
        go.SetActive(false);
        models[weapon] = model;
        return model;
    }

    // Parents a gun to the hand so its TP grip sits on the hand, at the authored scale
    public static void Attach(Transform root, GunModel gun, Transform hand, Vector3 baseScale)
    {
        float s = gun != null ? gun.TpScale : 1f;
        Vector3 hs = hand.lossyScale;

        root.SetParent(hand, false);
        root.localScale = new Vector3(baseScale.x * s / hs.x, baseScale.y * s / hs.y, baseScale.z * s / hs.z);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;

        var grip = gun != null ? gun.TpGrip : null;
        if (grip == null) return;
        root.rotation = hand.rotation * Quaternion.Inverse(grip.rotation) * root.rotation;
        root.position += hand.position - grip.position;
    }

    // Swaps to the weapon's TP override, keeping the locomotion state and time
    private void ApplyController(WeaponSO weapon)
    {
        var vm = weapon != null ? weapon.viewmodel : null;
        var rc = vm != null && vm.thirdPersonController != null ? vm.thirdPersonController : baseController;
        if (rc == null || rc == animator.runtimeAnimatorController) return;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        animator.runtimeAnimatorController = rc;
        animator.Play(info.fullPathHash, 0, info.normalizedTime);
        layer = animator.GetLayerIndex(upperBodyLayer);
        if (layer >= 0) animator.SetLayerWeight(layer, layerWeight);
    }

    // ===== Procedural =====

    // Spreads pitch over the spine bones around the body's right axis
    private void ApplyPitch(float degrees)
    {
        if (Mathf.Approximately(degrees, 0f)) return;

        Vector3 axis = transform.right;
        float total = 0f;
        for (int i = 0; i < spine.Length; i++) if (spine[i] != null) total += pitchWeights[i];
        if (total <= 0f) return;

        for (int i = 0; i < spine.Length; i++)
        {
            if (spine[i] == null) continue;
            float share = pitchWeights[i] / total;
            spine[i].rotation = Quaternion.AngleAxis(degrees * share, axis) * spine[i].rotation;
        }
    }

    // Turns the spine so the barrel axis points along aimDir; the last bone takes the remainder
    private void AimSpine(Vector3 aimDir, float weight)
    {
        var gun = current.Root.transform;
        Vector3 axis = current.Gun.TpAimAxis;

        float remaining = 0f;
        for (int i = 0; i < spine.Length; i++) if (spine[i] != null) remaining += pitchWeights[i];
        if (remaining <= 0f) return;

        for (int i = 0; i < spine.Length; i++)
        {
            if (spine[i] == null) continue;
            float share = pitchWeights[i] / remaining;
            remaining -= pitchWeights[i];

            Vector3 barrel = gun.TransformDirection(axis);
            var delta = Quaternion.FromToRotation(barrel, aimDir);
            spine[i].rotation = Quaternion.Slerp(Quaternion.identity, delta, share * weight) * spine[i].rotation;
        }
    }

    // Analytic two-bone IK: bends the elbow to reach, then aims the shoulder
    private static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 targetPos, Quaternion targetRot, float weight)
    {
        if (a == null || b == null || c == null) return;

        Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
        Vector3 t = Vector3.Lerp(cPos, targetPos, weight);

        Vector3 ab = bPos - aPos, bc = cPos - bPos, ac = cPos - aPos, at = t - aPos;
        float abLen = ab.magnitude, bcLen = bc.magnitude;
        float acLen = ac.magnitude;
        float atLen = Mathf.Clamp(at.magnitude, 0.001f, abLen + bcLen - 0.001f);

        float oldAngle = TriangleAngle(acLen, abLen, bcLen);
        float newAngle = TriangleAngle(atLen, abLen, bcLen);

        Vector3 axis = Vector3.Cross(ab, bc);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ab, Vector3.up);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ab, Vector3.forward);
        axis.Normalize();

        b.rotation = Quaternion.AngleAxis((oldAngle - newAngle) * Mathf.Rad2Deg, axis) * b.rotation;

        ac = c.position - aPos;
        if (ac.sqrMagnitude > 1e-8f && at.sqrMagnitude > 1e-8f)
            a.rotation = Quaternion.FromToRotation(ac, at) * a.rotation;

        c.rotation = Quaternion.Slerp(c.rotation, targetRot, weight);
    }

    // Angle opposite side `opposite` in a triangle, radians
    private static float TriangleAngle(float opposite, float side1, float side2)
    {
        float denom = 2f * side1 * side2;
        if (denom <= 0f) return 0f;
        float cos = (side1 * side1 + side2 * side2 - opposite * opposite) / denom;
        return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
    }
}
