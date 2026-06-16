using UnityEngine;
using TMPro;
using Combat.Core;

namespace Combat.Feedback
{
    // A persistent "rolling" damage number (League/D4 spin-up). Unlike the
    // fire-and-forget DamageNumber, this one PERSISTS above a target, absorbing
    // repeated damage from one effect: each absorb adds to a running total, the
    // number climbs and re-sizes/re-formats in place, and grows as it climbs.
    // After a window of no new damage it releases (fades out, returns to pool).
    //
    // Keyed externally by (target, StatusSO) in the registry. Numbers in a
    // target's column are ordered biggest-total-at-the-bottom; the column offset
    // is SLID smoothly toward its target slot so reorders glide instead of snap.
    [RequireComponent(typeof(TextMeshPro))]
    public class AccumulatorNumber : MonoBehaviour
    {
        private TextMeshPro text;
        private DamageAccumulatorRegistry registry;

        public ITargetInfo Target { get; private set; }
        public object EffectKey { get; private set; }

        // current accumulated total — the registry sorts the column by this
        // (biggest at the bottom)
        public float Total => total;

        public bool Releasing => releasing;

        // approximate world height of this number, for cumulative column layout.
        public float Height => text.fontSize * heightPerFontUnit;
        private const float heightPerFontUnit = 0.12f;

        private DamageTypeSO type;
        private bool isCrit, isDebuffed;

        private float total;
        private float releaseWindow;
        private float sinceLastAbsorb;
        private bool releasing;
        private float releaseAge;
        private float releaseFadeTime;

        private Transform follow;
        private Vector3 targetOffset;   // where the column wants this number
        private Vector3 currentOffset;  // smoothed toward targetOffset
        private float slideSpeed;
        private Camera cam;

        private float minSize, maxSize, minLog, maxLog;

        private void Awake() { text = GetComponent<TextMeshPro>(); }

        public void Begin(
            DamageAccumulatorRegistry registry,
            ITargetInfo target,
            object effectKey,
            Transform follow,
            DamageTypeSO type,
            bool isCrit,
            bool isDebuffed,
            float releaseWindow,
            float releaseFadeTime,
            float slideSpeed,
            float minSize, float maxSize, float minLog, float maxLog)
        {
            this.registry = registry;
            Target = target;
            EffectKey = effectKey;
            this.follow = follow;
            this.type = type;
            this.isCrit = isCrit;
            this.isDebuffed = isDebuffed;
            this.releaseWindow = releaseWindow;
            this.releaseFadeTime = Mathf.Max(0.05f, releaseFadeTime);
            this.slideSpeed = slideSpeed;
            this.minSize = minSize; this.maxSize = maxSize;
            this.minLog = minLog; this.maxLog = maxLog;

            total = 0f;
            sinceLastAbsorb = 0f;
            releasing = false;
            releaseAge = 0f;
            targetOffset = Vector3.zero;
            currentOffset = Vector3.zero; // start where it's placed (no slide-in)

            if (type != null)
            {
                text.enableVertexGradient = true;
                text.colorGradient = type.GetGradient(isCrit, isDebuffed);
                if (type.font != null) text.font = type.font;
                text.fontStyle = type.GetStyle(isCrit, isDebuffed);
            }
            text.alpha = 1f;

            cam = Camera.main;
            gameObject.SetActive(true);
        }

        // Registry sets the DESIRED column offset; Update slides toward it.
        public void SetColumnOffset(Vector3 offset) => targetOffset = offset;

        public void Absorb(float amount)
        {
            if (releasing)
            {
                releasing = false;
                releaseAge = 0f;
                text.alpha = 1f;
            }
            total += amount;
            sinceLastAbsorb = 0f;
            Refresh();

            // total/size changed — column may need reordering + relayout
            if (registry != null) registry.RequestRepack(Target);
        }

        private void Refresh()
        {
            text.text = DamageNumberFormatter.Format(total);

            float logDist = DamageMagnitudeReference.Instance != null
                ? DamageMagnitudeReference.Instance.GetLogDistance(total)
                : 0f;
            float magT = Mathf.InverseLerp(minLog, maxLog, logDist);
            text.fontSize = Mathf.Lerp(minSize, maxSize, magT);
        }

        private void Update()
        {
            // smooth-slide the column offset toward its target slot
            float t = 1f - Mathf.Exp(-slideSpeed * Time.deltaTime);
            currentOffset = Vector3.Lerp(currentOffset, targetOffset, t);

            if (follow != null)
                transform.position = follow.position + Vector3.up * 2f + currentOffset;

            if (cam != null)
                transform.forward = cam.transform.forward;

            if (!releasing)
            {
                sinceLastAbsorb += Time.deltaTime;
                if (sinceLastAbsorb >= releaseWindow)
                    releasing = true;
            }
            else
            {
                releaseAge += Time.deltaTime;
                float a = Mathf.InverseLerp(releaseFadeTime, 0f, releaseAge);
                text.alpha = a;
                if (releaseAge >= releaseFadeTime)
                    End();
            }
        }

        private void End()
        {
            gameObject.SetActive(false);
            if (registry != null) registry.Release(this);
        }
    }
}