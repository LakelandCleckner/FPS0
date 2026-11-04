using UnityEngine;

// Draws the first-person legs mesh shifted back, without moving the skeleton, hitboxes or camera.
// The legs mesh is skinned to proxy bones that copy the real bones each frame, offset along the body's back.
[DefaultExecutionOrder(1000)]   // after animation, aim and IK adjustments in LateUpdate
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class FirstPersonLegsOffset : MonoBehaviour
{
    [Tooltip("How far back the legs are drawn (m).")]
    [SerializeField] private float offset = 0.2f;
    [Tooltip("Body whose back direction is used. Found as the Animator above if empty.")]
    [SerializeField] private Transform body;

    private SkinnedMeshRenderer smr;
    private Transform[] source;
    private Transform sourceRoot;
    private Transform[] proxies;
    private Transform container;

    private void Awake()
    {
        smr = GetComponent<SkinnedMeshRenderer>();
        if (body == null)
        {
            var anim = GetComponentInParent<Animator>();
            body = anim != null ? anim.transform : transform.parent;
        }

        source = smr.bones;
        sourceRoot = smr.rootBone;

        container = new GameObject(name + "_ProxyBones").transform;
        proxies = new Transform[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == null) continue;
            proxies[i] = new GameObject(source[i].name).transform;
            proxies[i].SetParent(container, false);
        }
    }

    private void OnEnable()
    {
        if (proxies == null) return;
        Sync();
        smr.bones = proxies;
        int r = sourceRoot != null ? System.Array.IndexOf(source, sourceRoot) : -1;
        if (r >= 0) smr.rootBone = proxies[r];
    }

    private void OnDisable()
    {
        if (smr == null || source == null) return;
        smr.bones = source;
        smr.rootBone = sourceRoot;
    }

    private void OnDestroy()
    {
        if (container != null) Destroy(container.gameObject);
    }

    private void LateUpdate()
    {
        if (smr.enabled) Sync();
    }

    // World pose of each real bone, shifted back
    private void Sync()
    {
        Vector3 shift = body != null ? -body.forward * offset : Vector3.zero;
        for (int i = 0; i < source.Length; i++)
        {
            var src = source[i];
            var p = proxies[i];
            if (src == null || p == null) continue;
            p.SetPositionAndRotation(src.position + shift, src.rotation);
            p.localScale = src.lossyScale;
        }
    }
}
