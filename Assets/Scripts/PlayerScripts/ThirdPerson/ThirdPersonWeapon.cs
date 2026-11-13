using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Weapons;

// Holds the held weapon's world model on the third-person body: grip in the right hand, barrel onto the
// animated left hand for long guns, aim, fire raise and kick, procedural reload and equip/stow lowering
[DefaultExecutionOrder(120)]
public class ThirdPersonWeapon : MonoBehaviour
{
    [Header("Refs (found automatically if empty)")]
    [SerializeField] private PlayerPresentation presentation;
    [SerializeField] private Animator animator;
    [SerializeField] private BodyVisibility visibility;
    [Tooltip("Where the left hand fetches mags and shells. Found as TP_Belt under the body if empty.")]
    [SerializeField] private Transform belt;

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

    [Header("Raise on fire")]
    [Tooltip("Seconds to snap from low ready to shouldered when firing at hip.")]
    [SerializeField] private float raiseTime = 0.08f;
    [Tooltip("Seconds the gun stays shouldered after the last shot.")]
    [SerializeField] private float holdAfterShot = 0.6f;
    [Tooltip("Seconds to lower back to low ready.")]
    [SerializeField] private float lowerTime = 0.25f;

    [Header("Fire kick")]
    [Tooltip("Upward upper-body kick per shot, in degrees.")]
    [SerializeField] private float kickDegrees = 3f;
    [Tooltip("Kick never lasts longer than this, even on slow weapons.")]
    [SerializeField] private float maxKickTime = 0.15f;

    [Header("Reload")]
    [Tooltip("Gun roll during a reload, in degrees. Flip the sign if it rolls away from the body.")]
    [SerializeField] private float reloadTilt = 25f;
    [Tooltip("Seconds to roll the gun in or out.")]
    [SerializeField] private float tiltTime = 0.15f;
    [Tooltip("Seconds for the hand to return to the gun after a cancel.")]
    [SerializeField] private float cancelReturnTime = 0.15f;

    [Header("Equip / stow")]
    [Tooltip("Right arm swing down when fully stowed, in degrees.")]
    [SerializeField] private float lowerDegrees = 50f;

    [Header("Left hand")]
    [Tooltip("Seconds to blend the left hand onto the gun (and the barrel onto the left hand) on or off.")]
    [SerializeField] private float supportBlend = 0.15f;

    private sealed class WorldModel
    {
        public GameObject Root;
        public GunModel Gun;
        public Renderer[] Renderers;
        public Vector3 BaseLocalPos;
        public Quaternion BaseLocalRot;
    }

    private enum HandState { Hold, Mag, ShellStart, ShellLoad, ShellEnd, Return }

    private static readonly int AimBlendParam = Animator.StringToHash("AimBlend");

    private readonly Dictionary<WeaponSO, WorldModel> models = new Dictionary<WeaponSO, WorldModel>();
    private WorldModel current;
    private WeaponSO currentWeapon;
    private RuntimeAnimatorController baseController;

    private int layer = -1;
    private float layerWeight;
    private float supportWeight;
    private float alignWeight;

    // Where the animated left palm sits on the gun this frame, in the gun's space (fallback when the gun has no TP_Support)
    private Vector3 glueLocalPos;
    private Quaternion glueLocalRot = Quaternion.identity;

    private float kickTime = -1f;
    private float kickDuration;
    private float fireRaise;
    private float lastShotTime = float.NegativeInfinity;

    private HandState hand = HandState.Hold;
    private float handTime;
    private ReloadInfo reload;
    private float returnDuration;
    private Vector3 returnFrom;
    private Vector3 lastTarget;
    private float tilt;

    private float lowered;
    private float lowerTarget;
    private float lowerRate;

    private Transform rightHand, rightUpper, leftUpper, leftLower, leftHand, leftMiddle, hips;
    private readonly Transform[] spine = new Transform[3];

    // Pose aim: ADS or fire raise, whichever is higher
    public float PoseAim { get; private set; }

    private void Awake()
    {
        if (presentation == null) presentation = GetComponentInParent<PlayerPresentation>();
        if (animator == null) animator = GetComponent<Animator>();
        if (visibility == null) visibility = GetComponent<BodyVisibility>();
        if (animator == null) return;

        baseController = animator.runtimeAnimatorController;
        layer = animator.GetLayerIndex(upperBodyLayer);

        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        leftMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
        spine[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
        spine[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        if (belt == null)
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "TP_Belt") { belt = t; break; }
    }

    private void OnEnable()
    {
        if (presentation == null) return;
        presentation.EquipStarted += HandleEquipStarted;
        presentation.StowStarted += HandleStowStarted;
        presentation.WeaponEquipped += Show;
        presentation.Fired += HandleFired;
        presentation.ReloadStarted += HandleReloadStarted;
        presentation.ReloadCancelled += HandleReloadCancelled;
        presentation.ReloadInterrupted += HandleReloadInterrupted;
    }

    private void OnDisable()
    {
        if (presentation == null) return;
        presentation.EquipStarted -= HandleEquipStarted;
        presentation.StowStarted -= HandleStowStarted;
        presentation.WeaponEquipped -= Show;
        presentation.Fired -= HandleFired;
        presentation.ReloadStarted -= HandleReloadStarted;
        presentation.ReloadCancelled -= HandleReloadCancelled;
        presentation.ReloadInterrupted -= HandleReloadInterrupted;
    }

    // ===== Events =====

    // A new model comes up from the bottom; a reversed stow carries on from where it is
    private void HandleEquipStarted(WeaponSO weapon, float duration)
    {
        bool changed = weapon != currentWeapon;
        Show(weapon);
        if (changed) lowered = 1f;
        SetLower(0f, duration);
    }

    private void HandleStowStarted(WeaponSO weapon, float duration)
    {
        SetLower(1f, duration);
        BeginReturn(cancelReturnTime);
    }

    private void HandleFired(float interval)
    {
        if (current == null) return;
        lastShotTime = Time.time;
        kickDuration = Mathf.Max(0.01f, Mathf.Min(interval, maxKickTime));
        kickTime = 0f;
    }

    private void HandleReloadStarted(ReloadInfo info)
    {
        if (current == null) return;
        reload = info;
        hand = info.Shell ? HandState.ShellStart : HandState.Mag;
        handTime = 0f;
    }

    private void HandleReloadCancelled() => BeginReturn(cancelReturnTime);
    private void HandleReloadInterrupted(float ready) => BeginReturn(ready);

    // Moves from the current lowered amount to target over duration
    private void SetLower(float target, float duration)
    {
        lowerTarget = target;
        lowerRate = Mathf.Abs(target - lowered) / Mathf.Max(0.01f, duration);
    }

    private void BeginReturn(float duration)
    {
        if (hand == HandState.Hold || hand == HandState.Return) return;
        hand = HandState.Return;
        handTime = 0f;
        returnDuration = Mathf.Max(0.01f, duration);
        returnFrom = lastTarget;
    }

    // ===== Frame =====

    private void Update()
    {
        if (animator == null) return;
        float dt = Time.deltaTime;

        // Follow the held weapon; covers startup, where no equip event fires
        if (presentation != null && presentation.State.HeldWeapon != currentWeapon)
            Show(presentation.State.HeldWeapon);

        layerWeight = Mathf.MoveTowards(layerWeight, current != null ? 1f : 0f, dt / Mathf.Max(0.01f, layerFade));
        if (layer >= 0) animator.SetLayerWeight(layer, layerWeight);

        bool raised = current != null && Time.time - lastShotTime < holdAfterShot;
        float raiseRate = 1f / Mathf.Max(0.01f, raised ? raiseTime : lowerTime);
        fireRaise = Mathf.MoveTowards(fireRaise, raised ? 1f : 0f, raiseRate * dt);
        PoseAim = Mathf.Max(presentation != null ? presentation.State.AimBlend : 0f, fireRaise);
        animator.SetFloat(AimBlendParam, PoseAim);

        float blendRate = dt / Mathf.Max(0.01f, supportBlend);
        bool hasGun = current != null && current.Gun != null;
        supportWeight = Mathf.MoveTowards(supportWeight, hasGun ? 1f : 0f, blendRate);
        alignWeight = Mathf.MoveTowards(alignWeight, hasGun && hand == HandState.Hold ? 1f : 0f, blendRate);

        lowered = Mathf.MoveTowards(lowered, lowerTarget, lowerRate * dt);

        TickHand(dt);
        bool tilted = hand == HandState.Mag ? handTime < reload.Duration * 0.75f
                    : hand == HandState.ShellStart || hand == HandState.ShellLoad;
        tilt = Mathf.MoveTowards(tilt, tilted ? 1f : 0f, dt / Mathf.Max(0.01f, tiltTime));
    }

    // Advances the reload hand path; stops when the ammo says the reload is over
    private void TickHand(float dt)
    {
        if (hand == HandState.Hold) return;
        handTime += dt;
        bool reloading = presentation != null && presentation.State.Reloading;

        switch (hand)
        {
            case HandState.Mag:
                if (handTime >= reload.Duration || !reloading) hand = HandState.Hold;
                break;

            case HandState.ShellStart:
                if (!reloading) BeginReturn(cancelReturnTime);
                else if (handTime >= reload.ShellStart) { hand = HandState.ShellLoad; handTime = 0f; }
                break;

            case HandState.ShellLoad:
                if (!reloading) { BeginReturn(cancelReturnTime); break; }
                if (handTime < reload.ShellStep) break;
                handTime -= reload.ShellStep;
                if (!presentation.State.ShellContinue) { hand = HandState.ShellEnd; handTime = 0f; }
                break;

            case HandState.ShellEnd:
                if (handTime >= reload.ShellEnd) hand = HandState.Hold;
                break;

            case HandState.Return:
                if (handTime >= returnDuration) hand = HandState.Hold;
                break;
        }
    }

    // After animation: aim, lower, roll, then the left hand onto the moved gun
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
        bool holding = current != null && current.Gun != null && layerWeight > 0f;
        if (holding) HoldGun();

        if (holding)
        {
            float aimWeight = layerWeight * Mathf.Lerp(hipAimWeight, 1f, PoseAim) * (1f - lowered);
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

        if (!holding) return;

        if (lowered > 0f && rightUpper != null)
            rightUpper.rotation = Quaternion.AngleAxis(lowerDegrees * Mathf.SmoothStep(0f, 1f, lowered), transform.right) * rightUpper.rotation;

        if (tilt > 0f && rightHand != null)
        {
            Vector3 barrel = current.Root.transform.TransformDirection(current.Gun.TpAimAxis);
            rightHand.rotation = Quaternion.AngleAxis(reloadTilt * Mathf.SmoothStep(0f, 1f, tilt), barrel) * rightHand.rotation;
        }

        // Left palm onto TP_Support with its rotation (the clip's own left hand is rolled on these bodies);
        // without a support point it stays where the animation put it on the gun
        if (supportWeight > 0f && leftHand != null)
        {
            var root = current.Root.transform;
            var support = current.Gun.TpLeftHandIK ? current.Gun.TpSupport : null;
            Vector3 hold = support != null ? support.position : root.TransformPoint(glueLocalPos);
            Quaternion holdRot = support != null ? support.rotation : root.rotation * glueLocalRot;
            Vector3 target = LeftHandTarget(hold);
            lastTarget = target;
            PlaceLeftHand(leftUpper, leftLower, leftHand, leftMiddle, target, holdRot, supportWeight);
        }
    }

    // Gun back to its grip pose, turned onto the animated left hand for two-hand guns; notes where that hand sits on it
    private void HoldGun()
    {
        var root = current.Root.transform;
        root.localPosition = current.BaseLocalPos;
        root.localRotation = current.BaseLocalRot;
        if (leftHand == null) return;

        AlignToLeftHand(root, current.Gun, PalmOf(leftHand, leftMiddle), alignWeight);
        glueLocalPos = root.InverseTransformPoint(PalmOf(leftHand, leftMiddle));
        glueLocalRot = Quaternion.Inverse(root.rotation) * leftHand.rotation;
    }

    // ===== Reload hand path =====

    // Where the left hand should be this frame, given the reload state
    private Vector3 LeftHandTarget(Vector3 sup)
    {
        var gun = current.Gun;
        Vector3 mag = gun.TpMagWell != null ? gun.TpMagWell.position : sup;
        Vector3 port = gun.TpLoadPort != null ? gun.TpLoadPort.position : mag;
        Vector3 pouch = BeltPosition();

        switch (hand)
        {
            case HandState.Mag:
                return MagPath(sup, mag, pouch, Frac(handTime, reload.Duration));

            case HandState.ShellStart:
                return Ease(sup, pouch, Frac(handTime, reload.ShellStart));

            case HandState.ShellLoad:
                return ShellPath(pouch, port, Frac(handTime, reload.ShellStep));

            case HandState.ShellEnd:
                return Ease(pouch, sup, Frac(handTime, reload.ShellEnd));

            case HandState.Return:
                return Ease(returnFrom, sup, handTime / returnDuration);

            default:
                return sup;
        }
    }

    private Vector3 BeltPosition() => BeltPosition(belt, hips, transform);

    // Belt point, or front-left of the hips when there is none
    public static Vector3 BeltPosition(Transform belt, Transform hips, Transform body)
    {
        if (belt != null) return belt.position;
        Vector3 origin = hips != null ? hips.position : body.position + Vector3.up;
        return origin - body.right * 0.12f + body.forward * 0.1f;
    }

    // Mag reload, p 0..1: hold -> mag well -> belt -> mag well (seat) -> hold
    public static Vector3 MagPath(Vector3 sup, Vector3 mag, Vector3 pouch, float p)
    {
        if (p < 0.15f) return Ease(sup, mag, p / 0.15f);
        if (p < 0.35f) return Ease(mag, pouch, (p - 0.15f) / 0.2f);
        if (p < 0.6f) return Ease(pouch, mag, (p - 0.35f) / 0.25f);
        if (p < 0.75f) return mag;
        return Ease(mag, sup, (p - 0.75f) / 0.25f);
    }

    // One shell, c 0..1: belt -> port -> belt
    public static Vector3 ShellPath(Vector3 pouch, Vector3 port, float c)
        => c < 0.6f ? Ease(pouch, port, c / 0.6f) : Ease(port, pouch, (c - 0.6f) / 0.4f);

    private static Vector3 Ease(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
    private static float Frac(float time, float duration) => duration > 0f ? Mathf.Clamp01(time / duration) : 1f;

    // ===== Weapon model =====

    private void Show(WeaponSO weapon)
    {
        if (weapon == currentWeapon) return;
        currentWeapon = weapon;
        hand = HandState.Hold;
        tilt = 0f;

        if (current != null) current.Root.SetActive(false);
        current = weapon != null ? Acquire(weapon) : null;
        if (current != null) current.Root.SetActive(true);

        if (visibility != null) visibility.SetWeaponRenderers(current != null ? current.Renderers : Array.Empty<Renderer>());
        ApplyController(weapon);
    }

    private WorldModel Acquire(WeaponSO weapon)
    {
        if (models.TryGetValue(weapon, out var cached)) return cached;

        var go = rightHand != null ? SpawnWorldModel(weapon) : null;
        if (go == null)
        {
            models[weapon] = null;
            return null;
        }
        var prefab = weapon.viewmodel.gunPrefab;

        var visible = new List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.enabled) visible.Add(r);

        var gun = go.GetComponent<GunModel>();
        Attach(go.transform, gun, rightHand, prefab.transform.localScale);
        if (gun == null || gun.TpGrip == null)
            Debug.LogWarning($"[ThirdPersonWeapon] '{prefab.name}' has no TP grip; run Tools > TP Grip Tool.");

        var model = new WorldModel
        {
            Root = go, Gun = gun, Renderers = visible.ToArray(),
            BaseLocalPos = go.transform.localPosition, BaseLocalRot = go.transform.localRotation
        };
        go.SetActive(false);
        models[weapon] = model;
        return model;
    }

    // Static world copy of a weapon's gun prefab: skin and shader, no gun animation, no colliders, Default layer
    public static GameObject SpawnWorldModel(WeaponSO weapon)
    {
        var prefab = weapon != null && weapon.viewmodel != null ? weapon.viewmodel.gunPrefab : null;
        if (prefab == null) return null;

        var go = Instantiate(prefab);
        go.name = "TP_" + prefab.name;
        WeaponLoadout.ApplySkin(go, weapon.viewmodelSkin);
        if (weapon.defaultShader != null) weapon.defaultShader.ApplyTo(go);

        foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        return go;
    }

    // Left palm: halfway from the wrist to the middle knuckle
    public static Vector3 PalmOf(Transform hand, Transform middle)
        => middle != null ? Vector3.Lerp(hand.position, middle.position, 0.5f) : hand.position;

    // Puts the left palm (not the wrist) on palmTarget with the hand at handRot, elbow kept on the animation's side
    public static void PlaceLeftHand(Transform upper, Transform lower, Transform hand, Transform middle,
                                     Vector3 palmTarget, Quaternion handRot, float weight)
    {
        if (upper == null || lower == null || hand == null || weight <= 0f) return;
        Quaternion rot = Quaternion.Slerp(hand.rotation, handRot, weight);
        Vector3 palmOffset = rot * (Quaternion.Inverse(hand.rotation) * (PalmOf(hand, middle) - hand.position));
        SolveTwoBone(upper, lower, hand, palmTarget - palmOffset, handRot, weight, weight);
    }

    // Two Hand only: turns the gun about its grip so the barrel runs toward the left palm. False otherwise.
    public static bool AlignToLeftHand(Transform root, GunModel gun, Vector3 palm, float weight)
    {
        if (gun == null || gun.TpGrip == null || weight <= 0f || gun.TpHold != TpHold.TwoHand) return false;

        Vector3 grip = gun.TpGrip.position;
        Vector3 toPalm = palm - grip;

        // A palm behind or beside the grip means the hand is off the gun: don't spin the gun round to it
        Vector3 barrel = root.TransformDirection(gun.TpAimAxis);
        if (Vector3.Angle(barrel, toPalm) > 75f) return false;

        var delta = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(barrel, toPalm), weight);
        root.SetPositionAndRotation(grip + delta * (root.position - grip), delta * root.rotation);
        return true;
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

    private void AimSpine(Vector3 aimDir, float weight)
        => AimSpine(current.Root.transform, current.Gun.TpAimAxis, spine, pitchWeights, aimDir, weight);

    // Turns the spine so the gun's barrel axis points along aimDir; the last bone takes the remainder
    public static void AimSpine(Transform gun, Vector3 axis, Transform[] spine, Vector3 weights, Vector3 aimDir, float weight)
    {
        float remaining = 0f;
        for (int i = 0; i < spine.Length; i++) if (spine[i] != null) remaining += weights[i];
        if (remaining <= 0f) return;

        for (int i = 0; i < spine.Length; i++)
        {
            if (spine[i] == null) continue;
            float share = weights[i] / remaining;
            remaining -= weights[i];

            Vector3 barrel = gun.TransformDirection(axis);
            var delta = Quaternion.FromToRotation(barrel, aimDir);
            spine[i].rotation = Quaternion.Slerp(Quaternion.identity, delta, share * weight) * spine[i].rotation;
        }
    }

    // Analytic two-bone IK: bends the elbow to reach, then aims the shoulder
    public static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 targetPos, Quaternion targetRot,
                                    float weight, float rotationWeight)
    {
        if (a == null || b == null || c == null) return;

        Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
        Vector3 hint = bPos;   // the animation's elbow: keep the arm bending to that side
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
        {
            a.rotation = Quaternion.FromToRotation(ac, at) * a.rotation;

            // Twist about shoulder-to-target (hand stays put) so the elbow returns to the animation's side
            Vector3 n = at.normalized;
            Vector3 e = Vector3.ProjectOnPlane(b.position - aPos, n);
            Vector3 h = Vector3.ProjectOnPlane(hint - aPos, n);
            if (e.sqrMagnitude > 1e-6f && h.sqrMagnitude > 1e-6f)
                a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(e, h, n), n) * a.rotation;
        }

        if (rotationWeight > 0f) c.rotation = Quaternion.Slerp(c.rotation, targetRot, rotationWeight);
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
